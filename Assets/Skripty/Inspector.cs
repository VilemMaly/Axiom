using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class Inspector : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private Image iconImage;

    [Header("Dynamické statistiky")]
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private TMP_Text coriumText;
    [SerializeField] private TMP_Text energyText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private GameObject InspectorPanel;

    [Header("Barvy statistik")]
    [SerializeField] private Color red = Color.red;
    [SerializeField] private Color neutral = Color.white;
    [SerializeField] private Color green = Color.green;

    [Header("Dostupná data")]
    [SerializeField] private List<InspectorData> inspectorData = new List<InspectorData>();

    private Troop selectedTroop;
    private Building selectedBuilding;

    private void Update()
    {
        if (selectedTroop != null || selectedBuilding != null)
            UpdateStats();
    }

    public bool ShowInspector(Troop troop)
    {
        if (troop == null)
        {
            ClearInspector();
            return false;
        }

        selectedTroop = troop;
        selectedBuilding = null;
        SetText(coriumText, string.Empty, neutral);
        SetText(energyText, string.Empty, neutral);
        InspectorPanel.SetActive(true);
        InspectorData data = inspectorData.Find(item =>
            item != null && item.Troop && item.TroopType == troop.Type);
        return ShowInspectorData(data);
    }

    public bool ShowInspector(Building building)
    {
        if (building == null)
        {
            ClearInspector();
            return false;
        }
        InspectorPanel.SetActive(true);
        selectedBuilding = building;
        selectedTroop = null;

        InspectorData data = inspectorData.Find(item =>
            item != null && item.Budova && item.BuildingType == building.Type);
        int coriumNet = building.CoriumProduction - building.CoriumConsumption;
        int energyNet = building.EnergyProduction - building.EnergyConsumption;
        SetText(coriumText, $"{coriumNet:+#;-#;0} corium/sec", coriumNet > 0 ? green : coriumNet < 0 ? red : neutral);
        SetText(energyText, $"{energyNet:+#;-#;0} energy/sec", energyNet > 0 ? green : energyNet < 0 ? red : neutral);
        return ShowInspectorData(data);
    }

    public bool ShowInspectorData(InspectorData data)
    {
        if (data == null)
        {
            ClearInspector();
            return false;
        }

        if (nameText != null)
            nameText.text = data.DisplayName;

        if (descriptionText != null)
            descriptionText.text = data.Description;

        if (iconImage != null)
        {
            iconImage.sprite = data.Icon;
            iconImage.enabled = data.Icon != null;
        }

        UpdateStats();

        return true;
    }

    public void ClearInspector()
    {
        selectedTroop = null;
        selectedBuilding = null;
        if (nameText != null)
            nameText.text = string.Empty;

        if (descriptionText != null)
            descriptionText.text = string.Empty;

        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }

        SetText(healthText, string.Empty, neutral);
        SetText(coriumText, string.Empty, neutral);
        SetText(energyText, string.Empty, neutral);
        SetText(costText, string.Empty, neutral);
        InspectorPanel.SetActive(false);
    }

    private void UpdateStats()
    {
        int health = 0;
        int maxHealth = 0;
        int cost = 0;
        if (selectedTroop != null)
        {
            health = selectedTroop.Health.Value;
            maxHealth = selectedTroop.MaxHealth;
            cost = selectedTroop.Cost;
        }
        else if (selectedBuilding != null)
        {
            health = selectedBuilding.Health.Value;
            maxHealth = selectedBuilding.MaxHealth;
            cost = selectedBuilding.Cost;
        }
        else
        {
            return;
        }

        SetText(healthText, $"{health} / {maxHealth}", health >= maxHealth && maxHealth > 0 ? green : red);
        SetText(costText, cost.ToString(), neutral);

    }

    private static void SetText(TMP_Text text, string value, Color color)
    {
        if (text == null)
            return;

        text.text = value;
        text.color = color;
    }
}
