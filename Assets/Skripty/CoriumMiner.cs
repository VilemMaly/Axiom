using UnityEngine;

public class CoriumMiner : Building
{
    public override BuildingType Type => BuildingType.CoriumMiner;

    [Header("Corium Miner specific")]
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
            OwnerResources.Add(CoriumProduction, 0);
        }
    }
}