using System;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class BuildingSpawner : NetworkBehaviour
{
    public int coriumPerSecond;
    public StatusBar troopLoading;
    public event Action<ulong> loadingFinished;
    private float time;
    private bool isConstructingTroop;
    private int troopCost;
    private int troopCostMax;
    private float percentageBuilt = 100;

    private PlayerResources resources;
    private Building currentBuilding;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }
    public override void OnNetworkSpawn()
    {
        resources = NetworkManager.Singleton.ConnectedClients[OwnerClientId].PlayerObject.GetComponent<PlayerResources>();
        currentBuilding = GetComponent<Building>();
    }

    public override void OnNetworkDespawn()
    {
        loadingFinished = null;
    }

    // Update is called once per frame
    void Update()
    {
        if(!IsServer || !isConstructingTroop)
            return;
        time += Time.deltaTime;
        if(time >= 1 && isConstructingTroop)
        {
            time -= 1;
            troopCost -= coriumPerSecond;
            resources.Corium.Value -= coriumPerSecond;
            percentageBuilt = troopCost / troopCostMax;
            troopLoading.SetValue(troopCost,troopCostMax);
            updateStatusClientRpc(troopCost,troopCostMax);
            if(isConstructingTroop && troopCost <= 0)
            {
                loadingFinished?.Invoke(OwnerClientId);
                loadingFinished = null;
                isConstructingTroop = false;
                if(currentBuilding.Type == Building.BuildingType.Core)
                {
                    Core core = currentBuilding as Core;
                    core.OpenAndCloseClientRpc();
                }
            }
        }
        
    }

    [ClientRpc]
    private void updateStatusClientRpc(int cost, int costmax)
    {
        troopLoading.SetValue(cost,costmax);
    }

    public void startBuildingTroop(Troop troop)
    {
        if(!IsServer)
            return;
        if(resources.Corium.Value < troop.Cost)
            return;
        troopCost = troop.Cost;
        troopCostMax = troopCost;
        isConstructingTroop = true;
    }
}
