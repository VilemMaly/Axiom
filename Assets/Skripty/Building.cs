using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
public abstract class Building : NetworkBehaviour
{
    [Header("Stats – nastav v Inspectoru na prefabu")]
    public int MaxHealth;
    public int Cost;
    public int RequiredCorium;
    public int MaxBuildingAmount;
    public int CoriumConsumption;
    public int EnergyConsumption;
    public int CoriumProduction;
    public int EnergyProduction;
    public int Damage;
    public float AttackRange;
    public float AttackSpeed;
    public float AttackCooldown;
    public float SightRange;
    public enum BuildingType
    {
        Core,
        LaserTower,
        CoriumMiner,
        Research,
        Factory,
        Wall,
        Radar,
        EnergyPlant
    }
    public abstract BuildingType Type { get; }

    public NetworkVariable<int> Health = new NetworkVariable<int>(
        writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> StoredCorium =
    new(writePerm: NetworkVariableWritePermission.Server);
    protected PlayerResources OwnerResources { get; private set; }

    // TOHLE je jediný spolehlivý příznak "budova existuje a smí se s ní pracovat" (spawn/despawn)
    protected bool IsOperational { get; private set; } = false;

    // Nové: samostatný příznak "budova je dostavěná a smí útočit/produkovat/atd."
    // IsOperational != IsConstructionComplete - budova může být "operational" (naspawnutá,
    // platná reference, dá se jí ubližovat) a přitom ještě rozestavěná.
    public NetworkVariable<bool> IsConstructionComplete =
    new(writePerm: NetworkVariableWritePermission.Server);
    public bool NeedsConstruction =>
    !IsConstructionComplete.Value;
    public int MissingCorium =>
    RequiredCorium - StoredCorium.Value;
    public bool CanReceiveCorium =>
    !IsConstructionComplete.Value;

    public void ReceiveCorium(int amount)
{
    if (StoredCorium.Value >= RequiredCorium)
    {
        if(IsServer)
        {
        StoredCorium.Value = RequiredCorium;

        IsConstructionComplete.Value = true;
        }

        OnBuilt();
    }
    if (!IsServer)
        return;

    if (IsConstructionComplete.Value)
        return;

    StoredCorium.Value += amount;

}
    public override void OnNetworkSpawn()
    {
        if (IsServer)
            Health.Value = MaxHealth;

        if (IsServer)
        {
            var client = NetworkManager.Singleton.ConnectedClients[OwnerClientId];
            OwnerResources = client.PlayerObject.GetComponent<PlayerResources>();
        }

        IsOperational = true;

        ReceiveCorium(0); // inicializace IsConstructionComplete podle StoredCorium

    }

    public override void OnNetworkDespawn()
    {
        IsOperational = false;
    }

    public virtual void OnBuilt() { }

    public virtual void UpdateBuilding() { }

    private void Update()
    {
    if(!IsServer)
        return;

    if(!IsOperational)
        return;

    if(IsConstructionComplete.Value)
    {
        UpdateBuilding();
    }
    }


    public virtual void TakeDamage(int damage)
    {
        if (!IsServer || !IsOperational) return;

        Health.Value -= damage;
        if (Health.Value <= 0)
            DestroyBuilding();
    }

    protected virtual void DestroyBuilding()
    {
        if (!IsServer || !IsOperational) return;
        GetComponent<NetworkObject>().Despawn();
    }

    // ============================================================
    //  SDÍLENÁ SERVEROVÁ LOGIKA UMÍSTĚNÍ / SPAWNU BUDOVY
    //  Přesunuto sem z Build.cs a BuildingPlacementController.cs, aby
    //  validace a samotný spawn existovaly na JEDNOM místě. Kdokoliv,
    //  kdo chce budovu postavit (Build.cs, případně budoucí systémy),
    //  by měl volat tyhle statické metody místo vlastní kopie logiky.
    //  Vše je čistě serverové - volat pouze z ServerRpc / IsServer větví.
    // ============================================================

    /// <summary>
    /// Ověří, že pod zadaným bodem je terén (downward raycast na groundLayerMask)
    /// a že na výsledné "podlahové" pozici nic nekoliduje (Physics.CheckSphere na
    /// obstacleLayerMask). Vrací zarovnanou pozici na terénu.
    /// </summary>
    public static bool TryValidatePosition(
        Vector3 desiredPosition,
        float checkRadius,
        LayerMask groundLayerMask,
        LayerMask obstacleLayerMask,
        float raycastStartHeight,
        float raycastMaxDistance,
        out Vector3 groundedPosition,
        out string failReason)
    {
        groundedPosition = desiredPosition;
        failReason = null;

        Vector3 rayOrigin = new Vector3(desiredPosition.x, desiredPosition.y + raycastStartHeight, desiredPosition.z);

        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastMaxDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            failReason = "Pod bodem není terén";
            return false;
        }

        groundedPosition = hit.point;

        if (Physics.CheckSphere(groundedPosition + Vector3.up * 0.5f, checkRadius, obstacleLayerMask, QueryTriggerInteraction.Ignore))
        {
            failReason = "Pozice je obsazená";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Core-specific pravidla: vzdálenost od spawnu hráče a limit "jen jedno Core".
    /// Pro jiné typy budov vrací rovnou true (nic k ověření).
    /// </summary>
    public static bool ValidateCoreConstraints(
        BuildingType type,
        Vector3 playerSpawnPosition,
        Vector3 targetPosition,
        float maxCoreDistanceFromSpawn,
        PlayerResources ownerResources,
        out string failReason)
    {
        failReason = null;

        if (type != BuildingType.Core)
            return true;

        float distance = Vector3.Distance(playerSpawnPosition, targetPosition);
        if (distance > maxCoreDistanceFromSpawn)
        {
            failReason = "Core je příliš daleko od spawnu";
            return false;
        }

        if (ownerResources != null && ownerResources.Cores.Value >= 1)
        {
            failReason = "Máš už postavené jádro";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Server-authoritative instantiate + SpawnWithOwnership. Volat POUZE po úspěšné
    /// validaci pozice (a případně zdrojů) - tahle metoda sama nic neověřuje.
    ///
    /// "groundedPosition" je bod na terénu (např. z TryValidatePosition) - metoda
    /// budovu po instanciaci dorovná tak, aby na tomto bodě stál SPODEK jejího
    /// kolideru, ne její pivot. Bez tohohle dorovnání budova půlkou trčí pod zemí,
    /// pokud má prefab pivot uprostřed/na vršku modelu (stejný princip jako u ghosta
    /// v BuildingPlacementControlleru).
    /// </summary>
    public static GameObject SpawnAndAssignOwnership(GameObject prefab, Vector3 groundedPosition, Quaternion rotation, ulong ownerClientId)
    {
        GameObject instance = Instantiate(prefab, groundedPosition, rotation);

        float baseOffset = ComputeGroundOffset(instance);
        if (baseOffset != 0f)
        {
            Vector3 pos = instance.transform.position;
            pos.y = groundedPosition.y + baseOffset;
            instance.transform.position = pos;
        }

        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"[Building] Prefab '{prefab.name}' nemá komponentu NetworkObject!");
            Destroy(instance);
            return null;
        }

        netObj.SpawnWithOwnership(ownerClientId);
        return instance;
    }

    /// <summary>
    /// Spočítá vzdálenost od pivotu instance ke spodní hraně jejího kolideru
    /// (sloučené Bounds přes všechny collidery v hierarchii). Vrací 0, pokud
    /// instance nemá žádný Collider. Sdílené mezi reálným spawnem (výše) a
    /// ghost preview v BuildingPlacementControlleru, aby obě místa počítala
    /// zarovnání na zem identicky.
    /// </summary>
    public static float ComputeGroundOffset(GameObject instance)
    {
        Collider[] colliders = instance.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
            return 0f;

        Bounds combined = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++)
            combined.Encapsulate(colliders[i].bounds);

        return instance.transform.position.y - combined.min.y;
    }
}