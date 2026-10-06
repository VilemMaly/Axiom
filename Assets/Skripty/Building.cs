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
    public bool IsPowerSource;
    public bool IsEnergyConsumer;

    public enum BuildingType
    {
        Core,
        LaserTower,
        Battery,
        Mine,
        Factory,
        Wall,
        Radar,
        EnergyPlant,
        Gate,
        Storage,
        connector,
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
    protected bool IsOperational { get; set; } = false;

    public NetworkVariable<bool> IsConnected =
        new NetworkVariable<bool>(
            writePerm: NetworkVariableWritePermission.Server);

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

        if (IsOwner || IsServer)
        {
            BuildingInteractor interactor = NetworkManager.Singleton.ConnectedClients[OwnerClientId].PlayerObject.GetComponent<BuildingInteractor>();
            if (interactor != null)
            {
                if (!interactor.RegisterBuilding(this))
                {
                    
                    if(IsServer)
                    {
                        NetworkObject.Despawn();
                        Debug.LogWarning($"[Building-DIAG] {name} se nepodařilo zaregistrovat v BuildingInteractoru.");
                        // pokud se nepodařilo zaregistrovat, tak se budova zničí (despawnne) a hráč dostane zpět corium.
                        // příště by se to mělo kontrolovat ještě před spawnem, aby se to nestalo.
                    }
                        
                }
            }
                
            else
                Debug.LogWarning($"[Building-DIAG] {name} nenašel BuildingInteractor na vlastním PlayerObjectu.");
        }

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
        if (IsOwner || IsServer)
        {
            BuildingInteractor interactor = NetworkManager.Singleton.ConnectedClients[OwnerClientId].PlayerObject.GetComponent<BuildingInteractor>();
            if (interactor != null)
                interactor.UnregisterBuilding(this);
            else
                Debug.LogWarning($"[Building-DIAG] {name} nenašel BuildingInteractor na vlastním PlayerObjectu.");
        }
        base.OnNetworkDespawn();
    }

    public virtual void OnBuilt()
    {
    }

    public void searchForEnergyConnections()
    {
        if (!IsServer)
            return;
        // pokud nepožaduje elektřinu, tak se nemusí připojovat k síti budov
        if (IsEnergyConsumer == false)
        {
            IsConnected.Value = true;
            return;
        }
        // pokud je zdroj energie tak je automaticky připojený k síti budov
        IsConnected.Value = true;
        // vyhledá budovy v okolí, které nejsou připojené k síti budov a připojí je k sobě, pokud jsou v dosahu.

            // vytvoří sphere collider s lehkou pamětí, aby se našly všechny budovy v dosahu s určitým layer maskem (např. "Building").
            Collider[] hitColliders = Physics.OverlapSphere(
                transform.position,
                SightRange,
                LayerMask.GetMask("Building"),
                QueryTriggerInteraction.Ignore
            );
            // pro každou budovu, která je v dosahu a není připojená k síti budov, se zavolá metoda searchForEnergyConnections (rekurzivně).
            foreach (var hitCollider in hitColliders)
            {
                Building building = hitCollider.GetComponent<Building>();
                if (building != null && !building.IsConnected.Value)
                {
                    building.IsConnected.Value = true;
                    building.searchForEnergyConnections();
                    // spojí visuálně kabel mezi zdrojem energie a budovou, která je v dosahu na obou klientech (server a vlastník budovy).
                    CableManager cableManager = NetworkManager.Singleton.ConnectedClients[OwnerClientId].PlayerObject.GetComponent<CableManager>();
                    if (cableManager != null)
                    {
                        cableManager.CreateCableClientRpc(
                            transform.position,
                            building.transform.position
                        );
                    }
                    else
                    {
                        Debug.LogWarning($"[Building-DIAG] {name} nenašel CableManager na vlastním PlayerObjectu.");
                    }
                }
            }
        
    }

    /// <summary>
    /// Serverová logika budovy.
    /// </summary>
    public virtual void UpdateBuilding()
    {
    }

    private float operationTimer = 0f;

    private const float OperationInterval = 1f;

    private void Update()
    {
        if (!IsServer)
            return;

        if (!IsConstructionComplete.Value)
        {
            IsOperational = false;
            return;
        }

        if (IsEnergyConsumer == true && IsConnected.Value == false)
        {
            // budoucí vylepšení: animace, že budova je odpojená od sítě a nefunguje, dokud se znovu nepřipojí k síti budov.
            IsOperational = false;
            return;
        }
        // ------------------------------------------------------------
        // KONTROLA SPOTŘEBY - 1x ZA SEKUNDU
        // ------------------------------------------------------------

        operationTimer += Time.deltaTime;

        if (operationTimer >= OperationInterval)
        {
            operationTimer -= OperationInterval;

            UpdateOperation();
        }

        // ------------------------------------------------------------
        // LOGIKA BUDOVY - KAŽDÝ FRAME
        // ------------------------------------------------------------

        if (IsOperational)
        {
            UpdateBuilding();
        }
    }

    private void UpdateOperation()
    {
        if (!IsServer)
            return;

        if (!IsConstructionComplete.Value)
        {
            IsOperational = false;
            return;
        }

        if (OwnerResources == null)
        {
            IsOperational = false;
            return;
        }

        // ------------------------------------------------------------
        // URČENÍ SKUTEČNÉ SPOTŘEBY
        // ------------------------------------------------------------

        int actualCoriumConsumption = CoriumConsumption;
        int actualEnergyConsumption = EnergyConsumption;

        // ------------------------------------------------------------
        // CORIUM
        //
        // Pokud budova vyrábí Corium a produkce by dosáhla/překročila
        // maximální kapacitu, nemusí platit CoriumConsumption.
        // ------------------------------------------------------------

        if (CoriumProduction > 0)
        {
            int missingCorium = OwnerResources.MaxCorium.Value - OwnerResources.Corium.Value;

            if (missingCorium <= 0)
            {
                actualEnergyConsumption = 0;
                actualCoriumConsumption = 0;
            }
        }

        // ------------------------------------------------------------
        // ENERGIE
        //
        // Pokud budova vyrábí Energii a produkce by dosáhla/překročila
        // maximální kapacitu, nemusí platit EnergyConsumption.
        // ------------------------------------------------------------

        if (EnergyProduction > 0)
        {
            int missingEnergy = OwnerResources.MaxEnergy.Value - OwnerResources.Energy.Value;

            if (missingEnergy <= 0)
            {
                actualEnergyConsumption = 0;
                actualCoriumConsumption = 0;
            }
        }

        // ------------------------------------------------------------
        // ZAPLACENÍ PROVOZU
        // ------------------------------------------------------------

        if (!OwnerResources.TrySpend(
            actualCoriumConsumption,
            actualEnergyConsumption))
        {
            IsOperational = false;

            Debug.Log(
                $"[Building] {Type} ({name}) hráč {OwnerClientId} " +
                $"nemá dostatek coria/energie. Budova je OFFLINE."
            );

            return;
        }

        // ------------------------------------------------------------
        // BUDOVA JE ONLINE
        // ------------------------------------------------------------

        IsOperational = true;

        // Produkce proběhne až po úspěšném zaplacení provozu.
        OwnerResources.Add(
            CoriumProduction,
            EnergyProduction
        );
        /*
        Debug.Log(
            $"[Building] {Type} ({name}) hráč {OwnerClientId} " +
            $"zaplatil provoz: Corium={actualCoriumConsumption}, " +
            $"Energy={actualEnergyConsumption}. " +
            $"Produkce: Corium={CoriumProduction}, " +
            $"Energy={EnergyProduction}. " +
            $"Budova je ONLINE."
        );*/

        UpdateProduction();
    }

    // Zavolá se, když se úspěšně zaplatí provoz budovy
    // a je online, aby se mohlo dělat něco navíc
    // (např. těžit corium, vyrábět energii, atd.).
    public virtual void UpdateProduction()
    {
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