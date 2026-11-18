using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Unity.VisualScripting;

// Další typy (bojové jednotky apod.) se přidají sem časem -
// stejný princip jako BuildingType u budov.
public enum TroopType
{
    Cargo,
    Mouse,
    Knight,
    Rocket,
    Tank,
    Medium,
    Bird,
    Radar,
    
}

public enum DamageType
{
    BigAutomaticGun,
    SmallAutomaticGun,
    Gauss,
    SmallGreenLaser,
    BlueBigPlasma,
    BlueSmallPlasma,
    EPC,
    RedMiddlePlasma,
    VioletBigPlasma,
    Explosion
}

public enum TroopScanType { Building, Troop, TroopGroup, Coria, Scrap }

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(StatusBar))]
[RequireComponent(typeof(TroopMaterial))]
public abstract class Troop : NetworkBehaviour, IDamageable
{
    public enum State
    {
        moving,
        depositing,
        harvesting,
        attacking,
        escaping,
        idle
    }

    [Header("Typ")]
    public TroopType Type;

    [Header("Stats")]
    public int MaxHealth;
    public int Cost;
    [Tooltip("Dosah, na který jednotka vidí budovy ore a odhaluje mlhu")]
    public float SightRange;
    protected bool IsBusy;
    public float WorkRange = 0f;
    public int RepairCoriumPerSecond = 0;
    public int HarvestCoriumPerSecond = 0;
    public int MaxCargo = 0;
    public int DepositCoriumPerSecond = 0;
    public int Damage;
    [Tooltip("Dosah, na který jednotka útočí")]
    public float AttackRange;
    public float AttackSpeed;
    [Header("Air combat")]
    [Tooltip("Can this troop attack air units?")]
    public bool CanAttackAir;
    [Tooltip("Is this troop an air unit?")]
    public bool IsAirUnit;

    [Header("Pohyb - NavMesh")]
    [SerializeField] private SpriteRenderer currentVisibility;
    [SerializeField] private LayerMask groundLayerMask;
    public int groupSpriteIndex;
    private float mapaHranice = 300f;
    // maximální validní poloměr pro přesun jednotky k bodu na mapě
    private float navMeshSampleRadius = 10f;
    [SerializeField] private float formationSpacing = 1.5f;

    [Header("Synchronizace pozice")]
    [SerializeField] private float positionSyncInterval = 1f;
    [SerializeField] private float positionSyncTolerance = 2f;

    [Header("Detekce budov")]
    [SerializeField] private LayerMask buildingLayerMask;
    [SerializeField] private float buildingDetectionRadius = 5f;
    [SerializeField] private float buildingDetectionInterval = 0.5f;

    [Header("Bojové efekty")]
    [SerializeField] private DamageHitRelay damageHitRelay;

    [Header("Pohyb - klient-side detekce chůze")]
    [Tooltip("Nad touto rychlostí Agenta se jednotka považuje za 'pohybující se' (pro walk animaci).")]
    [SerializeField] private float movingVelocityThreshold = 0.01f;
    [Tooltip("AudioSource s chůzovým zvukem; při lokálně detekované chůzi se přehrává ve smyčce.")]
    [SerializeField] private AudioSource walkAudioSource;
    public GameObject scrapPrefab;
    private static readonly int IsWalkingHash = Animator.StringToHash("isWalking");

    private bool isMoving;
    private bool combatMovementPaused;

    public NetworkVariable<bool> isVisible = new(false, writePerm: NetworkVariableWritePermission.Server);
    public bool isDiscovered = false;
    Vector3 lastKnownPosition;
    private TroopMaterial troopMaterial;

    public bool IsMoving => isMoving;

    [SerializeField] public Animator Animator;

    public int framePerUpdate = 10;

    private int frameCountSinceUpdate = 0;

    public IReadOnlyList<Building> BuildingsInRange => buildingsInRange;
    

    private List<Building> buildingsInRange = new();
    private readonly Collider[] buildingResults = new Collider[64];
    private float nextBuildingScan;

    public int Kompenzace = 1;

