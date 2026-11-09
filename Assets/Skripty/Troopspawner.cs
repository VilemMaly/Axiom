using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Umístit na hlavní Player NetworkObject.
/// Spawnování reálně provádí vždy jen server, klient si o něj řekne přes ServerRpc.
/// </summary>
public class TroopSpawner : NetworkBehaviour
{
    public event Action<int, int> SpawnRequestRejected;
    public event Action<int, int, TroopType> SpawnRequestAccepted;

    [System.Serializable]
    public class TroopEntry
    {
        public string troopName;
        public GameObject prefab;

        [Tooltip("Poloměr koule, kterou server zkouší 'nasadit' na dané místo, aby ověřil, že tam prefab nekoliduje.")]
        public float checkRadius = 0.5f;
    }

    [Header("Nastavení spawn zóny")]
    [Tooltip("Pokud necháš prázdné, použije se transform tohoto objektu (hráče).")]
    private Transform spawnCenter;
    [SerializeField] private float spawnAreaRadius = 5f;
    [SerializeField] private int maxSpawnAttempts = 20;

    [Header("Raycast na zem (stejný princip jako u budov)")]
    [Tooltip("Layer, který představuje zem/terén – na něj se raycastuje shora dolů.")]
    [SerializeField] private LayerMask groundLayerMask;
    [Tooltip("Z jaké výšky nad zemí se raycast spouští.")]
    [SerializeField] private float raycastStartHeight = 50f;
    [Tooltip("Jak daleko dolů raycast dostřelí.")]
    [SerializeField] private float raycastMaxDistance = 200f;

    [Header("Kontrola kolize před spawnem")]
    [SerializeField] private LayerMask obstacleLayerMask;

    [Header("Seznam jednotek (pořadí = index)")]
    [SerializeField] private List<TroopEntry> troopPrefabs = new List<TroopEntry>();

    [Header("Předem vytvořené skupiny")]
    [Tooltip("Přiřaď sem TroopGroup komponenty používané tlačítky skupin. Pořadí není důležité; ID musí být na každém klientovi stejné.")]
    [SerializeField] private List<TroopGroup> troopGroups = new List<TroopGroup>();

    private readonly HashSet<BuildingSpawner> subscribedBuildingSpawners = new HashSet<BuildingSpawner>();

    private Transform SpawnCenter => spawnCenter != null ? spawnCenter : transform;

    public override void OnNetworkSpawn()
    {
        TroopInteractor interactor = GetComponent<TroopInteractor>();
        if (interactor != null)
            interactor.RegisterGroups(troopGroups);
    }

    public void RegisterTroopGroup(TroopGroup group)
    {
        if (group == null) return;
        if (!troopGroups.Contains(group)) troopGroups.Add(group);
        TroopInteractor interactor = GetComponent<TroopInteractor>();
        if (interactor != null) interactor.RegisterGroups(new[] { group });
    }

    // ============================================================
    //  VEŘEJNÉ API – tohle voláš z tlačítek v UI
    // ============================================================

    /// <summary>
    /// Zavolej z tlačítka a předej index jednotky z listu (0, 1, 2...).
    /// V Unity Button OnClick() lze index rovnou nastavit jako parametr.
    /// </summary>
    public void RequestSpawnTroop(int troopIndex, TroopGroup group, int spriteIndex)
    {
        if (!IsOwner || group == null) return;

        Debug.Log($"[TroopSpawner] Hráč {OwnerClientId} žádá o spawn jednotky index {troopIndex} do skupiny {group.GroupId}.");
        RequestSpawnTroopServerRpc(troopIndex, group.GroupId, spriteIndex);
    }

    public void RequestSpawnTroopOfType(TroopType type, TroopGroup group, int spriteIndex)
    {
        if (!IsOwner || group == null) return;
        for (int i = 0; i < troopPrefabs.Count; i++)
        {
            Troop troop = troopPrefabs[i]?.prefab != null ? troopPrefabs[i].prefab.GetComponent<Troop>() : null;
            if (troop != null && troop.Type == type && group != null) { RequestSpawnTroop(i, group, spriteIndex); return; }
        }
        Debug.LogWarning($"[TroopSpawner] Není nastaven prefab pro refill jednotky {type}.");
    }

