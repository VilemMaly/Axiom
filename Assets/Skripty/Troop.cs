using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Unity.VisualScripting;

// Zatím jen Atlas, další typy (bojové jednotky apod.) se přidají sem časem -
// stejný princip jako BuildingType u budov.
public enum TroopType
{
    Atlas,
    Photon,
    Harvester,
}

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(StatusBar))]
public abstract class Troop : NetworkBehaviour
{
    [Header("Typ")]
    public TroopType Type;

    [Header("Stats")]
    public int MaxHealth;
    public int Cost;
    public float SightRange;
    protected bool IsBusy;

    [Header("Pohyb - NavMesh")]
    [SerializeField] private SpriteRenderer currentVisibility;
    [SerializeField] private LayerMask groundLayerMask;
    private float mapaHranice = 300f;
    [SerializeField] private float navMeshSampleRadius = 3f;
    [SerializeField] private float formationSpacing = 1.5f;

    [Header("Synchronizace pozice")]
    [SerializeField] private float positionSyncInterval = 1f;

    [Header("Detekce budov")]
    [SerializeField] private LayerMask buildingLayerMask;
    [SerializeField] private float buildingDetectionRadius = 5f;
    [SerializeField] private float buildingDetectionInterval = 0.5f;

    [Header("Bojové efekty - hit")]
    [Tooltip("Particle system, který se přehraje, když jednotka utrží damage (child prefab pod troopem).")]
    [SerializeField] protected ParticleSystem hitParticle;
    [SerializeField] protected AudioClip hitSfx;

    protected AudioSource audioSource;

    [Header("Pohyb - klient-side detekce chůze")]
    [Tooltip("Nad touto rychlostí Agenta se jednotka považuje za 'pohybující se' (pro walk animaci).")]
    [SerializeField] private float movingVelocityThreshold = 0.05f;

    [Tooltip("Jméno stavu (State) ve vlastním Animator Controlleru tohoto troopa, které se přehraje při chůzi.")]
    [SerializeField] private string walkAnimationStateName = "Walk";

    [Tooltip("Nepovinné - jméno stavu, na které se přepne po zastavení.")]
    [SerializeField] private string idleAnimationStateName;

    private bool isMoving;

    public bool IsMoving => isMoving;

    protected Animator Animator { get; private set; }

    public int framePerUpdate = 10;

    private int frameCountSinceUpdate = 0;

    public IReadOnlyList<Building> BuildingsInRange => buildingsInRange;

    private readonly List<Building> buildingsInRange = new();
    private readonly Collider[] buildingResults = new Collider[64];
    private float nextBuildingScan;

    public int Kompenzace = 1;

    public int MaxTroopAmount;

    public NetworkVariable<int> Health = new NetworkVariable<int>(
        writePerm: NetworkVariableWritePermission.Server);

    protected bool IsOperational { get; private set; } = false;

    protected NavMeshAgent Agent { get; private set; }

    private StatusBar statusBar;

    // ---------------------------------------------------------
    // Synchronizace pozice
    // ---------------------------------------------------------

    private float nextPositionSync;

    public virtual void StopCurrentTask()
    {
    }

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
        if (!IsOwner)
        {
            currentVisibility.GameObject().SetActive(false);
        }

        currentVisibility.GameObject().transform.localScale =
            new Vector3(SightRange, SightRange, 1f);

        if (IsServer)
            Health.Value = MaxHealth;

        IsOperational = true;

        if (IsOwner || IsServer)
        {
            TroopInteractor interactor =
                NetworkManager.Singleton.ConnectedClients[OwnerClientId]
                .PlayerObject.GetComponent<TroopInteractor>();

            if (interactor != null)
                interactor.RegisterTroop(this);
            else
                Debug.LogWarning(
                    $"[Troop-DIAG] {name} nenašel TroopInteractor na vlastním PlayerObjectu.");
        }

        statusBar = GetComponent<StatusBar>();

        statusBar.SetValue(Health.Value, MaxHealth);
        Health.OnValueChanged += OnHealthChanged;

        OnSpawned();

        Agent = GetComponent<NavMeshAgent>();
        Agent.enabled = true;

        audioSource = GetComponent<AudioSource>();
        Animator = GetComponent<Animator>();

        if (!Agent.isOnNavMesh)
        {
            Debug.LogWarning(
                $"[Troop-DIAG] {name} NENÍ na NavMeshi hned po spawnu!");
        }

        if (IsServer)
        {
            Agent.avoidancePriority = Random.Range(30, 70);

            // První synchronizace bude okamžitě po spawnu.
            nextPositionSync = Time.time;
        }

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

        if (IsOwner &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            TroopInteractor interactor =
                NetworkManager.Singleton.LocalClient.PlayerObject
                .GetComponent<TroopInteractor>();

            if (interactor != null)
                interactor.UnregisterTroop(this);
        }
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        statusBar.SetValue(newValue, MaxHealth);

        if (newValue < previousValue)
            PlayHitEffect();
    }

    private void PlayHitEffect()
    {
        if (hitParticle != null)
            hitParticle.Play();

        if (audioSource != null && hitSfx != null)
            audioSource.PlayOneShot(hitSfx);
    }

    private void ScanBuildings()
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
    }

    public virtual void OnSpawned() { }

    public virtual void UpdateTroop() { }

    public virtual void RepairBuilding(Building building) { }

    public virtual void BuildBuilding(Building building) { }

    public void RequestMove(
        Vector3 destination,
        int groupIndex = 0,
        int groupSize = 1)
    {
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
    // NOVÉ: server každou sekundu pošle správnou pozici
    // ---------------------------------------------------------

    private void SendPositionSync()
    {
        if (!IsServer || !IsOperational)
            return;

        SyncPositionClientRpc(transform.position);
    }

    [ClientRpc]
    private void SyncPositionClientRpc(Vector3 serverPosition)
    {
        // Host/server už má správnou autoritativní pozici.
        if (IsServer)
            return;

        if (Agent == null)
            return;

        // Když lokální NavMesh nesedí se serverem,
        // nastavíme alespoň přesnou serverovou pozici.
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
        if (!IsServer || !IsOperational)
            return;

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

    private void Update()
    {
        if (!IsOperational)
            return;

        // -----------------------------------------------------
        // Server -> klient synchronizace pozice
        // -----------------------------------------------------

        if (IsServer && Time.time >= nextPositionSync)
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

            ScanBuildings();
        }

        frameCountSinceUpdate++;

        if (framePerUpdate - frameCountSinceUpdate <= 0)
        {
            UpdateTroop();
            frameCountSinceUpdate = 0;
        }
    }

    private void UpdateMovementState()
    {
        if (Agent == null ||
            !Agent.enabled ||
            !Agent.isOnNavMesh)
            return;

        bool moving =
            Agent.velocity.sqrMagnitude >
            movingVelocityThreshold * movingVelocityThreshold;

        if (moving != isMoving)
        {
            isMoving = moving;

            if (Animator != null)
            {
                if (isMoving &&
                    !string.IsNullOrEmpty(walkAnimationStateName))
                {
                    Animator.Play(walkAnimationStateName);
                }
                else if (!isMoving &&
                         !string.IsNullOrEmpty(idleAnimationStateName))
                {
                    Animator.Play(idleAnimationStateName);
                }
            }

            OnMovingStateChanged(isMoving);
        }
    }

    protected virtual void OnMovingStateChanged(bool isMoving) { }

    public bool IsNearEnough(Vector3 first, Vector3 second)
    {
        return Vector3.Distance(first, second) <
               SightRange + Kompenzace;
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
            buildingDetectionRadius);
    }
#endif
}