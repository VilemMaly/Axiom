using TMPro;
using UnityEngine;

/// <summary>
/// Zobrazení informací o nepřátelské jednotce.
/// </summary>
public class EnemyTroopStatus : MonoBehaviour
{
    [Header("Texty")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI troopTypeText;

    [Header("Formátování")]
    [SerializeField] private string playerNameFormat = "Hráč: {0}";
    [SerializeField] private string healthFormat = "Životy: {0} / {1}";
    [SerializeField] private string troopTypeFormat = "Jednotka: {0}";

    private Troop selectedTroop;

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

    public void SetTroopType(string troopType)
    {
        if (troopTypeText != null)
            troopTypeText.text = string.Format(troopTypeFormat, troopType);
    }

    public void UpdateStatus(Troop troop, string playerName)
    {
        if (troop == null)
            return;

        selectedTroop = troop;

        SetPlayerName(playerName);
        SetHealth(troop.Health.Value, troop.MaxHealth);
        SetTroopType(troop.Type.ToString());
    }
}