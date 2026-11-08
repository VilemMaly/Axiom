using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TroopInteractor : NetworkBehaviour
{
    private Selector selector;

    [Tooltip("Komponenta Build - stejný GameObject jako Selector/TroopSpawner (Player NetworkObject).")]
    private Build build;

    // Skupiny vlastních jednotek tohoto hráče; jednotky se zařazují při network spawnu.
    private readonly List<TroopGroup> myGroups = new List<TroopGroup>();
    private readonly Dictionary<TroopGroup, List<TroopType>> replacementRequests = new Dictionary<TroopGroup, List<TroopType>>();
    private int nextGroupId;
    public IReadOnlyList<TroopGroup> MyGroups => myGroups;

    private int BuildIndex;
    private Vector3 BuildPosition;
    private int BuildRotation;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        selector = GetComponent<Selector>();
        build = GetComponent<Build>();
    }

    public TroopGroup CreateGroup()
    {
        if (!IsOwner && !IsServer) return null;
        TroopGroup group = CreateGroupLocal(nextGroupId++);
        if (IsOwner && !IsServer) CreateGroupServerRpc(group.GroupId);
        return group;
    }

    private TroopGroup CreateGroupLocal(int id)
    {
        GameObject groupObject = new GameObject($"Troop Group {myGroups.Count + 1}");
        groupObject.transform.SetParent(transform, false);
        TroopGroup group = groupObject.AddComponent<TroopGroup>();
        group.SetGroupId(id);
        group.SetOwner(this);
        myGroups.Add(group);
        nextGroupId = Mathf.Max(nextGroupId, id + 1);
        return group;
    }

    public void RegisterGroups(IEnumerable<TroopGroup> groups)
    {
        if (groups == null) return;
        foreach (TroopGroup group in groups)
        {
            if (group == null) continue;
            if (group.GroupId < 0)
                group.SetGroupId(nextGroupId);
            else if (FindGroupById(group.GroupId) != null && FindGroupById(group.GroupId) != group)
            {
                Debug.LogError($"[TroopInteractor] Duplicitní TroopGroup ID {group.GroupId}.", group);
                continue;
            }

            group.SetOwner(this);
            if (!myGroups.Contains(group)) myGroups.Add(group);
            nextGroupId = Mathf.Max(nextGroupId, group.GroupId + 1);
        }
    }

    [ServerRpc]
    private void CreateGroupServerRpc(int groupId, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        EnsureServerGroup(groupId);
    }

    // TroopGroup is intentionally a local MonoBehaviour, so RPCs carry only its
    // stable per-player ID. This also handles a spawn request arriving before
    // the separate group-creation RPC has been processed.
    internal TroopGroup EnsureServerGroup(int groupId)
    {
        if (!IsServer || groupId < 0 || groupId > 1000) return null;
        TroopGroup existing = FindGroupById(groupId);
        if (existing != null) return existing;
        if (groupId != nextGroupId) return null;
        return CreateGroupLocal(groupId);
    }

    public TroopGroup FindGroupById(int groupId)
    {
        foreach (TroopGroup group in myGroups) if (group != null && group.GroupId == groupId) return group;
        return null;
    }

    public void MoveGroup(TroopGroup group, Vector3 destination) { if (myGroups.Contains(group)) group.MoveOrder(destination); }
    public void AttackGroup(TroopGroup group, TroopGroup enemyGroup)
    {
        if (group == null || enemyGroup == null || !myGroups.Contains(group)) return;
        Troop target = null;
        float best = float.MaxValue;
        foreach (Troop enemy in enemyGroup.Units)
        {
            if (enemy == null) continue;
            float strength = enemy.Health.Value + enemy.Damage * Mathf.Max(1f, enemy.AttackRange);
            if (strength < best) { best = strength; target = enemy; }
        }
        if (target != null) group.Attack(target);
    }
    public void AttackEnemyGroup(TroopGroup group, Troop enemyMember)
    {
        if (group == null || enemyMember == null || !myGroups.Contains(group)) return;
        List<Troop> members = TroopGroup.GetUnitsInSameGroup(enemyMember);
        Troop target = null;
        float weakest = float.MaxValue;
        foreach (Troop member in members)
        {
            if (member == null) continue;
            float strength = member.Health.Value + member.Damage * Mathf.Max(1f, member.AttackRange);
            if (strength < weakest) { weakest = strength; target = member; }
        }
        if (target != null) group.Attack(target);
    }
    public void AttackBuilding(TroopGroup group, Building target) { if (group != null && myGroups.Contains(group)) group.Attack(target); }
    public void Defend(TroopGroup group, Vector3 position) { if (group != null && myGroups.Contains(group)) group.SetTask(TroopGroupTask.Defend, position); }
    public void Harvest(TroopGroup group, Resource resource = null) { if (group != null && myGroups.Contains(group)) group.Harvest(resource); }

    // Update is called once per frame
    void Update()
    {
        
    }

    // ============================================================
    //  Seznam vlastních troopů
    // ============================================================

    public void RegisterTroop(Troop troop, TroopGroup group = null)
    {
        if (troop == null || troop.Group != null)
            return;
        TroopGroup targetGroup = null;
        if (targetGroup == null) targetGroup = FindGroupById(troop.TroopGroupId.Value);
        if (targetGroup == null) { Debug.LogError($"[TroopInteractor] Chybí skupina s ID {troop.TroopGroupId.Value} pro jednotku {troop.name}."); return; }
        targetGroup.AddUnit(troop);
        if (replacementRequests.TryGetValue(targetGroup, out List<TroopType> requests))
        {
            requests.Remove(troop.Type);
            if (requests.Count == 0) replacementRequests.Remove(targetGroup);
        }
    }

    public bool isAllowedToSpawn(Troop troopToSpawn)
    {
        if (!IsServer || troopToSpawn == null)
            return false;
        
        int troopCount = 0;
        foreach (TroopGroup group in myGroups)
        {
            if (group != null) foreach (Troop spawnedTroops in group.Units)
                if (spawnedTroops != null && troopToSpawn.Type == spawnedTroops.Type) troopCount++;
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

        if (troop.Group != null) troop.Group.RemoveUnit(troop);
    }

    internal void RequestReplacementFor(TroopGroup group, TroopType type, int spriteIndex)
    {
        if (!refillAllowed(group)) return;
        if (!replacementRequests.TryGetValue(group, out List<TroopType> requests))
        {
            requests = new List<TroopType>();
            replacementRequests.Add(group, requests);
        }
        requests.Add(type);
        TroopSpawner spawner = GetComponent<TroopSpawner>();
        if (spawner != null) spawner.RequestSpawnTroopOfType(type, group, spriteIndex);
    }
    private bool refillAllowed(TroopGroup group) => group != null && group.RefillUnits && group.Units.Count < group.MaxUnits;

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

        foreach (TroopGroup group in myGroups)
        foreach (Troop troop in group.Units)
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

    public void OrderSelectedGroupToHarvest()
    {
        Troop troop = selector.SelectedTroop;
        if (troop != null && troop.Group != null) troop.Group.Harvest();
    }

}
