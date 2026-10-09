using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// Centrální UI controller doktrín a výzkumů.
/// Připoj ho na GameObject přiřazený v MainUI.researchParent.
/// Controller neprovádí herní validaci: pouze odesílá požadavky do
/// PlayerTechnology a čeká na potvrzení prostřednictvím NetworkVariable.
/// </summary>
public class ResearchUiController : MonoBehaviour
{
    [Header("Local Player")]
    [Tooltip("Volitelné. Pokud není přiřazen lokální vlastník, controller ho vyhledá podle IsOwner a IsSpawned.")]
    [SerializeField] private PlayerTechnology playerTechnology;

    [Header("Doctrine UI")]
    [SerializeField] private DoctrineSelector doctrinePrefab;
    [SerializeField] private RectTransform doctrineGridParent;
    [Tooltip("Volitelný parent celé sekce doktrín. Pokud není nastaven, skrývá se pouze Doctrine Grid Parent.")]
    [SerializeField] private GameObject doctrineSectionParent;

    [Header("Research UI")]
    [Tooltip("Prefab položky výzkumu. Na kořenovém objektu musí být ResearchPopup s ikonou a vlastním skrytým detailem.")]
    [SerializeField] private GameObject researchButtonPrefab;
    [SerializeField] private RectTransform researchGridParent;
    [Tooltip("Volitelný parent celé sekce výzkumů. Pokud není nastaven, skrývá se pouze Research Grid Parent.")]
    [SerializeField] private GameObject researchSectionParent;

    [Header("Research Navigation")]
    [Tooltip("Volitelné tlačítko pro návrat do nabídky doktrín. Pouze přepíná UI; požadavek na změnu doktríny se odešle až potvrzením karty.")]
    [SerializeField] private Button backToDoctrineButton;

    [Header("Network Request Handling")]
    [Tooltip("Pokud se požadovaný stav neobjeví do tohoto času, controller požadavek považuje za nepotvrzený a obnoví UI.")]
    [SerializeField, Min(0.5f)] private float serverRequestTimeoutSeconds = 5f;

    [Header("Layout Validation")]
    [SerializeField] private bool warnIfGridLayoutGroupMissing = true;

    private sealed class DoctrineEntry
    {
        public DoctrineDefinition Definition;
        public DoctrineSelector Selector;
        public Action<DoctrineDefinition, bool> Listener;
    }

    private sealed class ResearchEntry
    {
        public ResearchDefinition Definition;
        public GameObject Instance;
        public ResearchPopup Popup;
        public Action<ResearchPopup, ResearchDefinition> ClickListener;
        public UnityAction AcceptListener;
    }

    private readonly List<DoctrineEntry> doctrineEntries = new List<DoctrineEntry>();
    private readonly List<ResearchEntry> researchEntries = new List<ResearchEntry>();

    private PlayerTechnology boundTechnology;
    private DoctrineType displayedDoctrineType = DoctrineType.None;

    private bool doctrineRequestPending;
    private DoctrineType pendingDoctrineType = DoctrineType.None;
    private bool researchRequestPending;
    private string pendingResearchId = string.Empty;
    private ResearchPopup pendingResearchPopup;
    private float pendingRequestStartedAt;

    private float nextTechnologySearchTime;
    private bool warnedMissingTechnology;
    private bool warnedMissingDoctrineGridLayout;
    private bool warnedMissingResearchGridLayout;
    private bool forceDoctrineRebuild;

    private void Awake()
    {
        ValidateConfiguration();
    }

    private void OnEnable()
    {
        if (backToDoctrineButton != null)
        {
            backToDoctrineButton.onClick.RemoveListener(HandleBackToDoctrinesClicked);
            backToDoctrineButton.onClick.AddListener(HandleBackToDoctrinesClicked);
        }

        if (TryBindLocalPlayerTechnology())
        {
            SynchronizeViewFromNetworkState();
            SubscribeGeneratedUiEvents();
        }
        else
        {
            SetSectionsVisible(false, false);
        }
    }

