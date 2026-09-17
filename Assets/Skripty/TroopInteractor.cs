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

    private Atlas selectedAtlas;

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
        if(!IsServer)
            return false;
        
        int TroopCount = 0;
        foreach (Troop spawnedTroops in myTroops)
        {
            if (troopToSpawn.Type == spawnedTroops.Type)
            {
                TroopCount++;
            }
        }

        if(TroopCount >= troopToSpawn.MaxTroopAmount)
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
    /// ještě žádného Atlase nemá) - u všech ostatních budov pošleme příkaz "jeď a
    /// postav" všem vlastním Atlasům; ten, kdo tam dorazí první, sám pošle serveru
    /// žádost přes Build.RequestBuildServerRpc (viz Atlas.RequestMoveToBuild).
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
            Debug.Log("[TroopInteractor] Core se staví rovnou, bez potřeby Atlasu.");
            build.RequestBuildServerRpc(buildingIndex, position, rotation, default);
            return;
        }

        bool anyAtlasFound = false;

        foreach (Troop troop in myTroops)
        {
            if (troop is Atlas atlas)
            {
                BuildIndex = buildingIndex;
                BuildPosition = position;
                BuildRotation = rotation;
                selectedAtlas = troop as Atlas;
                anyAtlasFound = true;
                atlas.NearBuilding += BuildSubscribeEvent;
                atlas.RequestMoveToBuild(position, buildingIndex, rotation);
            }
        }

        if (!anyAtlasFound)
            Debug.Log("[TroopInteractor] Žádný vlastní Atlas k dispozici, stavbu nemá kdo provést.");
    }

    private void BuildSubscribeEvent(Atlas atlas, int index, Vector3 position, int rotation, int BuildingLeft)
    {
        build.RequestBuildServerRpc(index, position, rotation, selectedAtlas.NetworkObject);
        if (BuildingLeft <= 0)
            atlas.NearBuilding -= BuildSubscribeEvent;
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
        Harvester troop = selector.SelectedTroop as Harvester;
        if (troop == null)
        {
            Debug.Log("No troop selected for interaction.");
            return;
        }
    

        troop.changeAutoState();
    }

}