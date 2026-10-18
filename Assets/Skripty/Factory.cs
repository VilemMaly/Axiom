using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class Factory : Building
{
    public override BuildingType Type => BuildingType.Factory;

    [Header("Factory specific")]
    [SerializeField] public Transform spawnPoint;
    [SerializeField] public Transform rallyPoint;
    private BuildingSpawner spawner;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        spawner = GetComponent<BuildingSpawner>();
    }
    public override void UpdateBuilding()
    {

    }
}