    public int MaxTroopAmount;
    public TroopGroup Group { get; internal set; }
    public NetworkVariable<int> TroopGroupId = new(-1, writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Health = new NetworkVariable<int>(
        writePerm: NetworkVariableWritePermission.Server);

    protected bool IsOperational { get; private set; } = false;

    protected NavMeshAgent Agent { get; private set; }
    public Vector3 AgentDestination => Agent.destination;
    private StatusBar statusBar;

    public GameObject Scrap;
    public int scrapDropped;

    // ---------------------------------------------------------
    // Synchronizace pozice
    // ---------------------------------------------------------

    private float nextPositionSync;
    private float stoppingDistance = 5f;

    public virtual void StopCurrentTask()
    {
        if (!IsServer) return;
        if (Agent != null) Agent.isStopped = true;
        GetComponent<GenericAttacker>()?.ClearCommandedTargetServer();
    }

    /// <summary>Explicit combat API used by TroopAI after group target assignment.</summary>
    public void Attack(Troop target)
    {
        if (!IsServer || target == null || target.OwnerClientId == OwnerClientId || target.Health.Value <= 0) return;
        if(GetComponent<GenericAttacker>() != null)
        {
            if(GetComponent<GenericAttacker>().SetCommandedTargetServer(target) == true)
            {
                Debug.Log($"[Troop] {name} attacking {target.name} owned by {target.OwnerClientId}");
            }
        }
    }

    public void Attack(Building target)
    {
        if (!IsServer || target == null || target.OwnerClientId == OwnerClientId || target.Health.Value <= 0) return;
        if(GetComponent<GenericAttacker>() != null)
        {
            if(GetComponent<GenericAttacker>().SetCommandedTargetServer(target) == true)
            {
                Debug.Log($"[Troop] {name} attacking {target.name} owned by {target.OwnerClientId}");
            }
        }
    }

    public virtual void AttackServer(Troop target) => Attack(target);

    public virtual List<T> Scan<T>(TroopScanType type) where T : Component
    {
        var found = new List<T>();
        if (!IsServer || SightRange <= 0f) return found;
        Collider[] hits = Physics.OverlapSphere(transform.position, SightRange, ~0, QueryTriggerInteraction.Collide);
        var seen = new HashSet<T>();
        foreach (Collider hit in hits)
        {
            if (hit == null) continue;
            Component component = type switch
            {
                TroopScanType.Building => hit.GetComponentInParent<Building>(),
                TroopScanType.Troop => hit.GetComponentInParent<Troop>(),
                TroopScanType.TroopGroup => hit.GetComponentInParent<Troop>()?.Group,
                TroopScanType.Coria => hit.GetComponentInParent<Corium>(),
                TroopScanType.Scrap => hit.GetComponentInParent<Scrap>(),
                _ => null
            };
            if (component is T match && seen.Add(match)) found.Add(match);
        }
        return found;
    }

    public virtual List<Building> ScanBuildings() => Scan<Building>(TroopScanType.Building);
    public virtual List<Troop> ScanTroops() => Scan<Troop>(TroopScanType.Troop);
    public virtual List<TroopGroup> ScanTroopGroups()
    {
        var groups = new HashSet<TroopGroup>();
        foreach (Troop troop in ScanTroops()) if (troop != null && troop.Group != null) groups.Add(troop.Group);
        return new List<TroopGroup>(groups);
    }
    public virtual List<Corium> ScanCoria() => Scan<Corium>(TroopScanType.Coria);

    public virtual void OnSelectBuilding(Building building)
    {
        Debug.Log("onselectbuilding");
    }

    public virtual void OnSelectTroop(Troop troop)
    {
        Debug.Log("onselecttroop");
    }

    public virtual void OnSelectResource(Resource resource)
    {
        Debug.Log("onselectresource");
    }

    public override void OnNetworkSpawn()
    {

        currentVisibility.gameObject.transform.localScale =
            new Vector3(SightRange/2, SightRange/2, SightRange/2);
        if (!IsOwner)
        {
            Destroy(currentVisibility);
        }
        if (IsServer)
        {
            Health.Value = MaxHealth;
            if(scrapDropped <= 0)
            {
                scrapDropped = (int)((float) Cost * 0.5f);
            }
        }

        // Boost půjde na klientovi i server zvlášť, protože se stejně pravděpodobně bude kontrolovat vzdálenost při výstřelu rychlost atd
        // takže i kdyby si klient dal upgrade svůj tak by nefungoval
        NetworkManager.Singleton.ConnectedClients[OwnerClientId]
                .PlayerObject.GetComponent<TechResolver>().AddBoost(this);
            

        IsOperational = true;

        if (IsOwner || IsServer)
        {
            TroopInteractor interactor =
                NetworkManager.Singleton.ConnectedClients[OwnerClientId]
                .PlayerObject.GetComponent<TroopInteractor>();

            if (interactor != null)
                interactor.RegisterTroop(this, Group);
            else
                Debug.LogWarning(
                    $"[Troop-DIAG] {name} nenašel TroopInteractor na vlastním PlayerObjectu.");
        }

        statusBar = GetComponent<StatusBar>();

        statusBar.SetValue(Health.Value, MaxHealth);
        Health.OnValueChanged += OnHealthChanged;

        OnSpawned();

        troopMaterial = GetComponent<TroopMaterial>();
        Agent = GetComponent<NavMeshAgent>();
        Agent.enabled = true;
        Agent.isStopped = combatMovementPaused;
        if(GetComponent<GenericAttacker>() != null)
        {
            Agent.stoppingDistance = (AttackRange - 0.1f) / 2.0f;
        }
        else
        {
            Agent.stoppingDistance = stoppingDistance;
        }
        

        if (damageHitRelay == null)
            damageHitRelay = GetComponent<DamageHitRelay>();

        if (walkAudioSource != null)
        {
            walkAudioSource.loop = true;
            walkAudioSource.Stop();
        }

        if (!Agent.isOnNavMesh)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} NENÍ na NavMeshi hned po spawnu!");
        }

