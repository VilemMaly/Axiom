using UnityEngine;

[CreateAssetMenu(fileName = "NewInspectorData", menuName = "Axiom/Inspector Data")]
public class InspectorData : ScriptableObject
{
    [Header("Kategorie")]
    [SerializeField] private bool troop;
    [SerializeField] private bool budova;

    [Header("Typ")]
    [SerializeField] private TroopType troopType;
    [SerializeField] private Building.BuildingType buildingType;

    [Header("Zobrazované informace")]
    [SerializeField] private Sprite icon;
    [SerializeField] private string displayName;
    [TextArea(3, 8)]
    [SerializeField] private string description;

    public bool Troop => troop;
    public bool Budova => budova;
    public TroopType TroopType => troopType;
    public Building.BuildingType BuildingType => buildingType;
    public Sprite Icon => icon;
    public string DisplayName => displayName;
    public string Description => description;
}
