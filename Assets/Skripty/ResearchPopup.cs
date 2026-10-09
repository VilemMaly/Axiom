using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class ResearchPopup : MonoBehaviour
{
    [Header("Research Data")]
    [SerializeField] private ResearchDefinition currentResearch;

    [Header("Text Elements")]
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text energyText;
    [SerializeField] private TMP_Text timeText;

    [Header("Icon Button")]
    [SerializeField] private Button iconButton;
    [SerializeField] private Image iconImage;

    [Header("Popup Elements")]
    [Tooltip("Empty parent obsahující Description, Name, Cost, Energy, Time, Accept a Exit.")]
    [SerializeField] private GameObject popupContentParent;

    [SerializeField] private Button acceptButton;
    [SerializeField] private Button exitButton;

    [Header("Upgrade Popups")]
    [Tooltip("Prefab obsahující komponentu UpgradePopup na kořenovém objektu.")]
    [SerializeField] private UpgradePopup upgradePopupPrefab;

    [Tooltip("RectTransform prázdného UI parentu, do kterého se budou upgrady spawnovat.")]
    [SerializeField] private RectTransform upgradesParent;

    [Tooltip("Volitelný RectTransform označující pravý konec gridu. Jeho pivot představuje pravou hranici.")]
    [SerializeField] private RectTransform gridEndMarker;

    [Tooltip("Vodorovný a svislý rozestup mezi upgrady.")]
    [SerializeField] private Vector2 upgradeSpacing = new Vector2(12f, 12f);

    [Tooltip("Šířka a výška kořenového RectTransform každého upgradu.")]
    [SerializeField] private Vector2 upgradeItemSize = new Vector2(180f, 100f);

    [Tooltip("Odsazení od levého a horního okraje gridu.")]
    [SerializeField] private Vector2 gridPadding = new Vector2(10f, 10f);

    [Tooltip("Automaticky nastaví výšku parentu podle počtu řádků.")]
    [SerializeField] private bool resizeGridParentHeight;

    [Header("Accept Button Event")]
    [Tooltip("Funkce volané po kliknutí na tlačítko Accept. ResearchUiController k této události přidává serverový požadavek.")]
    [SerializeField] private UnityEvent onAcceptClicked = new UnityEvent();

    private readonly List<UpgradePopup> spawnedUpgradePopups = new List<UpgradePopup>();
    private float lastLayoutWidth = float.NaN;

    /// <summary>Aktuálně zobrazený výzkum.</summary>
    public ResearchDefinition CurrentResearch => currentResearch;

    /// <summary>
    /// Vyvolá se po kliknutí na ikonu výzkumu. Předává instanci tohoto popupu
    /// i konkrétní ResearchDefinition, takže controller nemusí hledat objekty podle jména.
    /// </summary>
    public event Action<ResearchPopup, ResearchDefinition> OnResearchClicked;

    /// <summary>Událost kliknutí na Accept. Další posluchači se přidávají bez nahrazování UnityEvent nastaveného v Inspectoru.</summary>
    public UnityEvent OnAcceptClicked => onAcceptClicked;

    /// <summary>Vyvolá se po zavření detailu výzkumu.</summary>
    public event Action OnPopupClosed;

    private void Awake()
    {
        if (iconButton != null)
        {
            iconButton.onClick.RemoveListener(HandleIconButtonClicked);
            iconButton.onClick.AddListener(HandleIconButtonClicked);
        }

        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(ClosePopup);
            exitButton.onClick.AddListener(ClosePopup);
        }

        if (acceptButton != null)
        {
            acceptButton.onClick.RemoveListener(HandleAcceptClicked);
            acceptButton.onClick.AddListener(HandleAcceptClicked);
        }

        // Inicializace zavřeného stavu nesmí vyvolat událost uživatelského zavření.
        SetClosedVisualState();
    }

    private void OnDestroy()
    {
        if (iconButton != null)
            iconButton.onClick.RemoveListener(HandleIconButtonClicked);

        if (exitButton != null)
            exitButton.onClick.RemoveListener(ClosePopup);

        if (acceptButton != null)
            acceptButton.onClick.RemoveListener(HandleAcceptClicked);

        ClearSpawnedUpgradePopups();
    }

    private void LateUpdate()
    {
        if (spawnedUpgradePopups.Count == 0 || upgradesParent == null)
            return;

        float currentWidth = GetAvailableGridWidth();
        if (!Mathf.Approximately(currentWidth, lastLayoutWidth))
            LayoutUpgradePopups();
    }

    /// <summary>Načte data výzkumu, vyplní UI a vytvoří jeho UpgradePopup položky.</summary>
    public void Fill(ResearchDefinition researchData)
    {
        if (researchData == null)
        {
            Debug.LogWarning($"[{nameof(ResearchPopup)}] Fill dostal null ResearchDefinition.", this);
            return;
        }

        currentResearch = researchData;

        if (nameText != null)
            nameText.text = researchData.DisplayName;

        if (descriptionText != null)
            descriptionText.text = researchData.Description;

        if (costText != null)
            costText.text = $"{researchData.CoriumCost:N0} Corium";

        if (energyText != null)
            energyText.text = $"{researchData.EnergyPerSecond:0.##} energie/s";

        if (timeText != null)
            timeText.text = $"{researchData.ResearchTime:0.##} s";

        if (iconImage != null)
        {
            iconImage.sprite = researchData.Icon;
            iconImage.enabled = researchData.Icon != null;
        }

        SpawnUpgradePopups(researchData);

        Debug.Log($"[{nameof(ResearchPopup)}] Načten výzkum: {researchData.DisplayName}.", this);
    }

    private void SpawnUpgradePopups(ResearchDefinition researchData)
    {
        ClearSpawnedUpgradePopups();

        if (upgradePopupPrefab == null)
        {
            Debug.LogError($"[{nameof(ResearchPopup)}] Není přiřazen Upgrade Popup Prefab.", this);
            return;
        }

        if (upgradesParent == null)
        {
            Debug.LogError($"[{nameof(ResearchPopup)}] Není přiřazen Upgrades Parent.", this);
            return;
        }

        if (researchData.Upgrades == null || researchData.Upgrades.Count == 0)
        {
            Debug.Log($"[{nameof(ResearchPopup)}] Výzkum '{researchData.DisplayName}' nemá žádné upgrady.", this);
            return;
        }

        for (int i = 0; i < researchData.Upgrades.Count; i++)
        {
            UpgradeDefinition upgradeData = researchData.Upgrades[i];

            if (upgradeData == null)
            {
                Debug.LogWarning($"[{nameof(ResearchPopup)}] Výzkum '{researchData.DisplayName}' obsahuje prázdný upgrade; přeskakuji jej.", this);
                continue;
            }

            UpgradePopup spawnedPopup = Instantiate(upgradePopupPrefab, upgradesParent, false);
            if (spawnedPopup == null)
            {
                Debug.LogError($"[{nameof(ResearchPopup)}] Nepodařilo se vytvořit UpgradePopup.", this);
                continue;
            }

            RectTransform itemRect = spawnedPopup.transform as RectTransform;
            if (itemRect == null)
            {
                Debug.LogError($"[{nameof(ResearchPopup)}] Kořen prefabu UpgradePopup musí mít RectTransform.", spawnedPopup);
                Destroy(spawnedPopup.gameObject);
                continue;
            }

            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(0f, 1f);
            itemRect.pivot = new Vector2(0f, 1f);
            itemRect.sizeDelta = new Vector2(
                Mathf.Max(1f, upgradeItemSize.x),
                Mathf.Max(1f, upgradeItemSize.y)
            );

            spawnedPopup.Fill(upgradeData);
            spawnedUpgradePopups.Add(spawnedPopup);
        }

        LayoutUpgradePopups();
    }

    private void LayoutUpgradePopups()
    {
        if (upgradesParent == null || spawnedUpgradePopups.Count == 0)
            return;

        float itemWidth = Mathf.Max(1f, upgradeItemSize.x);
        float itemHeight = Mathf.Max(1f, upgradeItemSize.y);
        float horizontalSpacing = Mathf.Max(0f, upgradeSpacing.x);
        float verticalSpacing = Mathf.Max(0f, upgradeSpacing.y);
        float availableWidth = GetAvailableGridWidth();
        float usableWidth = Mathf.Max(0f, availableWidth - gridPadding.x * 2f);

        int columns = Mathf.Max(
            1,
            Mathf.FloorToInt(
                (usableWidth + horizontalSpacing) / (itemWidth + horizontalSpacing)
            )
        );

        for (int i = 0; i < spawnedUpgradePopups.Count; i++)
        {
            UpgradePopup popup = spawnedUpgradePopups[i];
            if (popup == null)
                continue;

            RectTransform itemRect = popup.transform as RectTransform;
            if (itemRect == null)
                continue;

            int column = i % columns;
            int row = i / columns;

            float x = gridPadding.x + column * (itemWidth + horizontalSpacing);
            float y = -(gridPadding.y + row * (itemHeight + verticalSpacing));
            itemRect.anchoredPosition = new Vector2(x, y);
        }

        if (resizeGridParentHeight)
        {
            int rowCount = Mathf.CeilToInt(spawnedUpgradePopups.Count / (float)columns);
            float requiredHeight =
                gridPadding.y * 2f +
                rowCount * itemHeight +
                Mathf.Max(0, rowCount - 1) * verticalSpacing;

            upgradesParent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, requiredHeight);
        }

        lastLayoutWidth = availableWidth;
    }

    private float GetAvailableGridWidth()
    {
        if (upgradesParent == null)
            return 0f;

        if (gridEndMarker == null)
            return upgradesParent.rect.width;

        Vector3 markerLocalPosition = upgradesParent.InverseTransformPoint(gridEndMarker.position);
        float widthFromParentLeft = markerLocalPosition.x - upgradesParent.rect.xMin;

        return Mathf.Clamp(widthFromParentLeft, 0f, upgradesParent.rect.width);
    }

    private void ClearSpawnedUpgradePopups()
    {
        for (int i = 0; i < spawnedUpgradePopups.Count; i++)
        {
            UpgradePopup popup = spawnedUpgradePopups[i];
            if (popup == null)
                continue;

            popup.gameObject.SetActive(false);
            Destroy(popup.gameObject);
        }

        spawnedUpgradePopups.Clear();
        lastLayoutWidth = float.NaN;
    }

    private void HandleIconButtonClicked()
    {
        if (currentResearch == null)
        {
            Debug.LogWarning($"[{nameof(ResearchPopup)}] Kliknutí na ikonu ignorováno, protože ještě nebyla načtena data výzkumu.", this);
            return;
        }

        // Controller může nejprve zavřít jiné otevřené výzkumy. Po návratu
        // se otevře detail této konkrétní položky.
        OnResearchClicked?.Invoke(this, currentResearch);
        OpenPopup();
    }

    public void OpenPopup()
    {
        if (popupContentParent == null)
        {
            Debug.LogError($"[{nameof(ResearchPopup)}] Není přiřazen Popup Content Parent.", this);
            return;
        }

        popupContentParent.SetActive(true);

        if (iconButton != null)
            iconButton.gameObject.SetActive(false);
    }

    public void ClosePopup()
    {
        bool wasOpen = popupContentParent != null && popupContentParent.activeSelf;
        SetClosedVisualState();

        if (wasOpen)
            OnPopupClosed?.Invoke();
    }

    private void SetClosedVisualState()
    {
        if (popupContentParent != null)
            popupContentParent.SetActive(false);

        if (iconButton != null)
            iconButton.gameObject.SetActive(true);
    }

    private void HandleAcceptClicked()
    {
        if (currentResearch == null)
        {
            Debug.LogWarning($"[{nameof(ResearchPopup)}] Nelze potvrdit výzkum, protože nejsou načtena data.", this);
            return;
        }

        onAcceptClicked?.Invoke();
    }
}