    private void OnDisable()
    {
        if (backToDoctrineButton != null)
            backToDoctrineButton.onClick.RemoveListener(HandleBackToDoctrinesClicked);

        UnsubscribeGeneratedUiEvents();
        UnbindPlayerTechnology();

        // RPC může být stále zpracováváno. Po opětovném otevření panelu se UI
        // znovu sestaví ze skutečného síťového stavu, nikoliv ze starého pending stavu.
        ClearPendingRequests();

        CloseAllResearchPopups();
    }

    private void OnDestroy()
    {
        if (backToDoctrineButton != null)
            backToDoctrineButton.onClick.RemoveListener(HandleBackToDoctrinesClicked);

        UnsubscribeGeneratedUiEvents();
        UnbindPlayerTechnology();
        ClearDoctrineButtons();
        ClearResearchButtons();
    }

    private void Update()
    {
        if (boundTechnology == null || !boundTechnology.IsSpawned || !boundTechnology.IsOwner)
        {
            UnbindPlayerTechnology();

            if (TryBindLocalPlayerTechnology())
            {
                SynchronizeViewFromNetworkState();
                SubscribeGeneratedUiEvents();
            }
        }

        if (boundTechnology == null || !boundTechnology.IsSpawned)
            return;

        ResolvePendingRequestsFromCurrentState();

        if ((doctrineRequestPending || researchRequestPending) &&
            Time.unscaledTime - pendingRequestStartedAt >= Mathf.Max(0.5f, serverRequestTimeoutSeconds))
        {
            HandleRequestTimeout();
        }
    }

    private void ValidateConfiguration()
    {
        if (doctrinePrefab == null)
            Debug.LogError($"[{nameof(ResearchUiController)}] Není přiřazen Doctrine Prefab.", this);

        if (doctrineGridParent == null)
            Debug.LogError($"[{nameof(ResearchUiController)}] Není přiřazen Doctrine Grid Parent.", this);

        if (researchButtonPrefab == null)
        {
            Debug.LogError($"[{nameof(ResearchUiController)}] Není přiřazen Research Button Prefab.", this);
        }
        else if (researchButtonPrefab.GetComponent<ResearchPopup>() == null)
        {
            Debug.LogError(
                $"[{nameof(ResearchUiController)}] Kořen prefabu položky výzkumu musí obsahovat komponentu ResearchPopup. Ikonu a detail přiřaď přímo v tomto skriptu.",
                researchButtonPrefab
            );
        }

        if (researchGridParent == null)
            Debug.LogError($"[{nameof(ResearchUiController)}] Není přiřazen Research Grid Parent.", this);

        if (doctrineGridParent != null && researchGridParent != null &&
            doctrineGridParent == researchGridParent)
        {
            Debug.LogError(
                $"[{nameof(ResearchUiController)}] Doctrine Grid Parent a Research Grid Parent musí být odlišné objekty.",
                this
            );
        }

        if (doctrineSectionParent != null && researchSectionParent != null &&
            doctrineSectionParent == researchSectionParent)
        {
            Debug.LogError(
                $"[{nameof(ResearchUiController)}] Doctrine Section Parent a Research Section Parent musí být odlišné objekty.",
                this
            );
        }
    }

