using System;
using System.Collections.Generic;
using UnityEngine;

public enum TroopGroupState { Idle, Attacking, Fleeing, Harvesting, Scouting, Moving, Depositing }
public enum TroopGroupTask { Nothing, Defend, Attack, Harvest, Scout }

/// <summary>A player's controllable collection of troops and its shared orders.</summary>
[DisallowMultipleComponent]
public class TroopGroup : MonoBehaviour
{
    [SerializeField] private int maxUnits = 20;
    [SerializeField] private int groupId = -1;
    [SerializeField] private bool refillUnits;
    private TroopAI troopAI;
    private readonly List<Troop> units = new List<Troop>();
    private TroopInteractor owner;

    public IReadOnlyList<Troop> Units => units;
    public int MaxUnits { get => maxUnits; set => maxUnits = Mathf.Max(1, value); }
    public int GroupId => groupId;
    public bool RefillUnits { get => refillUnits; set => refillUnits = value; }
    public TroopInteractor Owner => owner;
    private TroopGroupState state = TroopGroupState.Idle;
    public TroopGroupState State
    {
        get => state;
        internal set { state = value; }
    }
    public TroopGroupTask Task { get; private set; } = TroopGroupTask.Nothing;
    public event Action<Troop> UnitDespawned;
    public Vector3 ObjectivePosition { get; private set; }
    public Troop TargetTroop { get; private set; }
    public Building TargetBuilding { get; private set; }
    public Resource TargetResource { get; private set; }
    public bool attackingBuilding;
    public bool respondingToAttack;
    public ulong OwnerClientId => owner != null ? owner.OwnerClientId : ulong.MaxValue;
    public TroopGroup TargetGroup { get; private set; }
    public TroopAI AI => troopAI;

    private void Awake()
    {
        if (troopAI == null) troopAI = GetComponent<TroopAI>();
        if (troopAI == null) troopAI = gameObject.AddComponent<TroopAI>();
        troopAI.Initialize(this);
    }

    internal void SetOwner(TroopInteractor interactor) { owner = interactor; }
    internal void SetGroupId(int id) { groupId = id; }
    public bool AddUnit(Troop troop)
    {
        if (troop == null || units.Contains(troop) || units.Count >= maxUnits) return false;
        units.Add(troop);
        troop.Group = this;
        return true;
    }

    public void RemoveUnit(Troop troop)
    {
        if (troop == null || !units.Remove(troop)) return;
        UnitDespawned?.Invoke(troop);
        troop.Group = null;
        if (refillUnits && owner != null) owner.RequestReplacementFor(this, troop.Type, troop.groupSpriteIndex);
    }

    public void Prune() { for (int i = units.Count - 1; i >= 0; i--) if (units[i] == null) units.RemoveAt(i); }

    public static List<Troop> GetUnitsInSameGroup(Troop member)
    {
        List<Troop> result = new List<Troop>();
        if (member == null || member.TroopGroupId.Value < 0) return result;
        foreach (Troop troop in FindObjectsByType<Troop>(FindObjectsSortMode.None))
            if (troop != null && troop.OwnerClientId == member.OwnerClientId && troop.TroopGroupId.Value == member.TroopGroupId.Value)
                result.Add(troop);
        return result;
    }

    public void SetTask(TroopGroupTask task, Vector3 position = default)
    {
        Debug.Log($"[TroopGroup] Group {groupId} assigned task {task} at {position}");
        owner?.RequestGroupOrder(this, task, position);
        StopAll();
        Task = task; ObjectivePosition = position; TargetTroop = null; TargetBuilding = null; TargetResource = null;
        State = task == TroopGroupTask.Harvest ? TroopGroupState.Harvesting : task == TroopGroupTask.Scout ? TroopGroupState.Scouting : task == TroopGroupTask.Defend ? TroopGroupState.Idle : TroopGroupState.Moving;
        if (task == TroopGroupTask.Defend) troopAI.SetDefendPosition(position);
        else if (task == TroopGroupTask.Scout || task == TroopGroupTask.Attack) MoveAll(position);
        else if (task == TroopGroupTask.Harvest) troopAI.BeginHarvest();
    }

    public void Attack(Troop target)
    {
        if (target == null) return;
        foreach (Troop troop in Snapshot()) troop.StopCurrentTask();
        TargetTroop = target; TargetBuilding = null;
        if (target.Group != null) SetTargetGroup(target.Group);
        MoveAll(target.transform.position);
        State = TroopGroupState.Attacking;
    }

    public void Attack(Building target)
    {
        if (target == null) return;
        foreach (Troop troop in Snapshot()) troop.StopCurrentTask();
        TargetTroop = null; TargetBuilding = target;
        if (target != null) SetTargetBuilding(target);
        MoveAll(target.transform.position);
        State = TroopGroupState.Attacking;
    }

    public void AttackGroup(TroopGroup enemyGroup)
    {
        if (enemyGroup == null || enemyGroup == this || enemyGroup.Units.Count <= 0) return;
        TargetGroup = enemyGroup;
        AttackRandom(enemyGroup);
    }

    public void AttackBuilding(Building enemyBuilding)
    {
        if (enemyBuilding == null) return;
        // cíl měníme jen když ten původní neexistuje nebo je zničený
        if (TargetBuilding == null || TargetBuilding.Health.Value <= 0) TargetBuilding = enemyBuilding;
        AttackRandom(TargetBuilding);
    }

