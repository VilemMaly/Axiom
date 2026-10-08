using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class PlayerTechnology : NetworkBehaviour
{
    // proměnná doctrine, kterou může měnit pouze server, a klienti ji mohou pouze číst
    private NetworkVariable<DoctrineDefinition> SelectedDoctrine = new NetworkVariable<DoctrineDefinition>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ResearchDefinition> SelectedResearch = new NetworkVariable<ResearchDefinition>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // list odemčených výzkumů
    private List<ResearchDefinition> UnlockedResearches = new List<ResearchDefinition>();

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
            SelectedDoctrine.Value = null; // nebo nějaká výchozí hodnota
            // Inicializace SelectedResearch na serveru
            SelectedResearch.Value = null; // nebo nějaká výchozí hodnota
        }
        playerResources = GetComponent<PlayerResources>();
    }

    // Update is called once per frame
    void Update()
    {
        // Pokud je výzkum v průběhu, odečítáme energii každou sekundu
        if (isResearchInProgress)
        {
            if (lastTimeSpent + Time.deltaTime <= 1f)
            {
                playerResources.TrySpend(0, energyCostPerSecond);
                lastTimeSpent += Time.deltaTime;
            }
            else
            {
                Debug.Log("Not enough energy to continue research.");
                isResearchInProgress = false; // Zastavíme výzkum, pokud není dostatek energie
            }
        }

    }

    [ServerRpc(RequireOwnership = false)]
    public void SetSelectedDoctrineServerRpc(DoctrineDefinition doctrine)
    {
        if(SelectedDoctrine.Value != doctrine)
        {
            SelectedDoctrine.Value = doctrine;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SelectResearch(ResearchDefinition research)
    {
        if (SelectedResearch.Value != research)
        {
            SelectedResearch.Value = research;
        }
        if(research.coriumCost <= playerResources.Corium.Value)
        {
            playerResources.TrySpend(research.coriumCost,0);
        }
        else
        {
            Debug.Log("Not enough corium to select this research.");
        }
    }
}
