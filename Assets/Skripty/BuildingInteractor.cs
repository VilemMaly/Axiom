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
    private CableManager cableManager;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    public override void OnNetworkSpawn()
    {
        cableManager = GetComponent<CableManager>();
    }

    // Update is called once per frame
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

}