    /// <summary>
    /// Najde PlayerTechnology patřící lokálnímu vlastníkovi.
    /// Ani při ručně přiřazené referenci se nepracuje s objektem jiného hráče.
    /// </summary>
    private bool TryBindLocalPlayerTechnology()
    {
        if (boundTechnology != null && boundTechnology.IsSpawned && boundTechnology.IsOwner)
            return true;

        if (Time.unscaledTime < nextTechnologySearchTime)
            return false;

        nextTechnologySearchTime = Time.unscaledTime + 0.5f;

        PlayerTechnology candidate = null;

        if (playerTechnology != null && playerTechnology.IsSpawned && playerTechnology.IsOwner)
            candidate = playerTechnology;

        if (candidate == null)
        {
            PlayerTechnology[] allTechnologies = FindObjectsOfType<PlayerTechnology>();

            for (int i = 0; i < allTechnologies.Length; i++)
            {
                PlayerTechnology possible = allTechnologies[i];

                if (possible == null || !possible.IsSpawned || !possible.IsOwner)
                    continue;

                if (candidate != null && candidate != possible)
                {
                    Debug.LogError(
                        $"[{nameof(ResearchUiController)}] Bylo nalezeno více vlastněných PlayerTechnology objektů. Zkontroluj vlastnictví hráčských NetworkObjectů.",
                        this
                    );
                    return false;
                }

                candidate = possible;
            }
        }

        if (candidate == null)
        {
            if (!warnedMissingTechnology)
            {
                Debug.LogWarning(
                    $"[{nameof(ResearchUiController)}] Zatím není dostupný spawned PlayerTechnology s IsOwner == true. Controller bude hledání opakovat.",
                    this
                );
                warnedMissingTechnology = true;
            }

            return false;
        }

        UnbindPlayerTechnology();

        playerTechnology = candidate;
        boundTechnology = candidate;
        boundTechnology.SelectedDoctrineChanged += HandleSelectedDoctrineChanged;
        boundTechnology.SelectedResearchChanged += HandleSelectedResearchChanged;
        boundTechnology.NetworkObjectDespawned += HandlePlayerTechnologyDespawned;

        warnedMissingTechnology = false;

        Debug.Log(
            $"[{nameof(ResearchUiController)}] Připojen lokální PlayerTechnology. OwnerClientId: {boundTechnology.OwnerClientId}.",
            this
        );

        return true;
    }

    private void UnbindPlayerTechnology()
    {
        if (boundTechnology != null)
        {
            boundTechnology.SelectedDoctrineChanged -= HandleSelectedDoctrineChanged;
            boundTechnology.SelectedResearchChanged -= HandleSelectedResearchChanged;
            boundTechnology.NetworkObjectDespawned -= HandlePlayerTechnologyDespawned;
        }

        if (playerTechnology == boundTechnology)
            playerTechnology = null;

        boundTechnology = null;
    }

    private void HandlePlayerTechnologyDespawned(PlayerTechnology despawnedTechnology)
    {
        if (despawnedTechnology != boundTechnology)
            return;

        Debug.LogWarning($"[{nameof(ResearchUiController)}] Lokální PlayerTechnology byl despawnut. UI čeká na nový objekt.", this);
        UnbindPlayerTechnology();
        ClearPendingRequests();
        ClearDoctrineButtons();
        ClearResearchButtons();
        displayedDoctrineType = DoctrineType.None;
        forceDoctrineRebuild = false;
        SetSectionsVisible(false, false);
    }

    private void SynchronizeViewFromNetworkState()
    {
        if (boundTechnology == null || !boundTechnology.IsSpawned || !boundTechnology.IsOwner)
        {
            SetSectionsVisible(false, false);
            return;
        }

        DoctrineType selectedDoctrine = boundTechnology.SelectedDoctrineValue;

        if (selectedDoctrine == DoctrineType.None)
        {
            ShowDoctrineSelection();
            return;
        }

        DoctrineDefinition selectedDefinition = boundTechnology.GetDoctrine(selectedDoctrine);

        if (selectedDefinition == null)
        {
            Debug.LogError(
                $"[{nameof(ResearchUiController)}] Síťový stav obsahuje doktrínu '{selectedDoctrine}', ale PlayerTechnology pro ni nemá platnou DoctrineDefinition.",
                this
            );

            ShowDoctrineSelection();
            return;
        }

        ShowResearchesForDoctrine(selectedDefinition);
    }

    private void HandleSelectedDoctrineChanged(DoctrineType previous, DoctrineType current)
    {
        Debug.Log(
            $"[{nameof(ResearchUiController)}] Změna síťového stavu doktríny: {previous} -> {current}.",
            this
        );

        if (doctrineRequestPending && current == pendingDoctrineType)
        {
            ConfirmDoctrineRequest(current);
            return;
        }

        if (current == DoctrineType.None)
        {
            ShowDoctrineSelection();
            return;
        }

        if (boundTechnology == null)
            return;

        DoctrineDefinition definition = boundTechnology.GetDoctrine(current);

        if (definition == null)
        {
            Debug.LogError(
                $"[{nameof(ResearchUiController)}] Změněná doktrína '{current}' nemá odpovídající DoctrineDefinition.",
                this
            );
            SetSectionsVisible(false, false);
            return;
        }

        ShowResearchesForDoctrine(definition);
    }

