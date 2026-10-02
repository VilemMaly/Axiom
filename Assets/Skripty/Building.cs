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
        Battery,
        Research,
        Factory,
        Wall,
        Radar,
        EnergyPlant,
        Gate,
        Storage
    }

    public abstract BuildingType Type { get; }

    public NetworkVariable<int> Health =
        new NetworkVariable<int>(
            writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<int> StoredCorium =
        new NetworkVariable<int>(
            writePerm: NetworkVariableWritePermission.Server);

    protected PlayerResources OwnerResources { get; private set; }

    // Budova existuje a může přijímat damage / fungovat.
    protected bool IsOperational { get; private set; } = false;

    public NetworkVariable<bool> IsConstructionComplete =
        new NetworkVariable<bool>(
            writePerm: NetworkVariableWritePermission.Server);

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
            if (IsServer)
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

        // Inicializace IsConstructionComplete podle StoredCorium.
        ReceiveCorium(0);

        Debug.Log(
            $"[Building] {Type} spawned for client {OwnerClientId}. " +
            $"IsOperational={IsOperational}, " +
            $"IsConstructionComplete={IsConstructionComplete.Value}"
        );
    }

    public override void OnNetworkDespawn()
    {
        IsOperational = false;
        base.OnNetworkDespawn();
    }

    public virtual void OnBuilt()
    {
    }

    /// <summary>
    /// Serverová logika budovy.
    /// </summary>
    public virtual void UpdateBuilding()
    {
    }

    private void Update()
    {
        if (!IsServer)
            return;

        if (!IsOperational)
            return;

        if (IsConstructionComplete.Value)
        {
            UpdateBuilding();
        }
    }

    public virtual void TakeDamage(int damage)
    {
        if (!IsServer || !IsOperational)
            return;

        if (Health.Value - damage <= 0)
        {
            // DŮLEŽITÉ:
            // Budova se označí jako mrtvá JEŠTĚ PŘED DestroyBuilding().
            // Díky tomu domino exploze nemůže tuto budovu znovu poškodit
            // a spustit její DestroyBuilding().
            Health.Value = 0;
            IsOperational = false;

            DestroyBuilding();
        }
        else
        {
            Health.Value -= damage;
        }
    }

    protected virtual void DestroyBuilding()
    {
        if (!IsServer)
            return;

        if (!NetworkObject.IsSpawned)
            return;

        NetworkObject.Despawn();
    }

    // ============================================================
    // SDÍLENÁ SERVEROVÁ LOGIKA UMÍSTĚNÍ / SPAWNU BUDOVY
    // ============================================================

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

        Vector3 rayOrigin = new Vector3(
            desiredPosition.x,
            desiredPosition.y + raycastStartHeight,
            desiredPosition.z
        );

        if (!Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit hit,
            raycastMaxDistance,
            groundLayerMask,
            QueryTriggerInteraction.Ignore))
        {
            failReason = "Pod bodem není terén";
            return false;
        }

        groundedPosition = hit.point;

        if (Physics.CheckSphere(
            groundedPosition + Vector3.up * 0.5f,
            checkRadius,
            obstacleLayerMask,
            QueryTriggerInteraction.Ignore))
        {
            failReason = "Pozice je obsazená";
            return false;
        }

        return true;
    }

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

        float distance =
            Vector3.Distance(playerSpawnPosition, targetPosition);

        if (distance > maxCoreDistanceFromSpawn)
        {
            failReason = "Core je příliš daleko od spawnu";
            return false;
        }

        if (ownerResources != null &&
            ownerResources.Cores.Value >= 1)
        {
            failReason = "Máš už postavené jádro";
            return false;
        }

        return true;
    }

    public static GameObject SpawnAndAssignOwnership(
        GameObject prefab,
        Vector3 groundedPosition,
        Quaternion rotation,
        ulong ownerClientId)
    {
        GameObject instance =
            Instantiate(prefab, groundedPosition, rotation);

        float baseOffset = ComputeGroundOffset(instance);

        if (baseOffset != 0f)
        {
            Vector3 pos = instance.transform.position;
            pos.y = groundedPosition.y + baseOffset;
            instance.transform.position = pos;
        }

        NetworkObject netObj =
            instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError(
                $"[Building] Prefab '{prefab.name}' " +
                $"nemá komponentu NetworkObject!"
            );

            Destroy(instance);
            return null;
        }

        netObj.SpawnWithOwnership(ownerClientId);

        return instance;
    }

    public static float ComputeGroundOffset(GameObject instance)
    {
        Collider[] colliders =
            instance.GetComponentsInChildren<Collider>();

        if (colliders.Length == 0)
            return 0f;

        Bounds combined = colliders[0].bounds;

        for (int i = 1; i < colliders.Length; i++)
        {
            combined.Encapsulate(colliders[i].bounds);
        }

        return instance.transform.position.y - combined.min.y;
    }
}