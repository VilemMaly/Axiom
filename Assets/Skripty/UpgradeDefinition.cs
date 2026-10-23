using UnityEngine;
using static Building;

[CreateAssetMenu(
    fileName = "NewUpgrade",
    menuName = "Axiom/Technology/Upgrade Definition"
)]
public class UpgradeDefinition : ScriptableObject
{
    [Header("Basic Information")]
    [SerializeField] private string upgradeId;
    [SerializeField] private string displayName;

    [TextArea(2, 5)]
    [SerializeField] private string description;

    [SerializeField] private Sprite icon;

    [Header("Target")]
    [SerializeField] private UpgradeTargetType targetType;

    [SerializeField] private TroopType troopType;
    [SerializeField] private BuildingType buildingType;

    [Tooltip("Enable for a building tier upgrade. When research completes, this tier is applied to every owned building of Building Type.")]
    [SerializeField] private bool buildingTierUpgrade;
    [SerializeField, Min(1)] private int buildingUpgradeTier = 1;

    [Header("Stat")]
    [SerializeField] private UpgradeStatType statType;

    [Header("Value")]
    [Tooltip(
        "Pro Flat se hodnota přičte. " +
        "Pro Percentage zadej například 15 pro +15 %."
    )]
    [SerializeField] private float value;

    [Header("Value Type")]
    [SerializeField] private UpgradeValueType valueType;

    public string UpgradeId => upgradeId;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;

    public UpgradeTargetType TargetType => targetType;

    public TroopType TroopType => troopType;
    public BuildingType BuildingType => buildingType;
    public bool BuildingTierUpgrade => buildingTierUpgrade;
    public int BuildingUpgradeTier => buildingUpgradeTier;

    public UpgradeStatType StatType => statType;

    public float Value => value;
    public UpgradeValueType ValueType => valueType;
}

public enum UpgradeTargetType
{
    Troop,
    Building
}

public enum UpgradeValueType
{
    Flat,
    Percentage
}

public enum UpgradeStatType
{
    // Původní hodnoty ponechány ve stejném pořadí.
    MaxHealth,
    Damage,
    AttackRange,
    AttackSpeed,
    MovementSpeed,
    SightRange,
    BuildSpeed,
    RepairPower,
    MiningSpeed,
    CargoCapacity,

    // Nové upgrady podporované NavMeshAgentem.
    AgentAcceleration,
    TurnSpeed
}
