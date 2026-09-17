using TMPro;
using UnityEngine;

/// <summary>
/// Zobrazení informací o nepřátelské budově.
/// </summary>
public class EnemyBuildingStatus : MonoBehaviour
{
    [Header("Texty")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI buildingTypeText;

    [Header("Formátování")]
    [SerializeField] private string playerNameFormat = "Hráč: {0}";
    [SerializeField] private string healthFormat = "Životy: {0} / {1}";
    [SerializeField] private string buildingTypeFormat = "Budova: {0}";

    private Building selectedBuilding;

    public void SetPlayerName(string playerName)
    {
        if (playerNameText != null)
            playerNameText.text = string.Format(playerNameFormat, playerName);
    }

    public void SetHealth(float current, float max)
    {
        if (healthText != null)
            healthText.text = string.Format(healthFormat, current, max);
    }

    public void SetBuildingType(string buildingType)
    {
        if (buildingTypeText != null)
            buildingTypeText.text = string.Format(buildingTypeFormat, buildingType);
    }

    /// <summary>
    /// Aktualizuje celý panel.
    /// </summary>
    public void UpdateStatus(Building building, string playerName)
    {
        if (building == null)
            return;

        selectedBuilding = building;

        SetPlayerName(playerName);
        SetHealth(building.Health.Value, building.MaxHealth);
        SetBuildingType(building.Type.ToString());
    }
}