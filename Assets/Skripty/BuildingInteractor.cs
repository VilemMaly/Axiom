using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BuildingInteractor : NetworkBehaviour
{

    // Seznam budov hráče. Naplňuje se automaticky - každá Building se sem
    // sama zaregistruje ve svém OnNetworkSpawn/OnNetworkDespawn (viz Building.cs),
    // pokud je její vlastníkem tenhle klient.
    private readonly List<Building> myBuildings = new List<Building>();
    public IReadOnlyList<Building> MyBuildings => myBuildings;

    /// <summary>Returns the first eligible idle spawner, or the first eligible spawner if all are busy.</summary>
    public BuildingSpawner GetSpawner(TroopType troopType)
    {
        BuildingSpawner firstBusySpawner = null;

        foreach (Building building in myBuildings)
        {
            if (building == null || !building.IsConstructionComplete.Value || !building.IsOperational)
                continue;

            BuildingSpawner spawner = building.GetComponent<BuildingSpawner>();
            if (spawner == null || !spawner.CanSpawn(troopType))
                continue;

            if (!spawner.HasQueuedTroops)
                return spawner;

            if (firstBusySpawner == null)
                firstBusySpawner = spawner;
        }

        return firstBusySpawner;
    }
    private CableManager cableManager;
    public event Action<ulong> lostGame; // zavolá se z core do game spawner a ukončí hru pokud je core zničen nebo nefunkční

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    public override void OnNetworkSpawn()
    {
        cableManager = GetComponent<CableManager>();
    }

    private float timer = 0f;

void Update()
{

}

    // ============================================================
    //  Seznam vlastních budov
    // ============================================================
    private void ResetEnergySources()
    {
        if (!IsServer)
            return;
        // visuálně se u obou klientů resetuje připojení kabelů
        cableManager.ClearAllCablesClientRpc();
        // všechny budovy se nejdříve označí jako nepřipojené k síti budov, aby se mohlo znovu hledat připojení.
        foreach (Building building in MyBuildings)
        {
            building.IsConnected.Value = false;
        }
        // vydá signál, ze všech budov, které jsou zdrojem energie, aby znovu zkontrolovaly, jestli mají připojené budovy kolem sebe,
        // a pokud ano, tak ty připojené budovy které nejsou zdrojem energie hledají další nepřipojené budovy a připojí je k sobě, dokud se nenajdou všechny budovy, které jsou v dosahu všech budov v síti. 
        // Tím se zajistí, že všechny budovy, které jsou v dosahu zdrojů energie, budou mít připojené zdroje energie a budou moci fungovat.
        // Případně se může stát, že některé budovy budou odpojené od zdrojů energie, a přestane fungovat, dokud se znovu nepřipojí k síti budov.
        foreach (Building building in myBuildings)
        {
            if (building.IsPowerSource)
            {
                building.searchForEnergyConnections();
            }
        }
    }
    public bool RegisterBuilding(Building building)
    {
        if (building == null || myBuildings.Contains(building))
            return false;

        myBuildings.Add(building);
        if(building.Type == Building.BuildingType.Core)
        {
            Core core = building as Core;
            core.coreNotFunctional += clientId => lostGame?.Invoke(clientId);
        }
        ResetEnergySources();
        Debug.Log($"[BuildingInteractor] Zaregistrována budova {building.name} (typ {building.Type}). Celkem: {myBuildings.Count} Stav Sítě: {(building.IsConnected.Value ? "Připojeno" : "Nepřipojeno")}");
        return CheckBuildingRules(building, OwnerClientId);
    }
    public bool CheckBuildingRules(Building building, ulong clientId)
    {
        // tohle bude kontrolovat jen server.
        if (!IsServer)
            return false;
        // pokud není platná budova, tak se stavba nepovede
        if (building == null)
            return false;
        // nyní server počítá kolik má klient postavených budov daného typu a porovnává s maximálním počtem, který je povolený pro daný typ budovy.
        int buildingCount = 0;
        foreach (Building b in myBuildings)
        {
            if (b.Type == building.Type)
            {
                buildingCount++;
            }
        }
        // pokud je počet budov daného typu větší nebo roven maximálnímu počtu, tak se stavba nepovede
        if (buildingCount > building.MaxBuildingAmount)
        {
            Debug.Log($"[BuildingInteractor] Nelze postavit budovu {building.name} (typ {building.Type}) - dosaženo max. počtu {building.MaxBuildingAmount}.");
        return false;
        }

        if(!GetComponent<PlayerTechnology>().CheckBuildingResearched(building))
        {
            Debug.Log("Hráč tuto budovu nemůže postavit, protože ji nemá odemčenou ve výzkumu.");
            return false;
        }
        return true;
    }

    public bool UnregisterBuilding(Building building)
    {
        if (building == null)
            return false;

        if (myBuildings.Remove(building))
            Debug.Log($"[BuildingInteractor] Odregistrována budova {building.name} (typ {building.Type}). Celkem: {myBuildings.Count}");
        ResetEnergySources();
        return true;
    }

    /// <summary>Ask the server to apply the building upgrade granted by completed research.</summary>
    public void RequestResearchBuildingUpgrade(
        int researchId,
        string upgradeId,
        Building.BuildingType buildingType,
        int tier)
    {
        if (!IsSpawned || !IsOwner || tier < 1)
            return;

        ApplyResearchBuildingUpgradeServerRpc(researchId, upgradeId, buildingType, tier);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ApplyResearchBuildingUpgradeServerRpc(
        int researchId,
        string upgradeId,
        Building.BuildingType buildingType,
        int tier,
        ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || string.IsNullOrEmpty(upgradeId) || tier < 1)
            return;

        PlayerTechnology technology = GetComponent<PlayerTechnology>();
        if (technology == null || !technology.TryGetUnlockedBuildingUpgrade(researchId, upgradeId, buildingType, tier))
            return;

        bool upgraded = false;
        foreach (Building building in myBuildings)
        {
            if (building != null && building.IsSpawned && building.OwnerClientId == OwnerClientId &&
                building.Type == buildingType && building.IsConstructionComplete.Value)
            {
                building.UpgradeBuilding(tier);
                upgraded = true;
            }
        }

        if (!upgraded)
            Debug.LogWarning($"[BuildingInteractor] Nebyla nalezena dokončená budova typu {buildingType} pro upgrade na tier {tier}.", this);
    }

}