    private void HandleSelectedResearchChanged(string previous, string current)
    {
        Debug.Log(
            $"[{nameof(ResearchUiController)}] Změna síťového stavu výzkumu: '{previous}' -> '{current}'.",
            this
        );

        if (researchRequestPending &&
            string.Equals(current ?? string.Empty, pendingResearchId, StringComparison.Ordinal))
        {
            ConfirmResearchRequest(current);
        }
    }

    private void ShowDoctrineSelection()
    {
        if (boundTechnology == null)
        {
            SetSectionsVisible(false, false);
            return;
        }

        EnsureDoctrineButtons();
        displayedDoctrineType = DoctrineType.None;
        SetSectionsVisible(true, false);
        SubscribeGeneratedUiEvents();
    }

    private void ShowResearchesForDoctrine(DoctrineDefinition doctrine)
    {
        if (doctrine == null)
        {
            Debug.LogError($"[{nameof(ResearchUiController)}] Nelze zobrazit výzkumy: DoctrineDefinition je null.", this);
            ShowDoctrineSelection();
            return;
        }

        if (boundTechnology == null)
            return;

        EnsureResearchButtons(doctrine);
        displayedDoctrineType = doctrine.DoctrineType;
        SetSectionsVisible(false, true);
        SubscribeGeneratedUiEvents();

        Debug.Log(
            $"[{nameof(ResearchUiController)}] Zobrazeny výzkumy doktríny '{doctrine.DisplayName}'.",
            this
        );
    }

    private void HandleBackToDoctrinesClicked()
    {
        CloseAllResearchPopups();
        ShowDoctrineSelection();
    }

