
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class ResearchInspectorUI : MonoBehaviour
{
    [Header("Common")]
    [SerializeField] private TMP_Text Name;
    [SerializeField] private TMP_Text Description;
    [SerializeField] private Image iconImage;

    [Header("Doctrine and requirements")]
    [SerializeField] private TMP_Text doctrineTypeText;
    [SerializeField] private Image requiredResearchImage;
    [SerializeField] private TMP_Text requiredResearchText;
    [SerializeField] private Image requiredResearchCompletedImage;
    [SerializeField] private Sprite checkmarkGreen;
    [SerializeField] private Sprite crossRed;

    [Header("Status (reserved)")]
    [SerializeField] private Image statusImage;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Sprite researchInProgressSprite;

    [Header("Research")]
    [SerializeField] private TMP_Text CoriumCost;
    [SerializeField] private TMP_Text EnergyPerSecond;
    [SerializeField] private TMP_Text TimeSeconds;

    [Header("Upgrade")]
    [SerializeField] private TMP_Text ExtraInfo;

    [Header("Optional UI Groups")]
    [SerializeField] private GameObject CoriumCostRoot;
    [SerializeField] private GameObject EnergyPerSecondRoot;
    [SerializeField] private GameObject TimeSecondsRoot;
    [SerializeField] private GameObject ExtraInfoRoot;

    [Header("Buttons")]
    [SerializeField] private Button acceptButton;
    [SerializeField] private Button declineButton;

    private DoctrineDefinition currentDoctrine;
    private ResearchDefinition currentResearch;
    private UpgradeDefinition currentUpgrade;
    private List<GameObject> upgradeList = new List<GameObject>();
    [SerializeField] public Transform upgradePosition;
    [SerializeField] public GameObject UpgradePrefab;
    private PlayerTechnology playerTech;
    [SerializeField] public int Gridx = 300;

    private enum SelectedDefinition
    {
        None,
        Doctrine,
        Research,
        Upgrade
    }

    private SelectedDefinition selected = SelectedDefinition.None;
    private bool selectionLocked;
    private InputSystem_Actions controls;

    private void Update()
    {
        if (selected == SelectedDefinition.Research && currentResearch != null)
        {
            RefreshResearchStatus(currentResearch);
            ResearchDefinition required = currentResearch.RequiredResearches != null &&
                currentResearch.RequiredResearches.Count > 0
                    ? currentResearch.RequiredResearches[0]
                    : null;
            SetRequiredResearchCompleted(required);
        }
    }

    private void Start()
    {
        if (InputManager.StaticClass != null && InputManager.StaticClass.Controls != null)
        {
            controls = InputManager.StaticClass.Controls;
            controls.UI.Click.performed += OnLeftClick;
        }
    }

    private void OnDestroy()
    {
        if (controls != null)
            controls.UI.Click.performed -= OnLeftClick;
    }

    private void OnLeftClick(InputAction.CallbackContext context)
    {
        if (selectionLocked &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            selectionLocked = false;
        }
    }

    public void HoverDoctrine(DoctrineDefinition value) { if (!selectionLocked) FillInspector(value); }
    public void HoverResearch(ResearchDefinition value) { if (!selectionLocked) FillInspector(value); }
    public void HoverUpgrade(UpgradeDefinition value) { if (!selectionLocked) FillInspector(value); }

    public void ClickDoctrine(DoctrineDefinition value) { ToggleLock(value); FillInspector(value); }
    public void ClickResearch(ResearchDefinition value) { ToggleLock(value); FillInspector(value); }
    public void ClickUpgrade(UpgradeDefinition value) { ToggleLock(value); FillInspector(value); }

    private void ToggleLock(UnityEngine.Object value)
    {
        bool same = (selected == SelectedDefinition.Doctrine && currentDoctrine == value) ||
                    (selected == SelectedDefinition.Research && currentResearch == value) ||
                    (selected == SelectedDefinition.Upgrade && currentUpgrade == value);
        selectionLocked = !(selectionLocked && same);
    }

    private void Awake()
    {
        ClearInspector();
    }

    public void SetPlayerTechnology(PlayerTechnology technology)
    {
        playerTech = technology;
    }

    public void FillInspector(DoctrineDefinition doctrine)
    {
        if (doctrine == null)
            return;

        EraseUpgrades();

        currentDoctrine = doctrine;
        currentResearch = null;
        currentUpgrade = null;
        selected = SelectedDefinition.Doctrine;

        SetCommon(
            $"{doctrine.DisplayName} ({doctrine.DoctrineType})",
            doctrine.Description,
            doctrine.Icon
        );

        SetText(doctrineTypeText, "");
        SetRequiredResearch(null);
        SetRequiredResearchCompleted(null);
        SetResearchStatus("", null);

        SetResearchInfo(false);
        SetActive(ExtraInfoRoot, false);

        SetButton(acceptButton, true);
        SetButton(declineButton, false);
    }

    private void EraseUpgrades()
{
    foreach (GameObject upgradeObject in upgradeList)
    {
        if (upgradeObject == null)
            continue;

        UpgradePopup popup = upgradeObject.GetComponent<UpgradePopup>();

        if (popup != null)
            popup.Opened -= ClickUpgrade;
            popup.Hovered -= HoverUpgrade;

        Destroy(upgradeObject);
    }

    upgradeList.Clear();
}

    public void FillInspector(ResearchDefinition research)
    {
        if (research == null)
            return;
        
        EraseUpgrades();

        currentResearch = research;
        currentDoctrine = null;
        currentUpgrade = null;
        selected = SelectedDefinition.Research;

        SetCommon(
            research.DisplayName,
            research.Description,
            research.Icon
        );

        SetText(doctrineTypeText, playerTech != null &&
            playerTech.SelectedDoctrineValue != DoctrineType.None
                ? playerTech.SelectedDoctrineValue.ToString()
                : "");
        ResearchDefinition firstRequirement = research.RequiredResearches != null &&
            research.RequiredResearches.Count > 0
                ? research.RequiredResearches[0]
                : null;
        SetRequiredResearch(firstRequirement);
        SetRequiredResearchCompleted(firstRequirement);
        RefreshResearchStatus(research);

        SetResearchInfo(true);

        SetText(CoriumCost, research.CoriumCost.ToString());
        SetText(EnergyPerSecond, $"{research.EnergyPerSecond:0.##}");
        TimeSpan researchDuration = TimeSpan.FromSeconds(research.ResearchTime);
        SetText(TimeSeconds, $"{(int)researchDuration.TotalHours:00}:{researchDuration.Minutes:00}:{researchDuration.Seconds:00}");

        SetActive(ExtraInfoRoot, false);

        SetButton(acceptButton, true);
        SetButton(declineButton, false);
        int i = 0;
        foreach(UpgradeDefinition upgrade in research.Upgrades)
        {
            Vector3 grid = new Vector3(i*Gridx,0,0);
            GameObject UpgradeInstance = Instantiate(UpgradePrefab, upgradePosition);
            UpgradeInstance.transform.localPosition += grid;
            UpgradePopup skript = UpgradeInstance.GetComponent<UpgradePopup>();
            skript.Fill(upgrade);
            skript.Opened += ClickUpgrade;
            skript.Hovered += HoverUpgrade;
            upgradeList.Add(UpgradeInstance);
            i++;
        }
    }

    public void FillInspector(UpgradeDefinition upgrade)
    {
        if (upgrade == null)
            return;

        EraseUpgrades();

        currentUpgrade = upgrade;
        currentDoctrine = null;
        selected = SelectedDefinition.Upgrade;

        SetCommon(
            upgrade.DisplayName,
            upgrade.Description,
            upgrade.Icon
        );

        SetText(doctrineTypeText, "");
        SetRequiredResearch(null);
        SetRequiredResearchCompleted(null);
        SetResearchStatus("", null);

        SetResearchInfo(false);

        SetActive(ExtraInfoRoot, true);

        string target = upgrade.TargetType == UpgradeTargetType.Troop
            ? $"Jednotka: {upgrade.TroopType}"
            : $"Budova: {upgrade.BuildingType}";

        string value = upgrade.ValueType == UpgradeValueType.Percentage
            ? $"{upgrade.Value:+0.##;-0.##;0}%"
            : $"{upgrade.Value:+0.##;-0.##;0}";

        string stat = GetStatDisplayName(upgrade.StatType);
        if (upgrade.BuildingTierUpgrade)
            stat = $"Vylepšení budovy na úroveň {upgrade.BuildingUpgradeTier}";

        SetText(
            ExtraInfo,
            $"{target}\n{stat}: {value}"
        );

        // Upgrade se samostatně nepotvrzuje.
        // Je součástí výzkumu.
        SetButton(acceptButton, false);

        // Umožní návrat k předchozímu výzkumu.
        SetButton(declineButton, currentResearch != null);
    }

    public void AcceptClicked()
    {
        if (playerTech == null)
        {
            Debug.LogWarning(
                "ResearchInspectorUI: PlayerTechnology není nastaven."
            );
            return;
        }

        switch (selected)
        {
            case SelectedDefinition.Doctrine:
                if (currentDoctrine != null)
                {
                    playerTech.SetSelectedDoctrineServerRpc(
                        currentDoctrine.DoctrineType
                    );
                }
                break;

            case SelectedDefinition.Research:
                if (currentResearch != null)
                {
                    playerTech.SelectResearchServerRpc(
                        currentResearch.ResearchId
                    );
                }
                break;
        }
    }

    public void DeclineClicked()
    {
        if (selected == SelectedDefinition.Upgrade &&
            currentResearch != null)
        {
            FillInspector(currentResearch);
            return;
        }

        ClearInspector();
    }

    public void ClearInspector()
    {
        selectionLocked = false;
        currentDoctrine = null;
        currentResearch = null;
        currentUpgrade = null;
        selected = SelectedDefinition.None;

        SetText(Name, "");
        SetText(Description, "");
        SetText(ExtraInfo, "");

        SetImage(null);
        SetText(doctrineTypeText, "");
        SetRequiredResearch(null);
        SetRequiredResearchCompleted(null);
        SetImage(requiredResearchImage, null);
        SetImage(statusImage, null);
        SetText(statusText, "");

        SetResearchInfo(false);
        SetActive(ExtraInfoRoot, false);

        SetButton(acceptButton, false);
        SetButton(declineButton, false);
    }

    private void SetCommon(
        string displayName,
        string description,
        Sprite icon)
    {
        SetText(Name, displayName);
        SetText(Description, description);
        SetImage(icon);
    }

    private void SetResearchInfo(bool visible)
    {
        SetActive(CoriumCostRoot, visible);
        SetActive(EnergyPerSecondRoot, visible);
        SetActive(TimeSecondsRoot, visible);

        if (!visible)
        {
            SetText(CoriumCost, "");
            SetText(EnergyPerSecond, "");
            SetText(TimeSeconds, "");
        }
    }

    private void SetImage(Sprite sprite)
    {
        if (iconImage == null)
            return;

        iconImage.sprite = sprite;
        iconImage.enabled = sprite != null;
    }

    private void SetRequiredResearch(ResearchDefinition research)
    {
        SetImage(requiredResearchImage, research != null ? research.Icon : null);
        SetText(requiredResearchText, research != null ? research.DisplayName : "");
    }

    private void SetRequiredResearchCompleted(ResearchDefinition research)
    {
        if (research == null)
        {
            SetImage(requiredResearchCompletedImage, null);
            return;
        }

        bool completed = playerTech != null && playerTech.IsResearchUnlocked(research.ResearchId);
        SetImage(requiredResearchCompletedImage, completed ? checkmarkGreen : crossRed);
    }

    private void RefreshResearchStatus(ResearchDefinition research)
    {
        if (research == null || playerTech == null)
        {
            SetResearchStatus("Nevyzkoumáno", crossRed);
            return;
        }

        if (playerTech.IsResearchUnlocked(research.ResearchId))
        {
            SetResearchStatus("Vyzkoumáno", checkmarkGreen);
            return;
        }

        if (playerTech.CurrentSelectedResearchId == research.ResearchId)
        {
            SetResearchStatus($"Probíhá výzkum: {playerTech.CurrentResearchProgressPercent:0}%", researchInProgressSprite);
            return;
        }

        PlayerResources resources = playerTech.GetComponent<PlayerResources>();
        if (resources != null && resources.Corium.Value < research.CoriumCost)
        {
            SetResearchStatus("Nedostatečný zdroje", crossRed);
            return;
        }

        SetResearchStatus("Nevyzkoumáno", crossRed);
    }

    private void SetResearchStatus(string value, Sprite sprite)
    {
        SetText(statusText, value);
        SetImage(statusImage, sprite);
    }

    private void SetImage(Image target, Sprite sprite)
    {
        if (target == null)
            return;

        target.sprite = sprite;
        target.enabled = sprite != null;
        target.gameObject.SetActive(sprite != null);
    }

    private void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }

    private void SetActive(GameObject target, bool active)
    {
        if (target != null)
            target.SetActive(active);
    }

    private void SetButton(Button button, bool visible)
    {
        if (button != null)
            button.gameObject.SetActive(visible);
    }

    private string GetStatDisplayName(UpgradeStatType statType)
    {
        switch (statType)
        {
            case UpgradeStatType.MaxHealth:
                return "Maximální zdraví";

            case UpgradeStatType.Damage:
                return "Poškození";

            case UpgradeStatType.AttackRange:
                return "Dosah útoku";

            case UpgradeStatType.AttackSpeed:
                return "Rychlost útoku";

            case UpgradeStatType.MovementSpeed:
                return "Rychlost pohybu";

            case UpgradeStatType.SightRange:
                return "Dosah vidění";

            case UpgradeStatType.BuildSpeed:
                return "Rychlost stavby";

            case UpgradeStatType.RepairPower:
                return "Síla oprav";

            case UpgradeStatType.MiningSpeed:
                return "Rychlost těžby";

            case UpgradeStatType.CargoCapacity:
                return "Kapacita nákladu";

            case UpgradeStatType.AgentAcceleration:
                return "Zrychlení jednotky";

            case UpgradeStatType.TurnSpeed:
                return "Rychlost otáčení";

            default:
                return "Neznámý atribut";
        }
    }
}
