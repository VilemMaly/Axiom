using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Umístit na hlavní Player NetworkObject.
/// Spawnování reálně provádí vždy jen server, klient si o něj řekne přes ServerRpc.
/// </summary>
public class TroopSpawner : NetworkBehaviour
{
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

    [SerializeField] private Selector buildingSelector;
    private int currentTroopIndex;
    private NetworkVariable<NetworkObjectReference> selectedBuildingRef = new NetworkVariable<NetworkObjectReference>(
    default,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server   // teď píše jen server
);

    private Transform SpawnCenter => spawnCenter != null ? spawnCenter : transform;

    // ============================================================
    //  VEŘEJNÉ API – tohle voláš z tlačítek v UI
    // ============================================================

    /// <summary>
    /// Zavolej z tlačítka a předej index jednotky z listu (0, 1, 2...).
    /// V Unity Button OnClick() lze index rovnou nastavit jako parametr.
    /// </summary>
    public void RequestSpawnTroop(int troopIndex)
{
    if (!IsOwner) return;

    Debug.Log($"[TroopSpawner] Hráč {OwnerClientId} žádá o spawn jednotky index {troopIndex}.");
    if (!RequestSetSelectedBuilding()) return; // zajistí, že server ví, která budova je vybraná
    RequestSpawnTroopServerRpc(troopIndex);
}

    [ServerRpc]
    private void RequestSpawnTroopServerRpc(int troopIndex, ServerRpcParams rpcParams = default)
    {
        ulong requestingClientId = rpcParams.Receive.SenderClientId;

        Transform center = transform; // fallback na hráče
        if (selectedBuildingRef.Value.TryGet(out NetworkObject buildingNetObj))
        {
            spawnCenter = buildingNetObj.transform;
            if(troopIndex == 0 && buildingNetObj.GetComponent<Building>().Type != Building.BuildingType.Core)
            {
                Debug.LogWarning($"[TroopSpawner] Hráč {requestingClientId} se snaží spawnout Core jednotku z budovy, která není Core.");
                return;
            }
        }
        
        if(buildingNetObj.GetComponent<Building>().Type == Building.BuildingType.Core || buildingNetObj.GetComponent<Building>().Type == Building.BuildingType.Factory)
        {
        
        }
        else
        {
            Debug.Log($"[TroopSpawner] Hráč {requestingClientId} nemůže spawnout jednotku, protože to není správná budova, asi podvádí");
            return;
        }

        if(buildingNetObj.GetComponent<Building>().Type == Building.BuildingType.Core && troopIndex > 0)
        {
            Debug.Log($"[TroopSpawner] Hráč {requestingClientId} nemůže spawnout jednotku, protože to není správná budova, asi podvádí");
            return;
        }
        if(!buildingNetObj.GetComponent<Building>().IsConstructionComplete.Value)
        {
            Debug.Log($"[TroopSpawner] Hráč {requestingClientId} nemůže spawnout jednotku, protože budova není dostavěná");
            return;
        }
        Troop troop = troopPrefabs[troopIndex].prefab.GetComponent<Troop>();
        if(!gameObject.GetComponent<TroopInteractor>().isAllowedToSpawn(troop))
        {
            Debug.Log("Hráč to nemůže spawnout, protože by překročil limit jednotek pro hráče");
            return;
        }

        // kouknu na cenu troop a na rychlost spawn budovy, pošlu klient rpc na visual spawn skript u spawn budovy, pokaždé co přičtu corium do počtu potřebného coria
        buildingNetObj.GetComponent<BuildingSpawner>().startBuildingTroop(troop);
        currentTroopIndex = troopIndex;
        // Subscribe the method group so the event receives the ownerClientId parameter when invoked
        buildingNetObj.GetComponent<BuildingSpawner>().loadingFinished += TrySpawnTroopServerSide;
    }