        if (IsServer)
            Agent.avoidancePriority = Random.Range(30, 70);

        if (IsOwner)
            nextPositionSync = Time.time;

        Debug.Log(
            $"[Troop-DIAG] {name} OnNetworkSpawn | IsServer={IsServer} IsOwner={IsOwner} " +
            $"| pozice={transform.position} | Agent.isOnNavMesh={Agent.isOnNavMesh} " +
            $"| Agent.enabled={Agent.enabled}");

        if (!Agent.isOnNavMesh)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} NENÍ na NavMeshi hned po spawnu! " +
                $"Pozice {transform.position} pravděpodobně neodpovídá NavMeshi na tomto klientovi.");
        }
    }

    public override void OnNetworkDespawn()
    {
        IsOperational = false;
        Health.OnValueChanged -= OnHealthChanged;
        if (Group != null) Group.RemoveUnit(this);

        if (IsOwner &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null ||
            IsServer)
        {
            TroopInteractor interactor =
                NetworkManager.Singleton.LocalClient.PlayerObject
                .GetComponent<TroopInteractor>();

            if (interactor != null)
                interactor.UnregisterTroop(this);
        }
        if(IsServer)
        {
            Building.SpawnScrap(scrapPrefab, transform.position, transform.rotation, OwnerClientId, scrapDropped);
        }
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        statusBar.SetValue(newValue, MaxHealth);
    }
    
