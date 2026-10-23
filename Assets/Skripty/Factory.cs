using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class Factory : Building
{
    public override BuildingType Type => BuildingType.Factory;

    [Header("Factory specific")]
    [SerializeField] public GameObject model1;
    [SerializeField] public GameObject model2;
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
            case 2:
                {
                    model1.SetActive(false);
                    model2.SetActive(true);
                    Level = tier;
                    return;
                }
        }
    }
}