    public bool RequestSetSelectedBuilding()
{
    if (!IsOwner) return false;

    if (buildingSelector.SelectedBuilding == null)
    {
        Debug.LogWarning("[TroopSpawner] Žádná budova není vybraná.");
        return false;
    }

    var buildingNetObj = buildingSelector.SelectedBuilding.GetComponent<NetworkObject>();
    if (buildingNetObj == null)
    {
        Debug.LogWarning("[TroopSpawner] Vybraná budova nemá NetworkObject.");
        return false;
    }

    RequestSetSelectedBuildingServerRpc(buildingNetObj);
    return true;
}

[ServerRpc]
private void RequestSetSelectedBuildingServerRpc(NetworkObjectReference buildingRef, ServerRpcParams rpcParams = default)
{
    ulong requestingClientId = rpcParams.Receive.SenderClientId;

    if (!buildingRef.TryGet(out NetworkObject buildingNetObj))
    {
        Debug.LogWarning("[TroopSpawner] Server nenašel budovu podle reference.");
        return;
    }

    // === VALIDACE NA SERVERU ===

    // 1) Patří budova opravdu tomuhle hráči? (např. přes NetworkObject.OwnerClientId, nebo vlastní komponentu Building.cs)
    Building building = buildingNetObj.GetComponent<Building>();
    if (building == null || building.OwnerClientId != requestingClientId)
    {
        Debug.LogWarning($"[TroopSpawner] Hráč {requestingClientId} se snaží nastavit cizí/neplatnou budovu.");
        return;
    }
    /* TODO asi nastavit polohu kamery na serveru, aby se dalo ověřit, že je budova dost blízko hráči
    // 2) (volitelně) je budova dost blízko hráči / v rozumné vzdálenosti?
    float maxDistance = 50f;
    if (Vector3.Distance(transform.position, buildingNetObj.transform.position) > maxDistance)
    {
        Debug.LogWarning($"[TroopSpawner] Budova je moc daleko od hráče {requestingClientId}.");
        return;
    }*/


    // Validace prošla → server teprve teď zapisuje
    Debug.Log($"[TroopSpawner] Server nastavil vybranou budovu pro hráče {requestingClientId} na {building.Type}.");
    selectedBuildingRef.Value = buildingRef;
}


    // ============================================================
    //  RPC – klient žádá server
    // ============================================================

    // ============================================================
    //  SERVEROVÁ LOGIKA (jediné místo, kde se reálně spawnuje)
    // ============================================================

    private void TrySpawnTroopServerSide(ulong ownerClientId)
    {
        if (!IsServer) return;

        if (currentTroopIndex < 0 || currentTroopIndex >= troopPrefabs.Count)
        {
            Debug.LogWarning($"[TroopSpawner] Neplatný index jednotky: {currentTroopIndex}");
            return;
        }

        TroopEntry entry = troopPrefabs[currentTroopIndex];
        if (entry.prefab == null)
        {
            Debug.LogWarning($"[TroopSpawner] Prefab na indexu {currentTroopIndex} není přiřazen.");
            return;
        }

        Debug.Log($"[TroopSpawner] Server spawn '{entry.troopName}' pro hráče {ownerClientId}.");

        if (TryFindFreePosition(entry.checkRadius, out Vector3 spawnPos))
        {
            SpawnTroopAt(entry.prefab, spawnPos, ownerClientId);
        }
        else
        {
            Debug.LogWarning($"[TroopSpawner] Nepodařilo se najít volné místo pro '{entry.troopName}'.");
        }
    }

    private bool TryFindFreePosition(float checkRadius, out Vector3 result)
    {
        for (int i = 0; i < maxSpawnAttempts; i++)
        {
            // 1) Náhodný bod v kruhu, pohled shora (jen X/Z)
            Vector2 randomCircle = Random.insideUnitCircle * spawnAreaRadius;
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

    private void SpawnTroopAt(GameObject prefab, Vector3 position, ulong ownerClientId)
    {
        GameObject instance = Instantiate(prefab, position, Quaternion.identity);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[TroopSpawner] Prefab '{prefab.name}' nemá komponentu NetworkObject!");
            Destroy(instance);
            return;
        }
        Debug.Log($"[TroopSpawner] Server spawnul '{prefab.name}' pro hráče {ownerClientId} na {position}.");

        // Jednotka bude patřit hráči, který o ni požádal.
        // Pokud má být jednotka vlastněná serverem (ne hráčem), použij: netObj.Spawn();
        netObj.SpawnWithOwnership(ownerClientId);
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