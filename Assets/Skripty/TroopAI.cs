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
            if (group.State != TroopGroupState.Attacked)
            {
                if (Vector3.Distance(Center(), defendPosition) > defendRadius)
                    group.MoveAll(defendPosition);
            }
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
        foreach (Troop troop in group.Units)
        {
            if (!(troop is Cargo cargo)) continue;

            if (group.Owner == null || group.Owner.BuildingInteractor == null)
                return;

            IReadOnlyList<Building> buildings = group.Owner.BuildingInteractor.MyBuildings;
            if (!cargo.IsCargoFull && !(cargo.IsDepositing.Value && cargo.HasCargo))
            {
                cargo.IsDepositing.Value = false;
                cargo.IsHarvesting.Value = false;

                Core nearestCore = null;
                float coreDistance = float.MaxValue;
                foreach (Building building in buildings)
                {
                    if (building == null || building.OwnerClientId != group.OwnerClientId ||
                        building.Type != Building.BuildingType.Core || !(building is Core core))
                        continue;

                    float distance = (core.transform.position - cargo.transform.position).sqrMagnitude;
                    if (distance < coreDistance)
                    {
                        nearestCore = core;
                        coreDistance = distance;
                    }
                }

                Resource nearestResource = null;
                float resourceDistance = float.MaxValue;
                ResourceManager resourceManager = group.Owner.ResourceManager;
                if (nearestCore != null && resourceManager != null)
                {
                    foreach (Resource resource in resourceManager.Resources)
                    {
                        if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0)
                            continue;

                        float distance = (resource.transform.position - nearestCore.transform.position).sqrMagnitude;
                        if (distance < resourceDistance)
                        {
                            nearestResource = resource;
                            resourceDistance = distance;
                        }
                    }
                }

                if (nearestResource == null)
                {
                    cargo.StopCurrentTask();
                    cargo.status.Value = Troop.State.idle;
                    continue;
                }

                Resource scannedResource = null;
                float scannedResourceDistance = float.MaxValue;
                foreach (Resource resource in cargo.Scan<Resource>(TroopScanType.Coria))
                {
                    if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0)
                        continue;

                    float distance = (resource.transform.position - cargo.transform.position).sqrMagnitude;
                    if (distance < scannedResourceDistance)
                    {
                        scannedResource = resource;
                        scannedResourceDistance = distance;
                    }
                }

                if (scannedResource != null)
                {
                    cargo.IsHarvesting.Value = true;
                    cargo.Harvest(scannedResource);
                }
                else if (cargo.status.Value != Troop.State.moving || !cargo.IsMoving)
                {
                    cargo.MoveTo(nearestResource.transform.position);
                }

                continue;
            }

            cargo.IsHarvesting.Value = false;

            Building depositTarget = null;
            Building.BuildingType selectedBuildingType = Building.BuildingType.Core;
            float depositDistance = float.MaxValue;
            foreach (Building building in buildings)
            {
                if (building == null || building.OwnerClientId != group.OwnerClientId ||
                    building.MissingCorium <= 0)
                    continue;

                float distance = (building.transform.position - cargo.transform.position).sqrMagnitude;
                if (distance < depositDistance)
                {
                    depositTarget = building;
                    selectedBuildingType = building.Type;
                    depositDistance = distance;
                }
            }

            if (depositTarget == null)
            {
                float nearestCoreDistance = float.MaxValue;
                foreach (Building building in buildings)
                {
                    if (building == null || building.OwnerClientId != group.OwnerClientId ||
                        building.Type != Building.BuildingType.Core)
                        continue;

                    float distance = (building.transform.position - cargo.transform.position).sqrMagnitude;
                    if (distance < nearestCoreDistance)
                    {
                        depositTarget = building;
                        selectedBuildingType = building.Type;
                        nearestCoreDistance = distance;
                    }
                }
            }

            if (depositTarget == null)
            {
                cargo.IsDepositing.Value = false;
                cargo.StopCurrentTask();
                cargo.status.Value = Troop.State.idle;
                continue;
            }

            Building scannedBuilding = null;
            float scannedBuildingDistance = float.MaxValue;

            foreach (Building building in cargo.Scan<Building>(TroopScanType.Building))
            {
                if (building == null || building.OwnerClientId != group.OwnerClientId ||
                    building.Type != selectedBuildingType ||
                    (selectedBuildingType != Building.BuildingType.Core && building.MissingCorium <= 0))
                    continue;

                float distance = (building.transform.position - cargo.transform.position).sqrMagnitude;
                if (distance < scannedBuildingDistance)
                {
                    scannedBuilding = building;
                    scannedBuildingDistance = distance;
                }
            }

            if (scannedBuilding != null)
            {
                cargo.IsDepositing.Value = true;
                cargo.Deposit(scannedBuilding);
            }
            else if (cargo.status.Value != Troop.State.moving || !cargo.IsMoving)
            {
                cargo.MoveTo(depositTarget.transform.position);
            }
        }
    }

    private Resource FindNearestResource(Vector3 from)
    {
        Resource best = null;
        float bestDistance = float.MaxValue;
        ResourceManager resourceManager = group != null && group.Owner != null ? group.Owner.ResourceManager : null;
        if (resourceManager == null) return null;

        foreach (Resource resource in resourceManager.Resources)
        {
            if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0) continue;
            float distance = (resource.transform.position - from).sqrMagnitude;
            if (distance < bestDistance) { best = resource; bestDistance = distance; }
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
