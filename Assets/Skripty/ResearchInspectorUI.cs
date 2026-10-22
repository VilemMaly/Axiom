
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResearchInspectorUI : MonoBehaviour
{
    [Header("Common")]
    [SerializeField] private TMP_Text Name;
    [SerializeField] private TMP_Text Description;
    [SerializeField] private Image iconImage;

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
            doctrine.DisplayName,
            doctrine.Description,
            doctrine.Icon
        );

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
            popup.Opened -= FillInspector;

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

        SetResearchInfo(true);

        SetText(CoriumCost, research.CoriumCost.ToString());
        SetText(EnergyPerSecond, $"{research.EnergyPerSecond:0.##}/s");
        SetText(TimeSeconds, $"{research.ResearchTime:0.##} s");

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
            skript.Opened += FillInspector;
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

        SetResearchInfo(false);

        SetActive(ExtraInfoRoot, true);

        string target = upgrade.TargetType == UpgradeTargetType.Troop
            ? $"Jednotka: {upgrade.TroopType}"
            : $"Budova: {upgrade.BuildingType}";

        string value = upgrade.ValueType == UpgradeValueType.Percentage
            ? $"{upgrade.Value:+0.##;-0.##;0}%"
            : $"{upgrade.Value:+0.##;-0.##;0}";

        string stat = GetStatDisplayName(upgrade.StatType);

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
        currentDoctrine = null;
        currentResearch = null;
        currentUpgrade = null;
        selected = SelectedDefinition.None;

        SetText(Name, "");
        SetText(Description, "");
        SetText(ExtraInfo, "");

        SetImage(null);

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
