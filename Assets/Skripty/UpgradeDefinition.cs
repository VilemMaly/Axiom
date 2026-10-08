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

    [Header("Stat")]
    [SerializeField] private UpgradeStatType statType;

    [Header("Value")]
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
    MaxHealth,
    Damage,
    AttackRange,
    AttackSpeed,
    MovementSpeed,
    SightRange,
    BuildSpeed,
    RepairPower,
    MiningSpeed,
    CargoCapacity
}