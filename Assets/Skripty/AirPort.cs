using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class AirPort : Building
{
    public override BuildingType Type => BuildingType.AirPort;

    [Header("Factory specific")]
    private BuildingSpawner spawner;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        spawner = GetComponent<BuildingSpawner>();
    }
    public override void UpdateBuilding()
    {

    }

    public override void UpgradeBuilding(int tier)
    {
        switch(tier)
        {
        }
    }
}