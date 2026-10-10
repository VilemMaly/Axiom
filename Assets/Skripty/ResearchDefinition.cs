using System.Collections.Generic;
using UnityEngine;
using static Building;

[CreateAssetMenu(
    fileName = "NewResearch",
    menuName = "Axiom/Technology/Research Definition"
)]
public class ResearchDefinition : ScriptableObject
{
    [Header("Basic Information")]
    [SerializeField] public int researchId;
    [SerializeField] private string displayName;

    [TextArea(2, 5)]
    [SerializeField] private string description;

    [SerializeField] private Sprite icon;

    [Header("Research Cost")]
    [SerializeField] public int coriumCost = 100;
    [SerializeField] public float researchTime = 30f;
    [SerializeField] public float energyPerSecond = 5f;

    [Header("Requirements")]
    [SerializeField]
    private List<ResearchDefinition> requiredResearches =
        new List<ResearchDefinition>();

    [Header("Effects")]
    [SerializeField]
    private List<UpgradeDefinition> upgrades =
        new List<UpgradeDefinition>();

    [Header("Production Unlocks")]
    [SerializeField]
    private List<TroopType> unlockedTroops =
        new List<TroopType>();

    [SerializeField]
    private List<BuildingType> unlockedBuildings =
        new List<BuildingType>();

    public int ResearchId => researchId;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;

    public int CoriumCost => coriumCost;
    public float ResearchTime => researchTime;
    public float EnergyPerSecond => energyPerSecond;

    public IReadOnlyList<ResearchDefinition> RequiredResearches =>
        requiredResearches;

    public IReadOnlyList<UpgradeDefinition> Upgrades =>
        upgrades;

    public IReadOnlyList<TroopType> UnlockedTroops =>
        unlockedTroops;

    public IReadOnlyList<BuildingType> UnlockedBuildings =>
        unlockedBuildings;
}