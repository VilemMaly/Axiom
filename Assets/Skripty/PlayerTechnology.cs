using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class PlayerTechnology : NetworkBehaviour
{
    // proměnná doctrine, kterou může měnit pouze server, a klienti ji mohou pouze číst
    private NetworkVariable<DoctrineType> SelectedDoctrine = new NetworkVariable<DoctrineType>(DoctrineType.Iron, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<string> SelectedResearchId = new NetworkVariable<string>(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // list odemčených výzkumů
    private List<ResearchDefinition> UnlockedResearches = new List<ResearchDefinition>();
    [SerializeField] private DoctrineDefinition[] doctrines;
    [SerializeField] private ResearchDefinition[] researches;

    private PlayerResources playerResources;
    private bool isResearchInProgress = false;
    private int energyCostPerSecond = 0; // Příkladová hodnota, můžete ji upravit podle potřeby
    private float lastTimeSpent = 0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if(IsServer)
        {
            // Inicializace SelectedDoctrine na serveru
            SelectedDoctrine.Value = DoctrineType.None; // nebo nějaká výchozí hodnota
            // Inicializace SelectedResearch na serveru
            SelectedResearchId.Value = null; // nebo nějaká výchozí hodnota
        }
        playerResources = GetComponent<PlayerResources>();
    }

    // Update is called once per frame
    void Update()
    {
     // Pokud je výzkum v průběhu, odečítáme energii každou sekundu
    if (isResearchInProgress)
    {
        lastTimeSpent += Time.deltaTime;

        if (lastTimeSpent >= 1f)
        {
            lastTimeSpent -= 1f;

            if (!playerResources.TrySpend(0, energyCostPerSecond))
            {
                Debug.Log("Not enough energy to continue research.");
                isResearchInProgress = false;
            }
        }
    }

    }

    [ServerRpc(RequireOwnership = false)]
    public void SetSelectedDoctrineServerRpc(DoctrineType doctrine)
    {
        if(SelectedDoctrine.Value != doctrine)
        {
            SelectedDoctrine.Value = doctrine;
        }
    }

    public DoctrineDefinition GetDoctrine(DoctrineType type)
    {
        foreach (var doctrine in doctrines)
        {
            if (doctrine.DoctrineType == type)
                return doctrine;
        }

        return null;
    }

    public ResearchDefinition GetResearch(string researchId)
    {
        foreach (var research in researches)
        {
            if (research.ResearchId == researchId)
                return research;
        }

        return null;
    }

    [ServerRpc(RequireOwnership = false)]
    public void SelectResearchServerRpc(string researchId)
    {
        ResearchDefinition research = GetResearch(researchId);
        if (SelectedResearchId.Value != researchId)
        {
            SelectedResearchId.Value = researchId;
        }
        if(research != null && research.coriumCost <= playerResources.Corium.Value)
        {
            playerResources.TrySpend(research.coriumCost,0);
        }
        else
        {
            Debug.Log("Not enough corium to select this research.");
        }
    }
}
