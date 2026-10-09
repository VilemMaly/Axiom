using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Autoritativní stav doktrín a výzkumu konkrétního hráče.
/// Síťové proměnné může měnit pouze server. UI klient čte jejich stav
/// a mění jej výhradně prostřednictvím ServerRpc.
/// </summary>
public class PlayerTechnology : NetworkBehaviour
{
    private readonly NetworkVariable<DoctrineType> selectedDoctrine =
        new NetworkVariable<DoctrineType>(
            DoctrineType.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<string> selectedResearchId =
        new NetworkVariable<string>(
            string.Empty,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly List<ResearchDefinition> unlockedResearches =
        new List<ResearchDefinition>();

    [Header("Technology Data")]
    [SerializeField] private DoctrineDefinition[] doctrines;
    [SerializeField] private ResearchDefinition[] researches;

    private PlayerResources playerResources;

    // Jednoduchý serverový stav právě probíhajícího výzkumu.
    private bool isResearchInProgress;
    private int energyCostPerSecond;
    private float lastTimeSpent;
    private float researchProgressSeconds;
    private ResearchDefinition activeResearch;

    /// <summary>Aktuální síťový stav doktríny. Pouze pro čtení.</summary>
    public DoctrineType SelectedDoctrineValue => selectedDoctrine.Value;

    /// <summary>Aktuální ID běžícího výzkumu, případně prázdný řetězec. Pouze pro čtení.</summary>
    public string CurrentSelectedResearchId => selectedResearchId.Value;

    /// <summary>Datový katalog doktrín používaný controllerem UI.</summary>
    public IReadOnlyList<DoctrineDefinition> Doctrines =>
        doctrines ?? Array.Empty<DoctrineDefinition>();

    /// <summary>Datový katalog výzkumů používaný serverovým ověřením i UI.</summary>
    public IReadOnlyList<ResearchDefinition> Researches =>
        researches ?? Array.Empty<ResearchDefinition>();

    /// <summary>Vyvolá se při změně síťové proměnné vybrané doktríny.</summary>
    public event Action<DoctrineType, DoctrineType> SelectedDoctrineChanged;

    /// <summary>Vyvolá se při změně ID právě běžícího výzkumu.</summary>
    public event Action<string, string> SelectedResearchChanged;

    /// <summary>Vyvolá se při despawnu tohoto síťového objektu.</summary>
    public event Action<PlayerTechnology> NetworkObjectDespawned;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        playerResources = GetComponent<PlayerResources>();

        if (IsServer)
        {
            // Normalizace staršího/null stavu. Výchozí hodnota nové instance
            // je už DoctrineType.None a prázdné ResearchId.
            if (selectedResearchId.Value == null)
                selectedResearchId.Value = string.Empty;

            if (playerResources == null)
            {
                Debug.LogError(
                    $"[{nameof(PlayerTechnology)}] Na hráčově NetworkObjectu chybí PlayerResources. Výzkumy nelze spouštět.",
                    this
                );
            }
        }

        // Události registrujeme v OnNetworkSpawn, kdy už je NetworkObject
        // síťově aktivní a počáteční hodnoty jsou synchronizované.
        selectedDoctrine.OnValueChanged -= HandleSelectedDoctrineValueChanged;
        selectedDoctrine.OnValueChanged += HandleSelectedDoctrineValueChanged;

        selectedResearchId.OnValueChanged -= HandleSelectedResearchValueChanged;
        selectedResearchId.OnValueChanged += HandleSelectedResearchValueChanged;
    }

    public override void OnNetworkDespawn()
    {
        selectedDoctrine.OnValueChanged -= HandleSelectedDoctrineValueChanged;
        selectedResearchId.OnValueChanged -= HandleSelectedResearchValueChanged;

        if (IsServer && isResearchInProgress)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerTechnology)}] Síťový objekt hráče byl despawnut během výzkumu. Výzkum byl přerušen bez vrácení již zaplaceného Coria.",
                this
            );
        }

        isResearchInProgress = false;
        activeResearch = null;
        lastTimeSpent = 0f;
        researchProgressSeconds = 0f;

        NetworkObjectDespawned?.Invoke(this);
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || !isResearchInProgress)
            return;

        if (activeResearch == null)
        {
            AbortActiveResearch("Aktivní ResearchDefinition již není dostupná.");
            return;
        }

        if (playerResources == null)
        {
            AbortActiveResearch("Chybí PlayerResources.");
            return;
        }

        lastTimeSpent += Time.deltaTime;

        // Výzkum se posouvá po celých sekundách, za každou se nejprve
        // zaplatí energetický náklad. while pokryje i delší frame hitch.
        while (lastTimeSpent >= 1f && isResearchInProgress)
        {
            lastTimeSpent -= 1f;

            if (energyCostPerSecond > 0 &&
                !playerResources.TrySpend(0, energyCostPerSecond))
            {
                AbortActiveResearch(
                    $"Nedostatek energie pro pokračování výzkumu '{activeResearch.DisplayName}'."
                );
                return;
            }

            researchProgressSeconds += 1f;

            if (researchProgressSeconds >= activeResearch.ResearchTime)
            {
                CompleteActiveResearch();
                return;
            }
        }
    }

    /// <summary>
    /// Požádá server o výběr doktríny pro hráče, kterému tento NetworkObject patří.
    /// UI musí potvrzení vyhodnotit až podle SelectedDoctrineValue.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SetSelectedDoctrineServerRpc(
        DoctrineType doctrine,
        ServerRpcParams rpcParams = default
    )
    {
        if (!IsAuthorizedRequest(rpcParams, "výběr doktríny"))
            return;

        if (doctrine == DoctrineType.None || GetDoctrine(doctrine) == null)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerTechnology)}] Zamítnut neplatný požadavek na doktrínu '{doctrine}'.",
                this
            );
            return;
        }

        if (selectedDoctrine.Value == doctrine)
        {
            Debug.Log($"[{nameof(PlayerTechnology)}] Doktrína '{doctrine}' už je vybraná; stav se nemění.", this);
            return;
        }

        selectedDoctrine.Value = doctrine;
        Debug.Log($"[{nameof(PlayerTechnology)}] Server nastavil doktrínu '{doctrine}' pro OwnerClientId {OwnerClientId}.", this);
    }

    /// <summary>Vrátí definici doktríny podle jejího typu, případně null.</summary>
    public DoctrineDefinition GetDoctrine(DoctrineType type)
    {
        if (doctrines == null || type == DoctrineType.None)
            return null;

        for (int i = 0; i < doctrines.Length; i++)
        {
            DoctrineDefinition doctrine = doctrines[i];
            if (doctrine != null && doctrine.DoctrineType == type)
                return doctrine;
        }

        return null;
    }

    /// <summary>Vrátí kanonickou definici výzkumu z katalogu PlayerTechnology, případně null.</summary>
    public ResearchDefinition GetResearch(string researchId)
    {
        if (string.IsNullOrWhiteSpace(researchId) || researches == null)
            return null;

        for (int i = 0; i < researches.Length; i++)
        {
            ResearchDefinition research = researches[i];
            if (research != null &&
                string.Equals(research.ResearchId, researchId, StringComparison.Ordinal))
            {
                return research;
            }
        }

        return null;
    }

    /// <summary>
    /// Požádá server o zahájení výzkumu. Server validuje identitu hráče,
    /// doktrínu, požadavky a zdroje ještě před nastavením síťového stavu.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SelectResearchServerRpc(
        string researchId,
        ServerRpcParams rpcParams = default
    )
    {
        if (!IsAuthorizedRequest(rpcParams, "zahájení výzkumu"))
            return;

        if (string.IsNullOrWhiteSpace(researchId))
        {
            Debug.LogWarning($"[{nameof(PlayerTechnology)}] Zamítnut požadavek s prázdným ResearchId.", this);
            return;
        }

        // Opakované potvrzení už běžícího výzkumu nic nestrhává podruhé.
        if (isResearchInProgress)
        {
            if (string.Equals(selectedResearchId.Value, researchId, StringComparison.Ordinal))
            {
                Debug.Log($"[{nameof(PlayerTechnology)}] Výzkum '{researchId}' už běží; další platba nebyla provedena.", this);
            }
            else
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerTechnology)}] Zamítnut výzkum '{researchId}', protože už běží jiný výzkum '{selectedResearchId.Value}'.",
                    this
                );
            }

            return;
        }

        ResearchDefinition research = GetResearch(researchId);
        if (research == null)
        {
            Debug.LogWarning($"[{nameof(PlayerTechnology)}] Zamítnut neznámý ResearchId '{researchId}'.", this);
            return;
        }

        if (research.CoriumCost < 0 ||
            research.ResearchTime <= 0f ||
            float.IsNaN(research.ResearchTime) ||
            float.IsInfinity(research.ResearchTime) ||
            research.EnergyPerSecond < 0f ||
            float.IsNaN(research.EnergyPerSecond) ||
            float.IsInfinity(research.EnergyPerSecond))
        {
            Debug.LogError(
                $"[{nameof(PlayerTechnology)}] Výzkum '{research.DisplayName}' má neplatnou cenu, délku nebo energetický náklad.",
                research
            );
            return;
        }

        if (selectedDoctrine.Value == DoctrineType.None)
        {
            Debug.LogWarning($"[{nameof(PlayerTechnology)}] Výzkum '{research.DisplayName}' nelze spustit bez vybrané doktríny.", this);
            return;
        }

        DoctrineDefinition selectedDoctrineDefinition = GetDoctrine(selectedDoctrine.Value);
        if (selectedDoctrineDefinition == null || !DoctrineContainsResearch(selectedDoctrineDefinition, research.ResearchId))
        {
            Debug.LogWarning(
                $"[{nameof(PlayerTechnology)}] Zamítnut výzkum '{research.DisplayName}', protože nepatří do aktuálně vybrané doktríny '{selectedDoctrine.Value}'.",
                this
            );
            return;
        }

        if (IsResearchUnlocked(research.ResearchId))
        {
            Debug.LogWarning($"[{nameof(PlayerTechnology)}] Výzkum '{research.DisplayName}' už byl dokončen.", this);
            return;
        }

        if (!AreResearchRequirementsUnlocked(research))
            return;

        if (playerResources == null)
        {
            Debug.LogError($"[{nameof(PlayerTechnology)}] Nelze spustit výzkum: PlayerResources chybí.", this);
            return;
        }

        if (playerResources.Corium.Value < research.CoriumCost)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerTechnology)}] Nedostatek Coria pro '{research.DisplayName}'. Potřeba: {research.CoriumCost}, dostupné: {playerResources.Corium.Value}.",
                this
            );
            return;
        }

        // Corium se odečte před změnou SelectedResearchId. Pokud platba selže,
        // síťový stav zůstane beze změny a výzkum nezačne.
        if (!playerResources.TrySpend(research.CoriumCost, 0))
        {
            Debug.LogWarning($"[{nameof(PlayerTechnology)}] TrySpend odmítl platbu za výzkum '{research.DisplayName}'. Výzkum nebyl zahájen.", this);
            return;
        }

        activeResearch = research;
        isResearchInProgress = true;
        lastTimeSpent = 0f;
        researchProgressSeconds = 0f;
        energyCostPerSecond = Mathf.CeilToInt(research.EnergyPerSecond);
        selectedResearchId.Value = research.ResearchId;

        Debug.Log(
            $"[{nameof(PlayerTechnology)}] Zahájen výzkum '{research.DisplayName}'. Corium: {research.CoriumCost}, energie/s: {energyCostPerSecond}, délka: {research.ResearchTime} s.",
            this
        );
    }

    private bool IsAuthorizedRequest(ServerRpcParams rpcParams, string actionName)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (senderClientId != OwnerClientId)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerTechnology)}] Zamítnut požadavek na {actionName}: klient {senderClientId} není vlastníkem tohoto objektu (OwnerClientId {OwnerClientId}).",
                this
            );
            return false;
        }

        return true;
    }

    private bool DoctrineContainsResearch(DoctrineDefinition doctrine, string researchId)
    {
        if (doctrine == null || doctrine.Researches == null)
            return false;

        for (int i = 0; i < doctrine.Researches.Count; i++)
        {
            ResearchDefinition listedResearch = doctrine.Researches[i];
            if (listedResearch != null &&
                string.Equals(listedResearch.ResearchId, researchId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool AreResearchRequirementsUnlocked(ResearchDefinition research)
    {
        if (research.RequiredResearches == null)
            return true;

        for (int i = 0; i < research.RequiredResearches.Count; i++)
        {
            ResearchDefinition requirement = research.RequiredResearches[i];

            if (requirement == null || string.IsNullOrWhiteSpace(requirement.ResearchId))
            {
                Debug.LogError(
                    $"[{nameof(PlayerTechnology)}] Výzkum '{research.DisplayName}' obsahuje neplatný požadovaný výzkum.",
                    research
                );
                return false;
            }

            if (!IsResearchUnlocked(requirement.ResearchId))
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerTechnology)}] Výzkum '{research.DisplayName}' vyžaduje nejprve dokončit '{requirement.DisplayName}'.",
                    this
                );
                return false;
            }
        }

        return true;
    }

    private bool IsResearchUnlocked(string researchId)
    {
        for (int i = 0; i < unlockedResearches.Count; i++)
        {
            ResearchDefinition unlocked = unlockedResearches[i];
            if (unlocked != null &&
                string.Equals(unlocked.ResearchId, researchId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void CompleteActiveResearch()
    {
        if (!IsServer || activeResearch == null)
            return;

        ResearchDefinition completedResearch = activeResearch;

        if (!IsResearchUnlocked(completedResearch.ResearchId))
            unlockedResearches.Add(completedResearch);

        isResearchInProgress = false;
        activeResearch = null;
        energyCostPerSecond = 0;
        lastTimeSpent = 0f;
        researchProgressSeconds = 0f;
        selectedResearchId.Value = string.Empty;

        Debug.Log($"[{nameof(PlayerTechnology)}] Výzkum '{completedResearch.DisplayName}' byl dokončen.", this);
    }

    private void AbortActiveResearch(string reason)
    {
        if (!IsServer)
            return;

        string researchName = activeResearch != null ? activeResearch.DisplayName : selectedResearchId.Value;

        isResearchInProgress = false;
        activeResearch = null;
        energyCostPerSecond = 0;
        lastTimeSpent = 0f;
        researchProgressSeconds = 0f;
        selectedResearchId.Value = string.Empty;

        Debug.LogWarning(
            $"[{nameof(PlayerTechnology)}] Výzkum '{researchName}' byl přerušen. Důvod: {reason} Již zaplacené Corium se nevrací.",
            this
        );
    }

    private void HandleSelectedDoctrineValueChanged(DoctrineType previous, DoctrineType current)
    {
        SelectedDoctrineChanged?.Invoke(previous, current);
    }

    private void HandleSelectedResearchValueChanged(string previous, string current)
    {
        SelectedResearchChanged?.Invoke(previous, current);
    }
}