    [ServerRpc]
    private void RequestSpawnTroopServerRpc(int troopIndex, int groupId, int spriteIndex, ServerRpcParams rpcParams = default)
    {
        ulong requestingClientId = rpcParams.Receive.SenderClientId;
        if (troopIndex < 0 || troopIndex >= troopPrefabs.Count)
        {
            Debug.LogWarning($"[TroopSpawner] Neplatný index jednotky: {troopIndex}");
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }

        TroopEntry entry = troopPrefabs[troopIndex];
        Troop troop = entry != null && entry.prefab != null ? entry.prefab.GetComponent<Troop>() : null;
        if (troop == null)
        {
            Debug.LogWarning($"[TroopSpawner] Pro index {troopIndex} není přiřazen prefab s komponentou Troop.");
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }

        TroopInteractor interactor = GetComponent<TroopInteractor>();
        // TroopGroup is local to each peer. Resolve it from this player's
        // interactor, creating the server-side counterpart if its creation RPC
        // has not arrived yet. RPCs on different NetworkBehaviours are not
        // guaranteed to arrive in the order the client issued them.
        TroopGroup targetGroup = interactor != null ? interactor.FindGroupById(groupId) : null;
        if (targetGroup == null && interactor != null)
            targetGroup = interactor.EnsureServerGroup(groupId);
        if (targetGroup == null || targetGroup.Units.Count + CountQueuedForGroup(groupId) >= targetGroup.MaxUnits)
        {
            Debug.LogWarning($"[TroopSpawner] Neplatná skupina {groupId} nebo nedostatek místa.");
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }
        bool slotOccupied = false;
        foreach (Troop existingTroop in targetGroup.Units)
        {
            if (existingTroop != null && existingTroop.groupSpriteIndex == spriteIndex)
            {
                slotOccupied = true;
                break;
            }
        }
        if (!slotOccupied && CountQueuedForSlot(groupId, spriteIndex) > 0)
            slotOccupied = true;
        if (slotOccupied)
        {
            Debug.LogWarning($"[TroopSpawner] Slot {spriteIndex} ve skupině {groupId} je již obsazen.");
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }
        if (interactor == null || !interactor.isAllowedToSpawn(troop))
        {
            Debug.Log("Hráč to nemůže spawnout, protože by překročil limit jednotek pro hráče");
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }

        if (!GetComponent<PlayerTechnology>().CheckTroopResearched(troop))
        {
            Debug.Log("Klient nemá vyzkoumanou tu jednotku, nemůže ji spawnout");
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(requestingClientId, out var client) ||
            client.PlayerObject == null)
        {
            Debug.LogWarning($"[TroopSpawner] Nelze najít hráče {requestingClientId}.");
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }

        BuildingInteractor buildingInteractor = client.PlayerObject.GetComponent<BuildingInteractor>();
        BuildingSpawner buildingSpawner = buildingInteractor != null ? buildingInteractor.GetSpawner(troop.Type) : null;
        if (buildingSpawner == null)
        {
            Debug.LogWarning($"[TroopSpawner] Hráč {requestingClientId} nemá vhodnou budovu pro jednotku {troop.Type}.");
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }

        Building building = buildingSpawner.GetComponent<Building>();
        if (building == null || building.OwnerClientId != requestingClientId || !building.IsConstructionComplete.Value ||
            !building.IsOperational || !buildingSpawner.CanSpawn(troop.Type))
        {
            Debug.LogWarning($"[TroopSpawner] Vybraný spawner pro jednotku {troop.Type} není způsobilý.");
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }

        if (building.Type == Building.BuildingType.Core)
            spawnCenter = building.transform;

        bool addedListener = subscribedBuildingSpawners.Add(buildingSpawner);
        if (addedListener)
            buildingSpawner.loadingFinished += TrySpawnTroopServerSide;

        if (!buildingSpawner.EnqueueTroop(troopIndex, troop, groupId, spriteIndex))
        {
            if (addedListener)
            {
                subscribedBuildingSpawners.Remove(buildingSpawner);
                buildingSpawner.loadingFinished -= TrySpawnTroopServerSide;
            }
            NotifySpawnRequestRejected(requestingClientId, groupId, spriteIndex);
            return;
        }
        NotifySpawnRequestAccepted(requestingClientId, groupId, spriteIndex, troop.Type);
    }

