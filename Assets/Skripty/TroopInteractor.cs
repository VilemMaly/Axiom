using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TroopInteractor : NetworkBehaviour
{
    private Selector selector;

    [Tooltip("Komponenta Build - stejný GameObject jako Selector/TroopSpawner (Player NetworkObject).")]
    private Build build;

    // Seznam vlastních troopů hráče. Naplňuje se automaticky - každý Troop se sem
    // sám zaregistruje ve svém OnNetworkSpawn/OnNetworkDespawn (viz Troop.cs),
    // pokud je jeho vlastníkem tenhle klient.
    private readonly List<Troop> myTroops = new List<Troop>();
    public IReadOnlyList<Troop> MyTroops => myTroops;

    private int BuildIndex;
    private Vector3 BuildPosition;
    private int BuildRotation;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        selector = GetComponent<Selector>();
        build = GetComponent<Build>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // ============================================================
    //  Seznam vlastních troopů
    // ============================================================

    public void RegisterTroop(Troop troop)
    {
        if (troop == null || myTroops.Contains(troop))
            return;

        myTroops.Add(troop);
        Debug.Log($"[TroopInteractor] Zaregistrována jednotka {troop.name} (typ {troop.Type}). Celkem: {myTroops.Count}");
    }

    public bool isAllowedToSpawn(Troop troopToSpawn)
    {
        if (!IsServer || troopToSpawn == null)
            return false;
        
        int troopCount = 0;
        foreach (Troop spawnedTroops in myTroops)
        {
            if (spawnedTroops != null && troopToSpawn.Type == spawnedTroops.Type)
            {
                troopCount++;
            }
        }

        BuildingInteractor buildingInteractor = GetComponent<BuildingInteractor>();
        if (buildingInteractor != null)
        {
            foreach (Building building in buildingInteractor.MyBuildings)
            {
                if (building == null)
                    continue;

                BuildingSpawner buildingSpawner = building.GetComponent<BuildingSpawner>();
                if (buildingSpawner != null)
                    troopCount += buildingSpawner.CountQueuedTroopsOfType(troopToSpawn.Type);
            }
        }

        if (troopCount >= troopToSpawn.MaxTroopAmount)
            return false;
        
        return true;
    }

    public void UnregisterTroop(Troop troop)
    {
        if (troop == null)
            return;

        if (myTroops.Remove(troop))
            Debug.Log($"[TroopInteractor] Odregistrována jednotka {troop.name} (typ {troop.Type}). Celkem: {myTroops.Count}");
    }

    // ============================================================
    //  Stavba budov
    // ============================================================

    /// <summary>
    /// Zavolej s pozicí, kam hráč chce postavit budovu, a indexem budovy podle listu
    /// v Build.cs. Core se dá postavit rovnou (bootstrap na začátku hry, kdy hráč
    /// ještě žádného Cargo nemá) - u všech ostatních budov pošleme příkaz "jeď a
    /// postav" všem vlastním Cargo jednotkám; ta, která dorazí první, požádá server
    /// o vytvoření budovy.
    /// </summary>
    public void Build(Vector3 position, int buildingIndex, Building.BuildingType buildingType, int rotation)
    {
        if (build == null)
        {
            Debug.LogWarning("[TroopInteractor] Chybí komponenta Build na tomto GameObjectu.");
            return;
        }

        if (buildingType == Building.BuildingType.Core)
        {
            Debug.Log("[TroopInteractor] Core se staví rovnou, bez potřeby Cargo.");
            build.RequestBuildServerRpc(buildingIndex, position, rotation, default);
            return;
        }

        bool anyCargoFound = false;

        foreach (Troop troop in myTroops)
        {
            if (troop is Cargo cargo)
            {
                BuildIndex = buildingIndex;
                BuildPosition = position;
                BuildRotation = rotation;
                Debug.Log($"[TroopInteractor] Posílám příkaz Cargu {troop.name} (typ {troop.Type}) k postavení budovy {buildingType.ToString()} na pozici {position} s rotací {rotation}.");
                anyCargoFound = true;
                cargo.NearBuilding += BuildSubscribeEvent;
                cargo.RequestMoveToBuild(position, buildingIndex, rotation);
            }
        }

        if (!anyCargoFound)
            Debug.Log("[TroopInteractor] Žádné vlastní Cargo k dispozici, stavbu nemá kdo provést.");
    }

    private void BuildSubscribeEvent(Cargo cargo, int index, Vector3 position, int rotation, int BuildingLeft)
    {
        build.RequestBuildServerRpc(index, position, rotation, cargo.NetworkObject);
        if (BuildingLeft <= 0)
        {
            cargo.NearBuilding -= BuildSubscribeEvent;
            Debug.Log($"[TroopInteractor] Cargo dokončilo všechny příkazy ke stavbě.");
        }
    }

    /// <summary>
    /// Pomocná varianta pro UI: počká na klik na zem (přes Selector) a teprve pak
    /// zavolá Build() s vybranou pozicí. Zavolej z tlačítka výběru budovy.
    /// </summary>
    public void ChooseBuildLocation(int buildingIndex, Building.BuildingType buildingType, int rotation = 0)
    {
        StartCoroutine(selector.IWantAGroundPoint(point =>
        {
            if (point == null)
            {
                Debug.Log("[TroopInteractor] Výběr místa stavby zrušen - klik netrefil zem.");
                return;
            }

            Build(point.Value, buildingIndex, buildingType, rotation);
        }));
    }

    public void ChooseBuilding()
    {
        Troop troop = selector.SelectedTroop;
        if (troop == null)
        {
            Debug.Log("No troop selected for interaction.");
            return;
        }

        // Zde můžete přidat logiku pro interakci s jednotkou, například otevření panelu s informacemi o jednotce
        Debug.Log($"Interacting with troop: {troop.name}");
        StartCoroutine(selector.IWantABuilding(building => troop.OnSelectBuilding(building)));
    }

    public void ChooseTroop()
    {
        Troop troop = selector.SelectedTroop;
        if (troop == null)
        {
            Debug.Log("No troop selected for interaction.");
            return;
        }

        // Zde můžete přidat logiku pro interakci s jednotkou, například otevření panelu s informacemi o jednotce
        Debug.Log($"Interacting with troop: {troop.name}");
        StartCoroutine(selector.IWantATroop(selectedTroop => troop.OnSelectTroop(selectedTroop)));
    }

    public void ChooseResource()
    {
        Troop troop = selector.SelectedTroop;
        if (troop == null)
        {
            Debug.Log("No troop selected for interaction.");
            return;
        }

        // Zde můžete přidat logiku pro interakci s jednotkou, například otevření panelu s informacemi o jednotce
        Debug.Log($"Interacting with troop: {troop.name}");
        Debug.Log($"[TroopInteractor-DIAG] ChooseResource voláno pro troop={troop.name} (IsOwner={troop.IsOwner}, OwnerClientId={troop.OwnerClientId})");
        StartCoroutine(selector.IWantAResource(resource =>
        {
            Debug.Log($"[TroopInteractor-DIAG] IWantAResource callback: resource={(resource != null ? resource.name : "null")} -> voláno troop.OnSelectResource");
            troop.OnSelectResource(resource);
        }));
    }

    public void ChangeAutoState()
    {
        Cargo troop = selector.SelectedTroop as Cargo;
        if (troop == null)
        {
            Debug.Log("No troop selected for interaction.");
            return;
        }
    

        troop.changeAutoState();
    }

}
