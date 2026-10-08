using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "NewDoctrine",
    menuName = "Axiom/Technology/Doctrine Definition"
)]
public class DoctrineDefinition : ScriptableObject
{
    [Header("Basic Information")]
    [SerializeField] private string doctrineId;
    [SerializeField] private string displayName;
    [TextArea(2, 5)]
    [SerializeField] private string description;
    [SerializeField] private Sprite icon;

    [Header("Doctrine")]
    [SerializeField] public DoctrineType doctrineType;

    [Header("Research")]
    [SerializeField] private List<ResearchDefinition> researches = new List<ResearchDefinition>();

    public string DoctrineId => doctrineId;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public DoctrineType DoctrineType => doctrineType;
    public IReadOnlyList<ResearchDefinition> Researches => researches;
}

public enum DoctrineType
{
    Iron,
    Logistics,
    Energy,
    AI,
    None
}