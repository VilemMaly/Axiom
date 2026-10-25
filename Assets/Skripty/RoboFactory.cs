using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class RoboFactory : Building
{
    public override BuildingType Type => BuildingType.RoboFactory;

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