using UnityEngine;

public class EnergyPlant : Building
{
    public override BuildingType Type => BuildingType.EnergyPlant;

    [Header("Energy Plant specific")]
    [SerializeField] private float tickInterval = 1f;

    private float timer;

    // TODO: pokud má miner fungovat jen na konkrétním resource nodu na mapě,
    // přidej referenci na ResourceNode a v OnBuilt() ověř, jestli je pod/vedle budovy.
    // Pokud ne, nastav isActive = false a budova netěží (+ vizuální feedback neplatné pozice).

    public override void OnBuilt()
    {
        timer = 0f;
    }

    public override void UpdateBuilding()
    {
        if (!IsOperational || !IsServer || OwnerResources == null) return;

        timer += Time.deltaTime;
        if (timer >= tickInterval)
        {
            timer -= tickInterval;
            OwnerResources.Add(0, EnergyProduction);
        }
    }
}