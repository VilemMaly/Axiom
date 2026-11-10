using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Chooses targets and executes shared orders for its owning group.</summary>
public class TroopAI : MonoBehaviour
{
    [SerializeField] private float thinkInterval = 1f;
    [SerializeField] private float defendRadius = 12f;
    private TroopGroup group;
    private float nextThink;
    private Vector3 defendPosition;
    private ResourceManager resourceManager;
    private BuildingInteractor buildingInteractor;

    public void Initialize(TroopGroup owner) { group = owner; }
    public void SetDefendPosition(Vector3 position) { defendPosition = position; }

    public void BeginHarvest() { if (group != null) group.State = TroopGroupState.Harvesting; }

    public void RespondTo(Troop attacker)
    {
        if (group == null || attacker == null || group.Units.Count == 0) return;
        group.State = TroopGroupState.Attacked;
        List<Troop> enemyGroup = TroopGroup.GetUnitsInSameGroup(attacker);
        float ownStrength = EstimateStrength(group.Units);
        float enemyStrength = EstimateStrength(enemyGroup);
        Troop target = SelectWeakest(enemyGroup);
        if (target != null)
        {
            // Group strength informs the response state; every order still returns fire.
            group.Attack(target);
            if (ownStrength < enemyStrength) group.State = TroopGroupState.Attacked;
        }
    }

    private void Update()
    {
        if (group == null || Time.time < nextThink || !IsServer()) return;
        nextThink = Time.time + Mathf.Max(.1f, thinkInterval);
        group.Prune();
        if (group.Units.Count == 0) return;

        if (group.Task == TroopGroupTask.Defend)
        {
            ScanForAttackers(false);
            if (Vector3.Distance(Center(), defendPosition) > defendRadius) group.MoveAll(defendPosition);
            else if (group.State == TroopGroupState.Attacked) group.State = TroopGroupState.Idle;
        }
        else if (group.Task == TroopGroupTask.Attack)
        {
            ScanForAttackers(true);
            if (group.TargetTroop != null && Vector3.Distance(Center(), group.TargetTroop.transform.position) > defendRadius)
                group.MoveAll(group.TargetTroop.transform.position);
            else if (group.TargetBuilding != null && Vector3.Distance(Center(), group.TargetBuilding.transform.position) > defendRadius)
                group.MoveAll(group.TargetBuilding.transform.position);
            AssignAttackTargets();
        }
        else if (group.Task == TroopGroupTask.Harvest) UpdateHarvestOrders();
    }

    private void ScanForAttackers(bool attackAnyEnemy)
    {
        foreach (Troop unit in group.Units)
        {
            if (unit == null) continue;
            foreach (TroopGroup enemy in unit.ScanTroopGroups())
            {
                if (enemy == null || enemy.OwnerClientId == group.OwnerClientId) continue;
                if (!attackAnyEnemy && enemy.Task != TroopGroupTask.Attack) continue;
                group.HandleEnemyGroupDetected(enemy);
                return;
            }
        }
    }

    private void AssignAttackTargets()
    {
        if (group.TargetGroupId < 0 || group.TargetGroupOwnerId == ulong.MaxValue) return;
        List<Troop> enemies = new List<Troop>();
        foreach (Troop candidate in FindObjectsByType<Troop>(FindObjectsSortMode.None))
            if (candidate != null && candidate.OwnerClientId == group.TargetGroupOwnerId && candidate.TroopGroupId.Value == group.TargetGroupId && candidate.Health.Value > 0)
                enemies.Add(candidate);
        if (enemies.Count == 0) return;

        foreach (Troop friendly in group.Units)
        {
            if (friendly == null) continue;
            Troop assigned = null;
            float nearest = float.MaxValue;
            foreach (Troop enemy in enemies)
            {
                float distance = (enemy.transform.position - friendly.transform.position).sqrMagnitude;
                if (distance < nearest) { nearest = distance; assigned = enemy; }
            }
            if (assigned != null) friendly.Attack(assigned);
        }
    }

