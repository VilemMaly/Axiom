using TMPro;
using UnityEngine;

public class TroopStatus : MonoBehaviour
{
    [Header("Texty - nastav podle konkrétního panelu")]
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI harvestPowerText;
    [SerializeField] private TextMeshProUGUI repairPowerText;

    public void UpdateStatus(Troop troop)
    {
        if (healthText != null)
            healthText.text = $"{troop.Health.Value} / {troop.MaxHealth}";

        // Těžební hodnoty nové jednotky Cargo.
        Cargo cargo = troop as Cargo;
        if (cargo != null)
        {
            if (harvestPowerText != null)
                harvestPowerText.text = cargo.HarvestCoriumPerSecond.ToString("0.0");
            if (repairPowerText != null)
                repairPowerText.text = cargo.RepairCoriumPerSecond.ToString("0.0");
        }
    }
}
