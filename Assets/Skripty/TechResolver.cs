using Unity.Netcode;
using UnityEngine;

public class TechResolver : NetworkBehaviour
{
    private PlayerTechnology research;

    private void Start()
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
                    : troop.Damage * (int)(1f + value);
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
                    : troop.BuildCoriumPerSecond * (int)(1f + value);
                break;

            case UpgradeStatType.RepairPower:
                troop.RepairCoriumPerSecond = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.RepairCoriumPerSecond + (int)value
                    : troop.RepairCoriumPerSecond * (int)(1f + value);
                break;

            case UpgradeStatType.MiningSpeed:
                troop.HarvestCoriumPerSecond = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.HarvestCoriumPerSecond + (int)value
                    : troop.HarvestCoriumPerSecond * (int)(1f + value);
                break;

            case UpgradeStatType.CargoCapacity:
                troop.MaxCargo = upgrade.ValueType == UpgradeValueType.Flat
                    ? troop.MaxCargo + (int)value
                    : troop.MaxCargo * (int)(1f + value);
                break;
        }
    }

    public void AddBoost(Building building)
    {
    }

    public void CheckUnlocked(Troop troop)
    {
    }

    public void CheckUnlocked(Building building)
    {
    }
}