/*    private void ScanBuildings()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            buildingDetectionRadius,
            buildingResults,
            buildingLayerMask);

        HashSet<Building> currentBuildings = new();

        for (int i = 0; i < count; i++)
        {
            if (buildingResults[i] == null)
                continue;

            Building building =
                buildingResults[i].GetComponentInParent<Building>();

            if (building == null)
                continue;

            currentBuildings.Add(building);

            if (!buildingsInRange.Contains(building))
            {
                buildingsInRange.Add(building);
                Debug.Log(
                    $"[Troop] {name} detekuje budovu {building.name} v dosahu.");
            }
        }

        for (int i = buildingsInRange.Count - 1; i >= 0; i--)
        {
            Building building = buildingsInRange[i];

            if (building == null || !currentBuildings.Contains(building))
            {
                if (building != null)
                {
                    Debug.Log(
                        $"[Troop] {name} ztrácí budovu {building.name} z dosahu.");
                }

                buildingsInRange.RemoveAt(i);
            }
        }
    }*/

    public virtual void OnSpawned() { }

    public virtual void UpdateTroop() { }

    public virtual void RepairBuilding(Building building) { }

    public virtual void BuildBuilding(Building building) { }

    public void RequestMove(
        Vector3 destination,
        int groupIndex = 0,
        int groupSize = 1)
    {
        if (IsServer)
        {
            MoveTo(ApplyFormationOffset(destination, groupIndex, groupSize));
            return;
        }
        if (!IsOwner)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} RequestMove voláno, ale IsOwner=false. Ignoruji.");
            return;
        }

        if (!IsOperational)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} RequestMove voláno, ale IsOperational=false. Ignoruji.");
            return;
        }

        Debug.Log(
            $"[Troop-DIAG] {name} RequestMove -> posílám ServerRpc, cíl={destination}");

        RequestMoveServerRpc(destination, groupIndex, groupSize);
    }

    public virtual void MoveTo(Vector3 destination)
    {
        if (!IsOperational || !IsServer)
        {
            Debug.Log(
                $"[Troop-DIAG] {name} MoveTo zamítnuto | " +
                $"IsOperational={IsOperational} IsServer={IsServer}");
            return;
        }

        bool sampled = NavMesh.SamplePosition(
            destination,
            out NavMeshHit hit,
            navMeshSampleRadius,
            NavMesh.AllAreas);

        if (!sampled)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} NavMesh.SamplePosition SELHALO " +
                $"pro cíl {destination}");
            return;
        }

        Agent.isStopped = false;

        Debug.Log(
            $"[Troop-DIAG] {name} [SERVER] SetDestination -> " +
            $"vstup={destination} vysamplovano={hit.position}");

        Agent.SetDestination(hit.position);

        MoveClientRpc(hit.position);
    }

    [ClientRpc]
    private void MoveClientRpc(Vector3 destination)
    {
        if (IsServer)
            return;

        if (!Agent.enabled)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} [CLIENT] Agent nebyl enabled, zapínám znovu.");

            Agent.enabled = true;
        }

        bool sampled = NavMesh.SamplePosition(
            destination,
            out NavMeshHit hit,
            navMeshSampleRadius,
            NavMesh.AllAreas);

        if (sampled)
        {
            Debug.Log(
                $"[Troop-DIAG] {name} [CLIENT] SetDestination -> " +
                $"od serveru přišlo={destination} lokálně={hit.position}");

            Agent.SetDestination(hit.position);
        }
        else
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} [CLIENT] NavMesh.SamplePosition SELHALO " +
                $"pro {destination}.");
        }
    }

    // ---------------------------------------------------------
    // Klienti pravidelně posílají pozici serveru ke kontrole.
    // ---------------------------------------------------------

    private void SendPositionSync()
    {
        if (IsServer || !IsOperational)
            return;

        CheckPositionServerRpc(transform.position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void CheckPositionServerRpc(Vector3 clientPosition, ServerRpcParams rpcParams = default)
    {
        if (!IsOperational || !NetworkManager.ConnectedClients.ContainsKey(rpcParams.Receive.SenderClientId))
            return;

        if (Vector3.Distance(clientPosition, transform.position) <= positionSyncTolerance)
        {
            return;
        }
        Debug.Log($"Position Sync {Vector3.Distance(clientPosition, transform.position)}");
        CorrectPositionClientRpc(transform.position);
    }

    [ClientRpc]
    private void CorrectPositionClientRpc(Vector3 serverPosition)
    {
        if (IsServer || Agent == null)
            return;

        transform.position = serverPosition;
    }

    [ServerRpc]
    public void RequestMoveServerRpc(
        Vector3 destination,
        int groupIndex = 0,
        int groupSize = 1,
        ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} RequestMoveServerRpc zamítnuto - " +
                $"sender {rpcParams.Receive.SenderClientId} není owner ({OwnerClientId}).");

            return;
        }

        if (!IsOperational)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} RequestMoveServerRpc zamítnuto - " +
                $"IsOperational=false.");

            return;
        }

        if (Mathf.Abs(destination.x) > mapaHranice ||
            Mathf.Abs(destination.z) > mapaHranice)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} RequestMoveServerRpc zamítnuto - " +
                $"cíl {destination} mimo mapaHranice ({mapaHranice}).");

            return;
        }

        Vector3 finalDestination =
            ApplyFormationOffset(destination, groupIndex, groupSize);

        if (groundLayerMask.value != 0 &&
            !JeNaValidniZemi(finalDestination))
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} RequestMoveServerRpc zamítnuto - " +
                $"JeNaValidniZemi selhalo pro {finalDestination}.");

            return;
        }

        Debug.Log(
            $"[Troop-DIAG] {name} RequestMoveServerRpc OK, " +
            $"volám MoveTo({finalDestination})");

        MoveTo(finalDestination);
    }

    private Vector3 ApplyFormationOffset(
        Vector3 center,
        int index,
        int groupSize)
    {
        if (groupSize <= 1)
            return center;

        int columns = Mathf.CeilToInt(Mathf.Sqrt(groupSize));
        int row = index / columns;
        int col = index % columns;

        float offsetX =
            (col - columns / 2f) * formationSpacing;

        float offsetZ =
            (row - columns / 2f) * formationSpacing;

        return center + new Vector3(offsetX, 0f, offsetZ);
    }

    private bool JeNaValidniZemi(Vector3 point)
    {
        bool hitSomething = Physics.Raycast(
            point + Vector3.up * 5f,
            Vector3.down,
            out RaycastHit hit,
            100f,
            groundLayerMask);

        if (!hitSomething)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] JeNaValidniZemi - raycast z " +
                $"{point + Vector3.up * 5f} dolů NETREFIL nic.");
        }
        else
        {
            Debug.Log(
                $"[Troop-DIAG] JeNaValidniZemi - raycast trefil " +
                $"'{hit.collider.name}' na výšce {hit.point.y:F2}.");
        }

        return hitSomething;
    }

    public virtual void TakeDamage(int damage)
    {
        TakeDamage(damage, DamageType.BigAutomaticGun);
    }

    public virtual void TakeDamage(int damage, DamageType damageType)
    {
        if (!IsServer || !IsOperational || damage <= 0 || Health.Value <= 0)
            return;

        if (damageHitRelay != null)
            damageHitRelay.PlayHit(damageType);

        if ((Health.Value - damage) <= 0)
            Die();
        else
            Health.Value -= damage;
    }

    protected virtual void Die()
    {
        if (!IsServer)
            return;

        IsOperational = false;
        Health.Value = 0;
        GetComponent<NetworkObject>().Despawn();
    }

    protected virtual void ScanSurroundingsServer()
    {
        // Odhalení nepřátelských budov podle SightRange TÉTO jednotky.
        // buildingsInRange se plní přes ScanBuildings(), který hledá právě
        // v dosahu SightRange jednotky, takže každá nepřátelská budova v seznamu
        // je v dosahu mé jednotky a může se odhalit.
        if (buildingsInRange.Count > 0)
        {
            foreach (Building building in buildingsInRange)
            {
                if (building == null || building.OwnerClientId == OwnerClientId || building.Health.Value <= 0)
                {
                    continue;
                }

                Debug.Log($"[Troop] Budova {building.Type} je viditelná");
                building.ChangeVisibilityClientRpc(true);
            }
        }

        List<Troop> nearbyTroops = ScanTroops();
        if(isVisible.Value == false)
        {
            ChangeVisibilityClientRpc(false);
        }
        if (nearbyTroops.Count > 0)
        {
            foreach (Troop troop in nearbyTroops)
            {
                if (troop == null || troop.OwnerClientId == OwnerClientId || troop.Health.Value <= 0)
                    {
                        isVisible.Value = false;
                        continue;
                    }

                // pokud ji nevlastním tak mě vidí
                if(Vector3.Distance(transform.position, troop.transform.position) <= troop.SightRange)
                {
                    isVisible.Value = true;
                    Debug.Log($"[Troop] {Type} je viditelná");
                    ChangeVisibilityClientRpc(true);
                    return;
                }
                
            }
        }

        if (buildingsInRange.Count > 0)
        {
            foreach (Building building in buildingsInRange)
            {
                if (building == null || building.OwnerClientId == OwnerClientId || building.Health.Value <= 0)
                    {
                        isVisible.Value = false;
                        continue;
                    }

                // pokud ji nevlastním tak mě vidí
                if(Vector3.Distance(transform.position, building.transform.position) <= building.SightRange)
                {
                    isVisible.Value = true;
                    Debug.Log($"[Troop] {Type} je viditelná");
                    ChangeVisibilityClientRpc(true);
                    return;
                }
                
            }
        }

        
    }

    [ClientRpc]
    protected virtual void ChangeVisibilityClientRpc(bool IsVisible)
    {
        if(IsOwner)
            return;

        statusBar.pivot.gameObject.SetActive(IsVisible);
        troopMaterial.model.SetActive(IsVisible);
        
        
    }
    private void Update()
    {
        if (!IsOperational)
            return;
        // -----------------------------------------------------
        // Klient -> server kontrola pozice
        // -----------------------------------------------------

        if (!IsServer && Time.time >= nextPositionSync)
        {
            nextPositionSync = Time.time + positionSyncInterval;
            SendPositionSync();
        }

        // Klient-side detekce pohybu
        UpdateMovementState();

        if (Time.time >= nextBuildingScan)
        {
            nextBuildingScan =
                Time.time + buildingDetectionInterval;

            buildingsInRange = ScanBuildings();
            if(IsServer)
            {
                ScanSurroundingsServer();
            }

        }

        frameCountSinceUpdate++;

        if (framePerUpdate - frameCountSinceUpdate <= 0)
        {
            UpdateTroop();
            frameCountSinceUpdate = 0;
        }
    }

    public void SetWalking(bool walking)
    {
        if(Animator == null)
            return;
        Animator.SetBool(IsWalkingHash, walking);
    }

    /// <summary>
    /// Pauses/resumes the current NavMesh path while this troop is engaged in combat.
    /// Setting isStopped preserves the path so the agent continues toward its existing
    /// destination when combat ends.
    /// </summary>
    public void SetCombatMovementPaused(bool paused)
    {
        combatMovementPaused = paused;

        if (Agent != null && Agent.enabled && Agent.isOnNavMesh)
            {Agent.isStopped = paused;}
        else
        {
            Debug.Log($"[Troop] Jednotka nemůže zastavit, agent není v funkčním stavu");
        }
    }

    private void UpdateMovementState()
    {
        bool walking = Agent != null &&
                       Agent.enabled &&
                       Agent.isOnNavMesh &&
                       Agent.velocity.sqrMagnitude > movingVelocityThreshold;

        if (walking == isMoving)
            return;

        isMoving = walking;
        SetWalking(walking);

        if (walkAudioSource != null)
        {
            if (walking)
            {
                if (!walkAudioSource.isPlaying)
                    walkAudioSource.Play();
            }
            else
            {
                walkAudioSource.Stop();
            }
        }

        OnMovingStateChanged(isMoving);
        
    }

    protected virtual void OnMovingStateChanged(bool isMoving) { }

    public bool IsNearEnough(Vector3 first, Vector3 second)
    {
        return Vector3.Distance(first, second) <
               SightRange + Kompenzace;
    }

    public bool hasTarget()
    {
        return GetComponent<GenericAttacker>()?.CurrentTarget != null;
    }

    public bool IsNearEnough(Vector3 first, Building second)
    {
        return Vector3.Distance(
            first,
            second.GameObject().transform.position) <
            SightRange + Kompenzace;
    }

    public bool IsNearEnough(Vector3 first, Troop second)
    {
        return Vector3.Distance(
            first,
            second.GameObject().transform.position) <
            SightRange + Kompenzace;
    }

    public bool IsNearEnough(Troop first, Vector3 second)
    {
        return Vector3.Distance(
            second,
            first.GameObject().transform.position) <
            SightRange + Kompenzace;
    }

    public bool IsNearEnough(Building first, Vector3 second)
    {
        return Vector3.Distance(
            second,
            first.GameObject().transform.position) <
            SightRange + Kompenzace;
    }

    public bool IsNearEnough(
        Building first,
        Building second)
    {
        return Vector3.Distance(
            second.GameObject().transform.position,
            first.GameObject().transform.position) <
            SightRange + Kompenzace;
    }

    public bool IsNearEnough(
        Troop first,
        Troop second)
    {
        return Vector3.Distance(
            second.GameObject().transform.position,
            first.GameObject().transform.position) <
            SightRange + Kompenzace;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;

        Gizmos.DrawWireSphere(
            transform.position,
            SightRange);

        Gizmos.color = Color.red;

        Gizmos.DrawWireSphere(
            transform.position,
            AttackRange);
    }
#endif
}