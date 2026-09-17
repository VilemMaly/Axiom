using TMPro;
using UnityEngine;

/// <summary>
/// Zobrazovací skript pro jeden konkrétní UI panel budovy.
/// Necháváš ho viset na panelu (corePanel, laserTowerPanel, ...), takže si
/// v Inspectoru pro každý panel přiřadíš vlastní textová pole na vlastním
/// místě v canvasu - žádná logika navíc, jen "kam se má co vypsat".
/// </summary>
public class BuildingStatus : MonoBehaviour
{
    [Header("Texty pro zobrazení hodnot (přiřaď v Inspectoru pro tento konkrétní panel)")]
    [SerializeField] private TextMeshProUGUI buildingNameText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI coriumConsumptionText;
    [SerializeField] private TextMeshProUGUI energyConsumptionText;
    [SerializeField] private TextMeshProUGUI coriumProductionText;
    [SerializeField] private TextMeshProUGUI energyProductionText;

    [Header("Formátování textu")]
    [SerializeField] private string healthFormat = "Životy: {0} / {1}";
    [SerializeField] private string coriumConsumptionFormat = "Spotřeba Coria: {0}";
    [SerializeField] private string energyConsumptionFormat = "Spotřeba energie: {0}";
    [SerializeField] private string coriumProductionFormat = "Výroba Coria: {0}";
    [SerializeField] private string energyProductionFormat = "Výroba energie: {0}";

    private Building selectedBuilding;

    // --- Jednotlivé settery - každý řeší jen jedno textové pole ---
    public void SetBuildingName(string name)
    {
        if (buildingNameText != null)
            buildingNameText.text = name;
    }

    public void SetHealth(int current, float max)
    {
        if (healthText != null)
            healthText.text = string.Format(healthFormat, current, max);
    }

    public void SetCoriumConsumption(float value)
    {
        if (coriumConsumptionText != null)
            coriumConsumptionText.text = string.Format(coriumConsumptionFormat, value);
    }

    public void SetEnergyConsumption(float value)
    {
        if (energyConsumptionText != null)
            energyConsumptionText.text = string.Format(energyConsumptionFormat, value);
    }

    public void SetCoriumProduction(float value)
    {
        if (coriumProductionText != null)
            coriumProductionText.text = string.Format(coriumProductionFormat, value);
    }

    public void SetEnergyProduction(float value)
    {
        if (energyProductionText != null)
            energyProductionText.text = string.Format(energyProductionFormat, value);
    }

    /// <summary>
    /// Hromadná aktualizace všech hodnot najednou z konkrétní instance Building.
    /// POZOR: názvy členů (CurrentHealth, MaxHealth, CoriumConsumption, ...) jsou
    /// placeholder podle běžné konvence - uprav je podle skutečných veřejných
    /// proměnných/vlastností ve tvém Building.cs, ať se to zkompiluje.
    /// </summary>
    public void UpdateStatus(Building building)
    {
        if (building == null)
            return;
        selectedBuilding = building;
        SetBuildingName(building.Type.ToString()); // nebo building.name, pokud chceš skutečný název objektu
        SetHealth(building.Health.Value, building.MaxHealth);
        SetCoriumConsumption(building.CoriumConsumption);
        SetEnergyConsumption(building.EnergyConsumption);
        SetCoriumProduction(building.CoriumProduction);
        SetEnergyProduction(building.EnergyProduction);
    }
}