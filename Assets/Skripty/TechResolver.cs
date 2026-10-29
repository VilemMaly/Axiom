using Unity.Netcode;
using UnityEngine;

public class TechResolver : NetworkBehaviour
{
    private PlayerTechnology research;

    private void Awake()
    {
        research = GetComponent<PlayerTechnology>();
    }

    public void AddBoost(Troop troop)
    {
        if (troop == null || research == null)
            return;

        foreach (ResearchDefinition researchDefinition in research.unlockedResearches)
        {
            foreach (UpgradeDefinition upgrade in researchDefinition.Upgrades)
            {
                if (upgrade == null ||
                    upgrade.TargetType != UpgradeTargetType.Troop ||
                    upgrade.TroopType != troop.Type)
                    continue;

                Debug.Log($"Aplikuji {GetUpgradeName(upgrade)}", troop);
                HandleTroopUpgrade(troop, upgrade);
            }
        }
    }

    public void HandleTroopUpgrade(Troop troop, UpgradeDefinition upgrade)
    {
        float value = upgrade.Value;

        if (upgrade.ValueType == UpgradeValueType.Percentage)
            value /= 100f;

        switch (upgrade.StatType)
        {
            case UpgradeStatType.MaxHealth:
                int oldMaxHealth = troop.MaxHealth;
                troop.MaxHealth = upgrade.ValueType == UpgradeValueType.Flat
                    ? oldMaxHealth + Mathf.RoundToInt(value)
                    : Mathf.RoundToInt(oldMaxHealth * (1f + value));

                troop.Health.Value += troop.MaxHealth - oldMaxHealth;
                break;

            case UpgradeStatType.Damage:
                troop.Damage = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.Damage + (int)value
                    : Mathf.RoundToInt(troop.Damage * (1f + value));
                break;

            case UpgradeStatType.AttackRange:
                troop.AttackRange = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.AttackRange + value
                    : troop.AttackRange * (1f + value);
                break;

            case UpgradeStatType.AttackSpeed:
                troop.AttackSpeed = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.AttackSpeed + value
                    : troop.AttackSpeed * (1f + value);
                break;

            case UpgradeStatType.MovementSpeed:
                troop.GetComponent<UnityEngine.AI.NavMeshAgent>().speed =
                    upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.GetComponent<UnityEngine.AI.NavMeshAgent>().speed + value
                    : troop.GetComponent<UnityEngine.AI.NavMeshAgent>().speed * (1f + value);
                break;

            case UpgradeStatType.SightRange:
                troop.SightRange = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.SightRange + value
                    : troop.SightRange * (1f + value);
                break;

            case UpgradeStatType.BuildSpeed:
                troop.BuildCoriumPerSecond = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.BuildCoriumPerSecond + (int)value
                    : Mathf.RoundToInt(troop.BuildCoriumPerSecond * (1f + value));
                break;

            case UpgradeStatType.RepairPower:
                troop.RepairCoriumPerSecond = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.RepairCoriumPerSecond + (int)value
                    : Mathf.RoundToInt(troop.RepairCoriumPerSecond * (1f + value));
                break;

            case UpgradeStatType.MiningSpeed:
                troop.HarvestCoriumPerSecond = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.HarvestCoriumPerSecond + (int)value
                    : Mathf.RoundToInt(troop.HarvestCoriumPerSecond * (1f + value));
                break;

            case UpgradeStatType.CargoCapacity:
                troop.MaxCargo = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.MaxCargo + (int)value
                    : Mathf.RoundToInt(troop.MaxCargo * (1f + value));
                break;
        }
    }

    public void AddBoost(Building building)
    {
        if (building == null || research == null)
            return;

        foreach (ResearchDefinition researchDefinition in research.unlockedResearches)
        {
            if (researchDefinition == null || researchDefinition.Upgrades == null)
                continue;

            foreach (UpgradeDefinition upgrade in researchDefinition.Upgrades)
            {
                if (upgrade == null ||
                    upgrade.TargetType != UpgradeTargetType.Building ||
                    upgrade.BuildingType != building.Type)
                    continue;

                Debug.Log($"Aplikuji {GetUpgradeName(upgrade)}", building);
                if (upgrade.BuildingTierUpgrade)
                    building.UpgradeBuilding(upgrade.BuildingUpgradeTier);
                HandleBuildingUpgrade(building, upgrade);
            }
        }
    }

    private void HandleBuildingUpgrade(Building building, UpgradeDefinition upgrade)
    {
        float value = upgrade.Value;
        if (upgrade.ValueType == UpgradeValueType.Percentage)
            value /= 100f;

        switch (upgrade.StatType)
        {
            case UpgradeStatType.MaxHealth:
                int oldMaxHealth = building.MaxHealth;
                building.MaxHealth = upgrade.ValueType == UpgradeValueType.Flat
                    ? oldMaxHealth + Mathf.RoundToInt(value)
                    : Mathf.RoundToInt(oldMaxHealth * (1f + value));

                if (building.IsServer)
                    building.Health.Value += building.MaxHealth - oldMaxHealth;
                break;

            case UpgradeStatType.Damage:
                building.Damage = upgrade.ValueType == UpgradeValueType.Flat
                    ? building.Damage + Mathf.RoundToInt(value)
                    : Mathf.RoundToInt(building.Damage * (1f + value));
                break;

            case UpgradeStatType.AttackRange:
                building.AttackRange = upgrade.ValueType == UpgradeValueType.Flat
                    ? building.AttackRange + value
                    : building.AttackRange * (1f + value);
                break;

            case UpgradeStatType.AttackSpeed:
                building.AttackSpeed = upgrade.ValueType == UpgradeValueType.Flat
                    ? building.AttackSpeed + value
                    : building.AttackSpeed * (1f + value);
                break;

            case UpgradeStatType.SightRange:
                building.SightRange = upgrade.ValueType == UpgradeValueType.Flat
                    ? building.SightRange + value
                    : building.SightRange * (1f + value);
                break;
        }
    }

    private static string GetUpgradeName(UpgradeDefinition upgrade)
    {
        return string.IsNullOrWhiteSpace(upgrade.DisplayName)
            ? upgrade.name
            : upgrade.DisplayName;
    }

}
