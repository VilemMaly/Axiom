using System.Runtime.CompilerServices;
using UnityEngine;

public class EnergyPlant : Building
{
    public override BuildingType Type => BuildingType.EnergyPlant;


    // TODO: pokud má miner fungovat jen na konkrétním resource nodu na mapě,
    // přidej referenci na ResourceNode a v OnBuilt() ověř, jestli je pod/vedle budovy.
    // Pokud ne, nastav isActive = false a budova netěží (+ vizuální feedback neplatné pozice).

    public override void OnBuilt()
    {
    }

    public override void UpdateBuilding()
    {
        if (!IsOperational || !IsServer || OwnerResources == null) return;

    }
}