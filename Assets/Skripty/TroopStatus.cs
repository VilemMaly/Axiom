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

        // Atlas-specific hodnoty - jednoduše zkusíme sáhnout na Atlase, pokud tam je
        Atlas atlas = troop as Atlas;
        if (atlas != null)
        {
            if (buildPowerText != null)
                buildPowerText.text = atlas.BuildCoriumPerSecond.ToString("0.0");
            if (repairPowerText != null)
                repairPowerText.text = atlas.RepairPower.ToString("0.0");
        }
    }
}