    private void NotifySpawnRequestAccepted(ulong requestingClientId, int groupId, int spriteIndex, TroopType troopType)
    {
        SpawnRequestAcceptedClientRpc(groupId, spriteIndex, troopType, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { requestingClientId } }
        });
    }

    private void NotifySpawnRequestRejected(ulong requestingClientId, int groupId, int spriteIndex)
    {
        SpawnRequestRejectedClientRpc(groupId, spriteIndex, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { requestingClientId }
            }
        });
    }

    [ClientRpc]
    private void SpawnRequestRejectedClientRpc(int groupId, int spriteIndex, ClientRpcParams rpcParams = default)
    {
        if (IsOwner)
            SpawnRequestRejected?.Invoke(groupId, spriteIndex);
    }

    [ClientRpc]
    private void SpawnRequestAcceptedClientRpc(int groupId, int spriteIndex, TroopType troopType, ClientRpcParams rpcParams = default)
    {
        if (IsOwner)
            SpawnRequestAccepted?.Invoke(groupId, spriteIndex, troopType);
    }

    private int CountQueuedForSlot(int groupId, int spriteIndex)
    {
        int count = 0;
        BuildingInteractor buildings = GetComponent<BuildingInteractor>();
        if (buildings == null) return count;
        foreach (Building building in buildings.MyBuildings)
        {
            BuildingSpawner spawner = building != null ? building.GetComponent<BuildingSpawner>() : null;
            if (spawner != null) count += spawner.CountQueuedTroopsForSlot(groupId, spriteIndex);
        }
        return count;
    }

    private int CountQueuedForGroup(int groupId)
    {
        int count = 0;
        BuildingInteractor buildings = GetComponent<BuildingInteractor>();
        if (buildings == null) return count;
        foreach (Building building in buildings.MyBuildings)
        {
            BuildingSpawner spawner = building != null ? building.GetComponent<BuildingSpawner>() : null;
            if (spawner != null) count += spawner.CountQueuedTroopsForGroup(groupId);
        }
        return count;
    }

    // ============================================================
    //  RPC – klient žádá server
    // ============================================================

    // ============================================================
    //  SERVEROVÁ LOGIKA (jediné místo, kde se reálně spawnuje)
    // ============================================================

    private void TrySpawnTroopServerSide(int troopIndex, Building building, int groupId, int spriteIndex)
    {
        if (IsServer && building != null)
            SpawnCompletedTroop(troopIndex, building, groupId, spriteIndex);

        BuildingSpawner buildingSpawner = building != null ? building.GetComponent<BuildingSpawner>() : null;
        if (buildingSpawner != null && !buildingSpawner.HasQueuedTroops &&
            subscribedBuildingSpawners.Remove(buildingSpawner))
        {
            buildingSpawner.loadingFinished -= TrySpawnTroopServerSide;
        }
    }

    private void SpawnCompletedTroop(int troopIndex, Building building, int groupId, int spriteIndex)
    {
        if (troopIndex < 0 || troopIndex >= troopPrefabs.Count)
        {
            Debug.LogWarning($"[TroopSpawner] Neplatný index jednotky: {troopIndex}");
            return;
        }

        TroopEntry entry = troopPrefabs[troopIndex];
        if (entry == null || entry.prefab == null)
        {
            Debug.LogWarning($"[TroopSpawner] Prefab na indexu {troopIndex} není přiřazen.");
            return;
        }

        ulong ownerClientId = building.OwnerClientId;
        Debug.Log($"[TroopSpawner] Server spawn '{entry.troopName}' pro hráče {ownerClientId}.");
        Vector3 spawnPos;
        if (building.GetComponent<BuildingSpawner>() != null)
        {
            BuildingSpawner spawner = building.GetComponent<BuildingSpawner>();
            if (spawner.spawnPoint == null)
            {
                Debug.LogWarning("[TroopSpawner] Factory nemá platný spawnPoint.");
                return;
            }
            spawnPos = spawner.spawnPoint.position;
        }
        else if (!TryFindFreePosition(1f, out spawnPos))
        {
            Debug.Log("Server nespawnuje jednotku, nenašel volnou pozici.");
            return;
        }
        
        Troop spawnedTroop = SpawnTroopAt(entry.prefab, spawnPos, ownerClientId, groupId, spriteIndex);
        if (spawnedTroop == null)
        {
            Debug.Log("Spawnutá jednotka neexistuje.");
            return;
        }

        if (building.GetComponent<BuildingSpawner>() != null)
        {
            spawnedTroop.MoveTo(building.GetComponent<BuildingSpawner>().rallyPoint.position);
        }
            
    }

    public override void OnNetworkDespawn()
    {
        foreach (BuildingSpawner buildingSpawner in subscribedBuildingSpawners)
        {
            if (buildingSpawner != null)
                buildingSpawner.loadingFinished -= TrySpawnTroopServerSide;
        }
        subscribedBuildingSpawners.Clear();
        base.OnNetworkDespawn();
    }

    private bool TryFindFreePosition(float checkRadius, out Vector3 result)
    {
        for (int i = 0; i < maxSpawnAttempts; i++)
        {
            // 1) Náhodný bod v kruhu, pohled shora (jen X/Z)
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * spawnAreaRadius;
            Vector3 xzPoint = SpawnCenter.position + new Vector3(randomCircle.x, 0f, randomCircle.y);

            // 2) Raycast shora dolů na layer země – stejný princip jako u budov
            Vector3 rayOrigin = new Vector3(xzPoint.x, SpawnCenter.position.y + raycastStartHeight, xzPoint.z);

            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastMaxDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
            {
                continue; // na tomhle místě není pod čím spawnovat, zkus jiný bod
            }

            Vector3 candidate = hit.point;

            // 3) Kontrola kolize přesně na místě dopadu raycastu
            if (!Physics.CheckSphere(candidate, checkRadius, obstacleLayerMask, QueryTriggerInteraction.Ignore))
            {
                result = candidate;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    private Troop SpawnTroopAt(GameObject prefab, Vector3 position, ulong ownerClientId, int groupId, int spriteIndex)
    {
        GameObject instance = Instantiate(prefab, position, Quaternion.identity);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[TroopSpawner] Prefab '{prefab.name}' nemá komponentu NetworkObject!");
            Destroy(instance);
            return null;
        }
        Debug.Log($"[TroopSpawner] Server spawnul '{prefab.name}' pro hráče {ownerClientId} na {position}.");

        // Jednotka bude patřit hráči, který o ni požádal.
        // Pokud má být jednotka vlastněná serverem (ne hráčem), použij: netObj.Spawn();
        Troop troop = instance.GetComponent<Troop>();
        if (troop == null) { Destroy(instance); return null; }
        troop.groupSpriteIndex = spriteIndex;
        troop.TroopGroupId.Value = groupId;
        netObj.SpawnWithOwnership(ownerClientId);
        return troop;
    }

    // ============================================================
    //  GIZMO – vizualizace spawn kruhu v editoru
    // ============================================================

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 center = SpawnCenter != null ? SpawnCenter.position : transform.position;
        DrawGizmoCircle(center, spawnAreaRadius);
    }

    private void DrawGizmoCircle(Vector3 center, float radius)
    {
        const int segments = 40;
        Vector3 prevPoint = center + new Vector3(radius, 0, 0);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }
}
