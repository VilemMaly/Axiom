using System;
using System.Collections.Generic;
using UnityEngine;

public enum TroopGroupState { Idle, Attacked, Attacking, Fleeing, Harvesting, Scouting, Moving, Depositing }
public enum TroopGroupTask { Nothing, Defend, Attack, Harvest, Scout }

/// <summary>A player's controllable collection of troops and its shared orders.</summary>
[DisallowMultipleComponent]
public class TroopGroup : MonoBehaviour
{
    [SerializeField] private int maxUnits = 20;
    [SerializeField] private int groupId = -1;
    [SerializeField] private bool refillUnits;
    [SerializeField] private TroopAI troopAI;
    private readonly List<Troop> units = new List<Troop>();
    private TroopInteractor owner;

    public IReadOnlyList<Troop> Units => units;
    public int MaxUnits { get => maxUnits; set => maxUnits = Mathf.Max(1, value); }
    public int GroupId => groupId;
    public bool RefillUnits { get => refillUnits; set => refillUnits = value; }
    private TroopGroupState state = TroopGroupState.Idle;
    public TroopGroupState State
    {
        get => state;
        internal set { state = value; SyncGroupState(); }
    }
    public TroopGroupTask Task { get; private set; } = TroopGroupTask.Nothing;
    public event Action<Troop> UnitDespawned;
    public Vector3 ObjectivePosition { get; private set; }
    public Troop TargetTroop { get; private set; }
    public Building TargetBuilding { get; private set; }
    public Resource TargetResource { get; private set; }
    public ulong OwnerClientId => owner != null ? owner.OwnerClientId : ulong.MaxValue;
    public TroopAI AI => troopAI;
    public int TargetGroupId { get; private set; } = -1;
    public ulong TargetGroupOwnerId { get; private set; } = ulong.MaxValue;

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
        SyncGroupState();
        return true;
    }

    public Troop GetTroop(int index) { return (index >= 0 && units[index] != null) ? units[index] : null; }

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
        TargetTroop = target; TargetBuilding = null; Task = TroopGroupTask.Attack;
        if (target.Group != null) SetTargetGroup(target.Group);
        ObjectivePosition = target.transform.position; State = TroopGroupState.Attacking;
        MoveAll(ObjectivePosition);
    }

    public void AttackGroup(TroopGroup enemyGroup)
    {
        if (enemyGroup == null || enemyGroup == this || enemyGroup.Units.Count == 0) return;
        TargetGroupId = enemyGroup.GroupId;
        TargetGroupOwnerId = enemyGroup.OwnerClientId;
        Troop target = SelectWeakest(enemyGroup.Units);
        if (target != null) Attack(target);
    }

    internal void HandleEnemyGroupDetected(TroopGroup enemyGroup)
    {
        if (enemyGroup == null || enemyGroup == this) return;
        if (enemyGroup.State == TroopGroupState.Attacking || Task == TroopGroupTask.Attack)
        {
            if (Task != TroopGroupTask.Attack || TargetGroupId != enemyGroup.GroupId || TargetGroupOwnerId != enemyGroup.OwnerClientId)
                AttackGroup(enemyGroup);
            State = TroopGroupState.Attacking;
        }
    }

    private void SetTargetGroup(TroopGroup target)
    {
        TargetGroupId = target != null ? target.GroupId : -1;
        TargetGroupOwnerId = target != null ? target.OwnerClientId : ulong.MaxValue;
        SyncGroupState();
    }

    private void SyncGroupState()
    {
        foreach (Troop troop in units)
        {
            if (troop == null || !troop.IsSpawned) continue;
            if (troop.IsServer)
            {
                troop.SyncedGroupState.Value = (int)state;
                troop.TargetGroupId.Value = TargetGroupId;
                troop.TargetGroupOwnerId.Value = TargetGroupOwnerId;
            }
            else troop.RequestGroupStateSync((int)state, TargetGroupId, TargetGroupOwnerId);
        }
    }

    internal void ApplySyncedState(TroopGroupState syncedState, int targetGroupId, ulong targetOwnerId)
    {
        state = syncedState;
        TargetGroupId = targetGroupId;
        TargetGroupOwnerId = targetOwnerId;
        if (syncedState == TroopGroupState.Attacking && targetGroupId >= 0)
        {
            Task = TroopGroupTask.Attack;
            TargetTroop = null;
            foreach (Troop candidate in FindObjectsByType<Troop>(FindObjectsSortMode.None))
                if (candidate != null && candidate.OwnerClientId == targetOwnerId && candidate.TroopGroupId.Value == targetGroupId)
                { TargetTroop = candidate; break; }
        }
        else if (syncedState != TroopGroupState.Attacking && Task == TroopGroupTask.Attack)
        {
            Task = TroopGroupTask.Nothing;
            TargetTroop = null;
            TargetBuilding = null;
        }
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

    public void Attack(Building target)
    {
        if (target == null) return;
        foreach (Troop troop in Snapshot()) troop.StopCurrentTask();
        TargetBuilding = target; TargetTroop = null; Task = TroopGroupTask.Attack;
        TargetGroupId = -1; TargetGroupOwnerId = ulong.MaxValue;
        ObjectivePosition = target.transform.position; State = TroopGroupState.Attacking;
        MoveAll(ObjectivePosition);
    }

    public void MoveAll(Vector3 destination)
    {
        ObjectivePosition = destination;
        if (Task == TroopGroupTask.Attack) State = TroopGroupState.Attacking;
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
        TargetGroupId = -1; TargetGroupOwnerId = ulong.MaxValue;
        Task = TroopGroupTask.Nothing; State = TroopGroupState.Idle;
    }
    public void AssignAll(Building building) { if (building == null) return; foreach (Troop troop in Snapshot()) troop.OnSelectBuilding(building); }
    public void AssignAll(Troop target) { if (target == null) return; foreach (Troop troop in Snapshot()) troop.OnSelectTroop(target); }
    public void Harvest(Resource resource = null)
    {
        Vector3 position = resource != null ? resource.transform.position : ObjectivePosition;
        if (resource == null && position == Vector3.zero && units.Count > 0 && units[0] != null)
            position = units[0].transform.position;
        SetTask(TroopGroupTask.Harvest, position);
        TargetResource = resource;
    }
    public void BuildAt(int buildingIndex, Building.BuildingType type, Vector3 position, int rotation = 0)
    {
        StopAll();
        foreach (Troop troop in Snapshot()) if (troop is Cargo cargo) cargo.RequestMoveToBuild(position, buildingIndex, rotation);
        Task = TroopGroupTask.Nothing; ObjectivePosition = position; State = TroopGroupState.Moving;
    }

    internal void MarkAttacked(Troop attacker)
    {
        if (Task == TroopGroupTask.Defend || Task == TroopGroupTask.Nothing) State = TroopGroupState.Attacked;
        if (attacker != null) troopAI.RespondTo(attacker);
    }

    private List<Troop> Snapshot() { Prune(); return new List<Troop>(units); }
}
