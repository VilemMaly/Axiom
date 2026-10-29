using TMPro;
using UnityEngine;

public class TroopStatus : MonoBehaviour
{
    [Header("Texty - nastav podle konkrétního panelu")]
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI buildPowerText;
    [SerializeField] private TextMeshProUGUI repairPowerText;

    public void UpdateStatus(Troop troop)
    {
        if (healthText != null)
            healthText.text = $"{troop.Health.Value} / {troop.MaxHealth}";

        // Stavěcí hodnoty nové jednotky Cargo.
        Cargo cargo = troop as Cargo;
        if (cargo != null)
        {
            if (buildPowerText != null)
                buildPowerText.text = cargo.BuildCoriumPerSecond.ToString("0.0");
            if (repairPowerText != null)
                repairPowerText.text = cargo.RepairCoriumPerSecond.ToString("0.0");
        }
    }
}