    private void EnsureDoctrineButtons()
    {
        if (doctrineGridParent == null || doctrinePrefab == null || boundTechnology == null)
            return;

        List<DoctrineDefinition> definitions = GetValidDoctrineDefinitions();
        bool matches = !forceDoctrineRebuild && doctrineEntries.Count == definitions.Count;

        if (matches)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (doctrineEntries[i].Selector == null ||
                    doctrineEntries[i].Definition != definitions[i])
                {
                    matches = false;
                    break;
                }
            }
        }

        if (matches)
        {
            CheckDoctrineGridLayout();
            return;
        }

        ClearDoctrineButtons();

        for (int i = 0; i < definitions.Count; i++)
        {
            DoctrineDefinition definition = definitions[i];
            DoctrineSelector selector = Instantiate(doctrinePrefab, doctrineGridParent, false);

            if (selector == null)
            {
                Debug.LogError($"[{nameof(ResearchUiController)}] Nepodařilo se vytvořit kartu doktríny '{definition.DisplayName}'.", this);
                continue;
            }

            selector.Fill(definition);

            DoctrineEntry entry = new DoctrineEntry
            {
                Definition = definition,
                Selector = selector
            };

            //entry.Listener = (selectedDefinition, accepted) => HandleDoctrineDecision(entry, selectedDefinition, accepted);
            doctrineEntries.Add(entry);
        }

        forceDoctrineRebuild = false;
        CheckDoctrineGridLayout();
        SubscribeGeneratedUiEvents();

        Debug.Log($"[{nameof(ResearchUiController)}] Vytvořeno karet doktrín: {doctrineEntries.Count}.", this);
    }

    private List<DoctrineDefinition> GetValidDoctrineDefinitions()
    {
        List<DoctrineDefinition> definitions = new List<DoctrineDefinition>();
        HashSet<DoctrineType> seenTypes = new HashSet<DoctrineType>();

        if (boundTechnology == null || boundTechnology.Doctrines == null)
            return definitions;

        for (int i = 0; i < boundTechnology.Doctrines.Count; i++)
        {
            DoctrineDefinition definition = boundTechnology.Doctrines[i];

            if (definition == null)
            {
                Debug.LogWarning($"[{nameof(ResearchUiController)}] Seznam doktrín obsahuje null položku; přeskakuji ji.", this);
                continue;
            }

            if (definition.DoctrineType == DoctrineType.None)
                continue;

            if (!seenTypes.Add(definition.DoctrineType))
            {
                Debug.LogWarning(
                    $"[{nameof(ResearchUiController)}] Doktrína '{definition.DisplayName}' používá duplicitní DoctrineType '{definition.DoctrineType}'; druhou položku přeskakuji.",
                    definition
                );
                continue;
            }

            definitions.Add(definition);
        }

        return definitions;
    }

    private void EnsureResearchButtons(DoctrineDefinition doctrine)
    {
        if (researchGridParent == null || researchButtonPrefab == null || boundTechnology == null)
            return;

        List<ResearchDefinition> definitions = GetValidResearchDefinitions(doctrine);
        bool matches = researchEntries.Count == definitions.Count;

        if (matches)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (researchEntries[i].Instance == null ||
                    researchEntries[i].Definition != definitions[i] ||
                    researchEntries[i].Popup == null)
                {
                    matches = false;
                    break;
                }
            }
        }

        if (matches)
        {
            CheckResearchGridLayout();
            return;
        }

        ClearResearchButtons();

        for (int i = 0; i < definitions.Count; i++)
        {
            ResearchDefinition definition = definitions[i];
            GameObject instance = Instantiate(researchButtonPrefab, researchGridParent, false);

            if (instance == null)
            {
                Debug.LogError($"[{nameof(ResearchUiController)}] Nepodařilo se vytvořit tlačítko výzkumu '{definition.DisplayName}'.", this);
                continue;
            }

            ResearchPopup researchPopupItem = instance.GetComponent<ResearchPopup>();

            if (researchPopupItem == null)
            {
                Debug.LogError(
                    $"[{nameof(ResearchUiController)}] Prefab výzkumu '{instance.name}' nemá na kořenovém objektu komponentu ResearchPopup. Prefab odstraňuji.",
                    instance
                );
                instance.SetActive(false);
                Destroy(instance);
                continue;
            }

            researchPopupItem.Fill(definition);

            ResearchEntry entry = new ResearchEntry
            {
                Definition = definition,
                Instance = instance,
                Popup = researchPopupItem,
                ClickListener = HandleResearchButtonClicked,
                AcceptListener = () => HandleResearchAcceptClicked(researchPopupItem)
            };
            researchEntries.Add(entry);
        }

        CheckResearchGridLayout();
        SubscribeGeneratedUiEvents();

        Debug.Log(
            $"[{nameof(ResearchUiController)}] Vytvořeno tlačítek výzkumů pro '{doctrine.DisplayName}': {researchEntries.Count}.",
            this
        );
    }

    private List<ResearchDefinition> GetValidResearchDefinitions(DoctrineDefinition doctrine)
    {
        List<ResearchDefinition> definitions = new List<ResearchDefinition>();
        HashSet<string> seenResearchIds = new HashSet<string>(StringComparer.Ordinal);

        if (doctrine == null || doctrine.Researches == null)
            return definitions;

        for (int i = 0; i < doctrine.Researches.Count; i++)
        {
            ResearchDefinition listedDefinition = doctrine.Researches[i];

            if (listedDefinition == null)
            {
                Debug.LogWarning(
                    $"[{nameof(ResearchUiController)}] Doktrína '{doctrine.DisplayName}' obsahuje null výzkum; přeskakuji jej.",
                    doctrine
                );
                continue;
            }

            if (string.IsNullOrWhiteSpace(listedDefinition.ResearchId))
            {
                Debug.LogError(
                    $"[{nameof(ResearchUiController)}] Výzkum v doktríně '{doctrine.DisplayName}' nemá platné ResearchId.",
                    listedDefinition
                );
                continue;
            }

            if (!seenResearchIds.Add(listedDefinition.ResearchId))
            {
                Debug.LogWarning(
                    $"[{nameof(ResearchUiController)}] Doktrína '{doctrine.DisplayName}' obsahuje duplicitní ResearchId '{listedDefinition.ResearchId}'; duplicitní tlačítko nevytvářím.",
                    doctrine
                );
                continue;
            }

            // Datový katalog PlayerTechnology je autoritativní lookup pro serverové RPC.
            // Pokud v něm výzkum chybí, UI ho nenabídne, protože by jej server nemohl ověřit.
            ResearchDefinition canonicalDefinition = boundTechnology.GetResearch(listedDefinition.ResearchId);

            if (canonicalDefinition == null)
            {
                Debug.LogError(
                    $"[{nameof(ResearchUiController)}] Výzkum '{listedDefinition.ResearchId}' je uveden v doktríně '{doctrine.DisplayName}', ale chybí v katalogu researches v PlayerTechnology.",
                    listedDefinition
                );
                continue;
            }

            definitions.Add(canonicalDefinition);
        }

        return definitions;
    }

    private void HandleResearchButtonClicked(ResearchPopup clickedPopup, ResearchDefinition research)
    {
        if (clickedPopup == null || research == null)
        {
            Debug.LogError($"[{nameof(ResearchUiController)}] Kliknutí předalo neplatný ResearchPopup nebo ResearchDefinition.", this);
            return;
        }

        if (boundTechnology == null || !boundTechnology.IsSpawned || !boundTechnology.IsOwner)
        {
            Debug.LogError($"[{nameof(ResearchUiController)}] Nelze otevřít výzkum: lokální PlayerTechnology není připravený.", this);
            return;
        }

        if (string.IsNullOrWhiteSpace(research.ResearchId) ||
            boundTechnology.GetResearch(research.ResearchId) == null)
        {
            Debug.LogError($"[{nameof(ResearchUiController)}] Výzkum '{research.name}' nemá platné ResearchId nebo chybí v PlayerTechnology.", research);
            return;
        }

        // Zobrazujeme vždy jen jeden detail výzkumu. Jiné položky zůstanou
        // v gridu, ale jejich popup obsah se zavře.
        for (int i = 0; i < researchEntries.Count; i++)
        {
            ResearchPopup otherPopup = researchEntries[i].Popup;
            if (otherPopup != null && otherPopup != clickedPopup)
                otherPopup.ClosePopup();
        }

        Debug.Log($"[{nameof(ResearchUiController)}] Otevřen výzkum '{research.DisplayName}'.", clickedPopup);
    }

    private void HandleResearchAcceptClicked(ResearchPopup sourcePopup)
    {
        if (sourcePopup == null || sourcePopup.CurrentResearch == null)
        {
            Debug.LogWarning($"[{nameof(ResearchUiController)}] Potvrzení výzkumu ignorováno: položka nemá načtený výzkum.", this);
            return;
        }

        if (researchRequestPending || doctrineRequestPending)
        {
            Debug.LogWarning($"[{nameof(ResearchUiController)}] Jiný požadavek ještě čeká na síťové potvrzení.", this);
            return;
        }

        if (boundTechnology == null || !boundTechnology.IsSpawned || !boundTechnology.IsOwner)
        {
            Debug.LogError($"[{nameof(ResearchUiController)}] Nelze potvrdit výzkum: lokální PlayerTechnology není připravený.", this);
            return;
        }

        ResearchDefinition research = sourcePopup.CurrentResearch;

        if (string.IsNullOrWhiteSpace(research.ResearchId) ||
            boundTechnology.GetResearch(research.ResearchId) == null)
        {
            Debug.LogError($"[{nameof(ResearchUiController)}] Nelze potvrdit neplatný výzkum bez odpovídajícího ResearchId v PlayerTechnology.", research);
            return;
        }

        researchRequestPending = true;
        pendingResearchId = research.ResearchId;
        pendingResearchPopup = sourcePopup;
        doctrineRequestPending = false;
        pendingDoctrineType = DoctrineType.None;
        pendingRequestStartedAt = Time.unscaledTime;

        Debug.Log($"[{nameof(ResearchUiController)}] Odesílám požadavek na výzkum '{research.DisplayName}'. Cena se ověřuje na serveru.", this);
        boundTechnology.SelectResearchServerRpc(research.ResearchId);

        ResolvePendingRequestsFromCurrentState();
    }



    private void ConfirmDoctrineRequest(DoctrineType selectedType)
    {
        doctrineRequestPending = false;
        pendingDoctrineType = DoctrineType.None;

        if (boundTechnology == null)
            return;

        DoctrineDefinition definition = boundTechnology.GetDoctrine(selectedType);

        if (definition == null)
        {
            Debug.LogError($"[{nameof(ResearchUiController)}] Server potvrdil doktrínu '{selectedType}', ale lokální katalog ji neobsahuje.", this);
            ShowDoctrineSelection();
            return;
        }

        Debug.Log($"[{nameof(ResearchUiController)}] Server potvrdil doktrínu '{definition.DisplayName}'. Zobrazuji její výzkumy.", this);
        ShowResearchesForDoctrine(definition);
    }

    private void ConfirmResearchRequest(string researchId)
    {
        string confirmedId = researchId;
        researchRequestPending = false;
        pendingResearchId = string.Empty;

        Debug.Log($"[{nameof(ResearchUiController)}] Síťový stav potvrdil výzkum '{confirmedId}'.", this);

        if (pendingResearchPopup != null)
            pendingResearchPopup.ClosePopup();

        pendingResearchPopup = null;
    }

    private void ResolvePendingRequestsFromCurrentState()
    {
        if (boundTechnology == null || !boundTechnology.IsSpawned)
            return;

        if (doctrineRequestPending &&
            boundTechnology.SelectedDoctrineValue == pendingDoctrineType)
        {
            ConfirmDoctrineRequest(pendingDoctrineType);
        }

        if (researchRequestPending &&
            string.Equals(
                boundTechnology.CurrentSelectedResearchId ?? string.Empty,
                pendingResearchId,
                StringComparison.Ordinal))
        {
            ConfirmResearchRequest(pendingResearchId);
        }
    }

    private void HandleRequestTimeout()
    {
        if (doctrineRequestPending)
        {
            DoctrineType requestedType = pendingDoctrineType;
            doctrineRequestPending = false;
            pendingDoctrineType = DoctrineType.None;

            Debug.LogWarning(
                $"[{nameof(ResearchUiController)}] Výběr doktríny '{requestedType}' nebyl potvrzen síťovým stavem do {serverRequestTimeoutSeconds:0.##} s. Zkontroluj serverovou konzoli a konfiguraci PlayerTechnology.",
                this
            );

            SynchronizeViewFromNetworkState();
            SubscribeGeneratedUiEvents();
            return;
        }

        if (researchRequestPending)
        {
            string requestedId = pendingResearchId;
            researchRequestPending = false;
            pendingResearchId = string.Empty;

            Debug.LogWarning(
                $"[{nameof(ResearchUiController)}] Výzkum '{requestedId}' nebyl potvrzen síťovým stavem do {serverRequestTimeoutSeconds:0.##} s. Popup zůstává otevřený; zkontroluj serverovou konzoli, požadavky výzkumu a dostupné Corium.",
                this
            );
        }
    }

    private void SetSectionsVisible(bool showDoctrines, bool showResearches)
    {
        if (doctrineSectionParent != null)
            doctrineSectionParent.SetActive(showDoctrines);
        else if (doctrineGridParent != null)
            doctrineGridParent.gameObject.SetActive(showDoctrines);

        if (researchSectionParent != null)
            researchSectionParent.SetActive(showResearches);
        else if (researchGridParent != null)
            researchGridParent.gameObject.SetActive(showResearches);
    }

    private void CheckDoctrineGridLayout()
    {
        if (!warnIfGridLayoutGroupMissing || doctrineGridParent == null || warnedMissingDoctrineGridLayout)
            return;

        if (doctrineGridParent.GetComponent<GridLayoutGroup>() == null)
        {
            Debug.LogWarning($"[{nameof(ResearchUiController)}] Doctrine Grid Parent nemá GridLayoutGroup. Přidej tuto komponentu a nastav Cell Size, Spacing a Constraint v Inspectoru.", doctrineGridParent);
            warnedMissingDoctrineGridLayout = true;
        }
    }

    private void CheckResearchGridLayout()
    {
        if (!warnIfGridLayoutGroupMissing || researchGridParent == null || warnedMissingResearchGridLayout)
            return;

        if (researchGridParent.GetComponent<GridLayoutGroup>() == null)
        {
            Debug.LogWarning($"[{nameof(ResearchUiController)}] Research Grid Parent nemá GridLayoutGroup. Přidej tuto komponentu a nastav Cell Size, Spacing a Constraint v Inspectoru.", researchGridParent);
            warnedMissingResearchGridLayout = true;
        }
    }

    private void SubscribeGeneratedUiEvents()
    {
        for (int i = 0; i < doctrineEntries.Count; i++)
        {
            DoctrineEntry entry = doctrineEntries[i];
            if (entry.Selector == null || entry.Listener == null)
                continue;

            entry.Selector.OnDecisionMade -= entry.Listener;
            entry.Selector.OnDecisionMade += entry.Listener;
        }

        for (int i = 0; i < researchEntries.Count; i++)
        {
            ResearchEntry entry = researchEntries[i];
            if (entry.Popup == null)
                continue;

            if (entry.ClickListener != null)
            {
                entry.Popup.OnResearchClicked -= entry.ClickListener;
                entry.Popup.OnResearchClicked += entry.ClickListener;
            }

            if (entry.AcceptListener != null)
            {
                entry.Popup.OnAcceptClicked.RemoveListener(entry.AcceptListener);
                entry.Popup.OnAcceptClicked.AddListener(entry.AcceptListener);
            }
        }
    }

    private void UnsubscribeGeneratedUiEvents()
    {
        for (int i = 0; i < doctrineEntries.Count; i++)
        {
            DoctrineEntry entry = doctrineEntries[i];
            if (entry.Selector != null && entry.Listener != null)
                entry.Selector.OnDecisionMade -= entry.Listener;
        }

        for (int i = 0; i < researchEntries.Count; i++)
        {
            ResearchEntry entry = researchEntries[i];
            if (entry.Popup == null)
                continue;

            if (entry.ClickListener != null)
                entry.Popup.OnResearchClicked -= entry.ClickListener;

            if (entry.AcceptListener != null)
                entry.Popup.OnAcceptClicked.RemoveListener(entry.AcceptListener);
        }
    }

    private void ClearDoctrineButtons()
    {
        for (int i = 0; i < doctrineEntries.Count; i++)
        {
            DoctrineEntry entry = doctrineEntries[i];
            if (entry.Selector == null)
                continue;

            if (entry.Listener != null)
                entry.Selector.OnDecisionMade -= entry.Listener;

            entry.Selector.gameObject.SetActive(false);
            Destroy(entry.Selector.gameObject);
        }

        doctrineEntries.Clear();
    }

    private void ClearResearchButtons()
    {
        for (int i = 0; i < researchEntries.Count; i++)
        {
            ResearchEntry entry = researchEntries[i];
            if (entry.Popup != null)
            {
                if (entry.ClickListener != null)
                    entry.Popup.OnResearchClicked -= entry.ClickListener;

                if (entry.AcceptListener != null)
                    entry.Popup.OnAcceptClicked.RemoveListener(entry.AcceptListener);

                entry.Popup.ClosePopup();
            }

            if (entry.Instance == null)
                continue;

            entry.Instance.SetActive(false);
            Destroy(entry.Instance);
        }

        researchEntries.Clear();
    }

    private void CloseAllResearchPopups()
    {
        for (int i = 0; i < researchEntries.Count; i++)
        {
            if (researchEntries[i].Popup != null)
                researchEntries[i].Popup.ClosePopup();
        }
    }

    private void ClearPendingRequests()
    {
        doctrineRequestPending = false;
        pendingDoctrineType = DoctrineType.None;
        researchRequestPending = false;
        pendingResearchId = string.Empty;
        pendingResearchPopup = null;
        pendingRequestStartedAt = 0f;
    }
}