    private bool IsServer()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }

    private void UpdateHarvestOrders()
    {
        if (group == null || !IsServer())
        {
            return;
        }

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(group.OwnerClientId, out var ownerClient)
            && ownerClient.PlayerObject != null)
        {
            if (resourceManager == null)
            {
                resourceManager = ownerClient.PlayerObject.GetComponent<ResourceManager>();
            }

            if (buildingInteractor == null)
            {
                buildingInteractor = ownerClient.PlayerObject.GetComponent<BuildingInteractor>();
            }
        }

        bool harvesting = false;
        bool depositing = false;

        foreach (Troop troop in group.Units)
        {
            if (!(troop is Cargo cargo))
            {
                continue;
            }
            if(IsServer())
            {
                Debug.Log($"Updating harvest orders for cargo unit {cargo.name}");
            }

            cargo.RefreshResourceKnowledge(resourceManager);

            if (cargo.Status.Value == Cargo.WorkStatus.Moving)
            {
                bool resourceIsUnavailable = cargo.AssignedResource != null
                    && (!cargo.AssignedResource.IsSpawned || cargo.AssignedResource.Depleted);
                bool buildingIsUnavailable = cargo.AssignedBuilding != null
                    && (!cargo.AssignedBuilding.IsSpawned || cargo.AssignedBuilding.IsConstructionComplete.Value);

                if (resourceIsUnavailable)
                {
                    cargo.StopCurrentTask();
                }
                else if (buildingIsUnavailable)
                {
                    cargo.StopCurrentTask();
                }
                // Keep moving until the destination is within sight so the cargo can continue its task.
                else if (Vector3.Distance(cargo.transform.position, cargo.WorkDestination) > cargo.SightRange)
                {
                    continue;
                }
            }

            if (cargo.IsBuilding.Value)
            {
                if (cargo.AssignedBuilding == null
                    || !cargo.AssignedBuilding.IsSpawned
                    || cargo.AssignedBuilding.IsConstructionComplete.Value)
                {
                    cargo.StopCurrentTask();
                }
                else
                {
                    continue;
                }
            }

            if (cargo.IsHarvesting.Value)
            {
                harvesting = true;
                continue;
            }

            if (cargo.IsDepositing.Value)
            {
                depositing = true;
                continue;
            }

            if (cargo.HasCargo)
            {
                Building buildTarget = null;
                if (buildingInteractor != null)
                {
                    buildTarget = buildingInteractor.FindNearestIncompleteBuilding(
                        cargo.transform.position,
                        cargo.OwnerClientId);
                }

                if (buildTarget != null)
                {
                    cargo.OrderBuild(buildTarget);
                    continue;
                }

                Building deposit = null;
                if (buildingInteractor != null)
                {
                    deposit = buildingInteractor.FindNearestDeposit(
                        cargo.transform.position,
                        cargo.OwnerClientId);
                }

                if (deposit is Core core)
                {
                    cargo.OrderDeposit(core);
                    depositing = true;
                }
                else if (deposit != null)
                {
                    cargo.OrderDepositStorage(deposit);
                    depositing = true;
                }

                continue;
            }

            Resource target = null;
            if (group.TargetResource != null
                && group.TargetResource.IsSpawned
                && !group.TargetResource.Depleted)
            {
                target = group.TargetResource;
            }
            else if (resourceManager != null)
            {
                // Prefer a known resource near the group's objective, then one near this cargo unit.
                target = resourceManager.FindNearestKnown(group.ObjectivePosition);
                if (target == null)
                {
                    target = resourceManager.FindNearestKnown(cargo.transform.position);
                }
            }

            if (target != null)
            {
                cargo.OrderHarvest(target);
                harvesting = true;
            }
        }

        if (depositing)
        {
            group.State = TroopGroupState.Depositing;
        }
        else if (harvesting)
        {
            group.State = TroopGroupState.Harvesting;
        }
        else
        {
            group.State = TroopGroupState.Idle;
        }
    }

    private Resource FindNearestResource(Vector3 from)
    {
        Resource best = null;
        float bestDistance = float.MaxValue;
        foreach (Resource resource in FindObjectsByType<Resource>(FindObjectsSortMode.None))
        {
            if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0) continue;
            float distance = (resource.transform.position - from).sqrMagnitude;
            if (distance < bestDistance) { best = resource; bestDistance = distance; }
        }
        return best;
    }

    private static Core FindNearestCore(Vector3 from, ulong ownerClientId)
    {
        Core best = null;
        float bestDistance = float.MaxValue;
        foreach (Core core in FindObjectsByType<Core>(FindObjectsSortMode.None))
        {
            if (core == null || core.OwnerClientId != ownerClientId || !core.IsOperational) continue;
            float distance = (core.transform.position - from).sqrMagnitude;
            if (distance < bestDistance) { best = core; bestDistance = distance; }
        }
        return best;
    }

    private static Troop SelectWeakest(List<Troop> troops)
    {
        Troop weakest = null;
        float score = float.MaxValue;
        foreach (Troop troop in troops)
        {
            if (troop == null) continue;
            float value = troop.Health.Value + troop.Damage * Mathf.Max(1f, troop.AttackRange);
            if (value < score) { score = value; weakest = troop; }
        }
        return weakest;
    }

    private static float EstimateStrength(IReadOnlyList<Troop> troops)
    {
        float value = 0f;
        foreach (Troop troop in troops) if (troop != null)
            value += troop.Health.Value + troop.Damage * Mathf.Max(1f, troop.AttackRange);
        return value;
    }

    private Vector3 Center()
    {
        Vector3 center = Vector3.zero; int count = 0;
        foreach (Troop troop in group.Units) if (troop != null) { center += troop.transform.position; count++; }
        return count > 0 ? center / count : transform.position;
    }
}