    private void AttackRandom(TroopGroup enemyGroup)
    {
        if (enemyGroup == null || enemyGroup == this) return;
        enemyGroup.Prune();
        List<Troop> validTargets = new List<Troop>();
        foreach (Troop candidate in enemyGroup.Units)
            if (candidate != null && candidate.Health.Value > 0)
                validTargets.Add(candidate);
        if (validTargets.Count == 0) return;

        State = TroopGroupState.Attacking;
        foreach (Troop troop in units)
        {
            if (troop == null) continue;
            if (troop.hasTarget()) continue;
            Troop target = validTargets[UnityEngine.Random.Range(0, validTargets.Count)];
            Debug.Log($"[TroopGroup] Group {groupId} attacking enemy group {enemyGroup.GroupId} owned by {enemyGroup.OwnerClientId} with troop {troop.Type} targeting {target.Type}");
            troop.Attack(target);
            troop.MoveTo(target.transform.position);
        }
    }

    private void AttackRandom(Building enemyBuilding)
    {
        if (enemyBuilding == null || enemyBuilding.OwnerClientId == OwnerClientId) 
        {
            Debug.Log($"[TroopGroup] Building neexistuje nebo je vlastněn stejným hráčem");
            return;
        }

        if (enemyBuilding.Health.Value <= 0)
        {
            Debug.Log($"[TroopGroup] Building je už zničený a nemá životy");
            return;
        }

        State = TroopGroupState.Attacking;
        foreach (Troop troop in units)
        {
            if (troop == null) continue;
            if (troop.hasTarget()) continue;
            Building target = enemyBuilding;
            Debug.Log($"[TroopGroup] Group {groupId} attacking enemy building {enemyBuilding.Type} owned by {enemyBuilding.OwnerClientId} with troop {troop.Type} targeting {target.Type}");
            troop.Attack(target);
            troop.MoveTo(target.transform.position);
        }
    }

    internal void HandleEnemyGroupDetected(TroopGroup enemyGroup)
    {
        if(State == TroopGroupState.Attacking) return;
        if (enemyGroup == null || enemyGroup == this) return;
        Debug.Log($"[TroopGroup] Group {groupId} detected enemy group {enemyGroup.GroupId} owned by {enemyGroup.OwnerClientId}");
        if (enemyGroup.State == TroopGroupState.Attacking || Task == TroopGroupTask.Attack)
        {
            AttackGroup(enemyGroup);
            MarkAttacked(enemyGroup);
            State = TroopGroupState.Attacking;
        }
    }

    private void SetTargetGroup(TroopGroup target)
    {
        TargetGroup = target;
    }
    private void SetTargetBuilding(Building target)
    {
        attackingBuilding = true;
        respondingToAttack = false;
        TargetBuilding = target;
    }

    private static Troop SelectWeakest(IReadOnlyList<Troop> candidates)
    {
        Troop target = null;
        float score = float.MaxValue;
        foreach (Troop candidate in candidates)
        {
            if (candidate == null) continue;
            float value = candidate.Health.Value + candidate.Damage * Mathf.Max(1f, candidate.AttackRange);
            if (value < score) { score = value; target = candidate; }
        }
        return target;
    }


    public void MoveAll(Vector3 destination)
    {
        if (Task == TroopGroupTask.Attack) State = TroopGroupState.Idle;
        else if (Task == TroopGroupTask.Scout) State = TroopGroupState.Scouting;
        else if (Task != TroopGroupTask.Harvest && Task != TroopGroupTask.Defend) State = TroopGroupState.Moving;
        Prune();
        int count = units.Count;
        for (int i = 0; i < count; i++) units[i].RequestMove(destination, i, count);
    }

    public void MoveOrder(Vector3 destination)
    {
        owner?.RequestGroupOrder(this, TroopGroupTask.Nothing, destination);
        StopAll();
        TargetTroop = null; TargetBuilding = null; TargetResource = null;
        MoveAll(destination);
    }

    public void MoveUnit(Troop troop, Vector3 destination) { if (units.Contains(troop)) troop.RequestMove(destination); }
    public void AssignUnit(Troop troop, Building building) { if (units.Contains(troop) && building != null) troop.OnSelectBuilding(building); }
    public void AssignUnit(Troop troop, Troop target) { if (units.Contains(troop) && target != null) troop.OnSelectTroop(target); }
    public void StopAll()
    {
        Prune(); foreach (Troop troop in units) troop.StopCurrentTask();
        Task = TroopGroupTask.Nothing; State = TroopGroupState.Idle;
    }
    public void AssignAll(Building building) { if (building == null) return; foreach (Troop troop in Snapshot()) troop.OnSelectBuilding(building); }
    public void AssignAll(Troop target) { if (target == null) return; foreach (Troop troop in Snapshot()) troop.OnSelectTroop(target); }
    public void Harvest(Resource resource = null)
    {
        StopAll();
        TargetResource = resource; Task = TroopGroupTask.Harvest; State = TroopGroupState.Harvesting;
        troopAI.BeginHarvest();
    }

    internal void MarkAttacked(TroopGroup attacker)
    {
        if (attacker == null) return;
        Debug.Log($"[TroopGroup] Group {groupId} attacked by {(attacker != null ? attacker.groupId.ToString() : "unknown")}");
        troopAI.RespondTo(attacker);
    }
    internal void MarkAttacked(Building attacker)
    {
        if (attacker == null) return;
        Debug.Log($"[TroopGroup] Group {groupId} attacked by {(attacker != null ? attacker.Type : "unknown")}");
        troopAI.RespondTo(attacker);
    }

    private List<Troop> Snapshot() { Prune(); return new List<Troop>(units); }
}