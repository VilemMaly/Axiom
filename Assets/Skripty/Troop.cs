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
    private float mapaHranice = 300f; // hranice mapy, mimo kterou se jednotka nesmí pohybovat
    [SerializeField] private float navMeshSampleRadius = 3f;
    [SerializeField] private float formationSpacing = 1.5f;

    [Header("Detekce budov")]
    [SerializeField] private LayerMask buildingLayerMask;
    [SerializeField] private float buildingDetectionRadius = 5f;
    [SerializeField] private float buildingDetectionInterval = 0.5f;

    [Header("Bojové efekty - hit")]
    [Tooltip("Particle system, který se přehraje, když jednotka utrží damage (child prefab pod troopem).")]
    [SerializeField] protected ParticleSystem hitParticle;
    [SerializeField] protected AudioClip hitSfx;

    // Required komponenta - AudioSource si troop najde sám vedle sebe (viz OnNetworkSpawn),
    // takže se nemusí ručně tahat do inspectoru. Sdílený i pro potomky (Photon si sem přehraje střelbu).
    protected AudioSource audioSource;

    [Header("Pohyb - klient-side detekce chůze")]
    [Tooltip("Nad touto rychlostí Agenta se jednotka považuje za 'pohybující se' (pro walk animaci).")]
    [SerializeField] private float movingVelocityThreshold = 0.05f;
    [Tooltip("Jméno stavu (State) ve vlastním Animator Controlleru tohoto troopa, které se přehraje při chůzi. Každý troop si sem dá svoje.")]
    [SerializeField] private string walkAnimationStateName = "Walk";
    [Tooltip("Nepovinné - jméno stavu, na které se přepne po zastavení. Necháš prázdné, pokud si přechod zpět řešíš přechody přímo v Animator Controlleru.")]
    [SerializeField] private string idleAnimationStateName;

    private bool isMoving;

    // Klient-side stav, jestli se jednotka aktuálně pohybuje - pro navázání walk animace
    // (framy podle transform pozice/rotace si řešíš mimo tenhle skript, tady jen dostaneš signál).
    public bool IsMoving => isMoving;

    // Required komponenta - Animator si troop najde sám vedle sebe. Každý troop prefab má
    // svůj vlastní Animator Controller s vlastním walk clipem pod stavem walkAnimationStateName.
    protected Animator Animator { get; private set; }

    public int framePerUpdate = 10;

    private int frameCountSinceUpdate = 0;

    public IReadOnlyList<Building> BuildingsInRange => buildingsInRange;

    private readonly List<Building> buildingsInRange = new();
    private readonly Collider[] buildingResults = new Collider[64];
    private float nextBuildingScan;
    public int Kompenzace = 1;
    // Maximální počet co může hráč vyrobit
    public int MaxTroopAmount;

    public NetworkVariable<int> Health = new NetworkVariable<int>(
        writePerm: NetworkVariableWritePermission.Server);

    protected bool IsOperational { get; private set; } = false;

    protected NavMeshAgent Agent { get; private set; }

    // HP bar - obecná komponenta StatusBar sedící vedle Troopu na stejném GameObjectu
    // (viz RequireComponent výše). Troop jí jen posílá aktuální Health/MaxHealth,
    // vykreslení a billboard řeší StatusBar sám.
    private StatusBar statusBar;

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
        currentVisibility.GameObject().transform.localScale = new Vector3(SightRange, SightRange, 1f);

        // POZOR: byl tu bug - tohle běželo na VŠECH klientech bez ohledu na write permission
        if (IsServer)
            Health.Value = MaxHealth;

        IsOperational = true;

        // Zaregistrovat se do seznamu vlastních troopů na TroopInteractoru hráče -
        // dělá to jen vlastník (na serveru i cizích klientech nemá smysl, protože
        // ten seznam čte jen vlastní TroopInteractor pro UI příkazy jako Build()).
        if (IsOwner || IsServer)
        {
            TroopInteractor interactor = NetworkManager.Singleton.ConnectedClients[OwnerClientId].PlayerObject.GetComponent<TroopInteractor>();
            if (interactor != null)
                interactor.RegisterTroop(this);
            else
                Debug.LogWarning($"[Troop-DIAG] {name} nenašel TroopInteractor na vlastním PlayerObjectu.");
        }

        statusBar = GetComponent<StatusBar>();

        // HP bar: nastavit počáteční stav hned (OnValueChanged se nemusí spolehlivě
        // vyvolat pro pozdě-joinující klienty, protože jen syncuje počáteční hodnotu),
        // a dál se přihlásit na změny, aby se bar aktualizoval jen když se HP skutečně mění.
        statusBar.SetValue(Health.Value, MaxHealth);
        Health.OnValueChanged += OnHealthChanged;

        OnSpawned();

                Agent = GetComponent<NavMeshAgent>();
        Agent.enabled = true;

        audioSource = GetComponent<AudioSource>();
        Animator = GetComponent<Animator>();
        
        if (!Agent.isOnNavMesh)
        {
            Debug.LogWarning($"... NENÍ na NavMeshi hned po spawnu! ...");
        }

        if (IsServer)
            Agent.avoidancePriority = Random.Range(30, 70);

        // DIAGNOSTIKA: klíčové pro bug "spawnuje se pod zemí na joinujícím klientovi"
        Debug.Log($"[Troop-DIAG] {name} OnNetworkSpawn | IsServer={IsServer} IsOwner={IsOwner} " +
                  $"| pozice={transform.position} | Agent.isOnNavMesh={Agent.isOnNavMesh} " +
                  $"| Agent.enabled={Agent.enabled}");

        if (!Agent.isOnNavMesh)
        {
            Debug.LogWarning($"[Troop-DIAG] {name} NENÍ na NavMeshi hned po spawnu! " +
                              $"Pozice {transform.position} pravděpodobně neodpovídá NavMeshi na tomto klientovi " +
                              $"(rozdílný bake / starý ground objekt / terén se neshoduje).");
        }
    }

    public override void OnNetworkDespawn()
    {
        IsOperational = false;
        Health.OnValueChanged -= OnHealthChanged;

        if (IsOwner && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null
            && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            TroopInteractor interactor = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<TroopInteractor>();
            if (interactor != null)
                interactor.UnregisterTroop(this);
        }
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        statusBar.SetValue(newValue, MaxHealth);

        // OnValueChanged se volá na serveru i na všech klientech (sync), takže tohle
        // je automaticky "klientský" efekt bez nutnosti ClientRpc. Přehrát jen při
        // poklesu HP (ne při léčení/setu), aby se hit efekt nespustil omylem.
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

            Building building = buildingResults[i].GetComponentInParent<Building>();

            if (building == null)
                continue;

            currentBuildings.Add(building);

            if (!buildingsInRange.Contains(building))
            {
                buildingsInRange.Add(building);
                Debug.Log($"[Troop] {name} detekuje budovu {building.name} v dosahu.");
            }
        }

        for (int i = buildingsInRange.Count - 1; i >= 0; i--)
        {
            Building building = buildingsInRange[i];

            if (building == null || !currentBuildings.Contains(building))
            {
                if (building != null)
                    Debug.Log($"[Troop] {name} ztrácí budovu {building.name} z dosahu.");

                buildingsInRange.RemoveAt(i);
            }
        }
    }

    public virtual void OnSpawned() { }

    public virtual void UpdateTroop() { }

    public virtual void RepairBuilding(Building building) { }

    public virtual void BuildBuilding(Building building) { }

    public void RequestMove(Vector3 destination, int groupIndex = 0, int groupSize = 1)
    {
        if (!IsOwner)
        {
            Debug.LogWarning($"[Troop-DIAG] {name} RequestMove voláno, ale IsOwner=false. Ignoruji.");
            return;
        }

        Debug.Log($"[Troop-DIAG] {name} RequestMove -> posílám ServerRpc, cíl={destination}");
        RequestMoveServerRpc(destination, groupIndex, groupSize);
    }

    public virtual void MoveTo(Vector3 destination)
    {
        if (!IsOperational || !IsServer)
        {
            Debug.Log($"[Troop-DIAG] {name} MoveTo zamítnuto | IsOperational={IsOperational} IsServer={IsServer}");
            return;
        }

        bool sampled = NavMesh.SamplePosition(destination, out NavMeshHit hit,
            navMeshSampleRadius,
            NavMesh.AllAreas);

        if (!sampled)
        {
            Debug.LogWarning($"[Troop-DIAG] {name} NavMesh.SamplePosition SELHALO pro cíl {destination} " +
                              $"v radiusu {navMeshSampleRadius}. Bod pravděpodobně příliš daleko od NavMeshe " +
                              $"nebo NavMesh na serveru neexistuje na tomhle místě.");
            return;
        }

        Debug.Log($"[Troop-DIAG] {name} [SERVER] SetDestination -> vstup={destination} vysamplovano={hit.position} " +
                  $"(rozdíl={Vector3.Distance(destination, hit.position):F2})");

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
            Debug.LogWarning($"[Troop-DIAG] {name} [CLIENT] Agent nebyl enabled, zapínám znovu.");
            Agent.enabled = true;
        }

        bool sampled = NavMesh.SamplePosition(destination, out NavMeshHit hit,
            navMeshSampleRadius,
            NavMesh.AllAreas);

        if (sampled)
        {
            Debug.Log($"[Troop-DIAG] {name} [CLIENT] SetDestination -> od serveru přišlo={destination} " +
                      $"lokálně vysamplováno={hit.position} (rozdíl={Vector3.Distance(destination, hit.position):F2}) " +
                      $"| aktuální pozice objektu={transform.position} " +
                      $"| Agent.isOnNavMesh={Agent.isOnNavMesh}");

            Agent.SetDestination(hit.position);
        }
        else
        {
            Debug.LogWarning($"[Troop-DIAG] {name} [CLIENT] NavMesh.SamplePosition SELHALO pro {destination}. " +
                              $"Klientův lokální NavMesh se pravděpodobně LIŠÍ od serverového " +
                              $"(jiný bake / neshoda terénu). Tohle je pravděpodobně tvůj hlavní bug.");
        }
    }

    [ServerRpc]
    public void RequestMoveServerRpc(Vector3 destination, int groupIndex = 0, int groupSize = 1, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Troop-DIAG] {name} RequestMoveServerRpc zamítnuto - sender {rpcParams.Receive.SenderClientId} " +
                              $"není owner ({OwnerClientId}).");
            return;
        }

        if (!IsOperational)
        {
            Debug.LogWarning($"[Troop-DIAG] {name} RequestMoveServerRpc zamítnuto - IsOperational=false.");
            return;
        }

        if (Mathf.Abs(destination.x) > mapaHranice || Mathf.Abs(destination.z) > mapaHranice)
        {
            Debug.LogWarning($"[Troop-DIAG] {name} RequestMoveServerRpc zamítnuto - cíl {destination} " +
                              $"mimo mapaHranice ({mapaHranice}).");
            return;
        }

        Vector3 finalDestination = ApplyFormationOffset(destination, groupIndex, groupSize);

        if (groundLayerMask.value != 0 && !JeNaValidniZemi(finalDestination))
        {
            Debug.LogWarning($"[Troop-DIAG] {name} RequestMoveServerRpc zamítnuto - JeNaValidniZemi selhalo " +
                              $"pro {finalDestination}. groundLayerMask={groundLayerMask.value} " +
                              $"(zkontroluj, jestli terén má nastavený tenhle layer!).");
            return;
        }

        Debug.Log($"[Troop-DIAG] {name} RequestMoveServerRpc OK, volám MoveTo({finalDestination})");
        MoveTo(finalDestination);
    }

    private Vector3 ApplyFormationOffset(Vector3 center, int index, int groupSize)
    {
        if (groupSize <= 1)
            return center;

        int columns = Mathf.CeilToInt(Mathf.Sqrt(groupSize));
        int row = index / columns;
        int col = index % columns;

        float offsetX = (col - columns / 2f) * formationSpacing;
        float offsetZ = (row - columns / 2f) * formationSpacing;

        return center + new Vector3(offsetX, 0f, offsetZ);
    }

    private bool JeNaValidniZemi(Vector3 point)
    {
        bool hitSomething = Physics.Raycast(point + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 100f, groundLayerMask);

        if (!hitSomething)
        {
            Debug.LogWarning($"[Troop-DIAG] JeNaValidniZemi - raycast z {point + Vector3.up * 5f} dolů NETREFIL nic " +
                              $"na layerMask={groundLayerMask.value}. Zkontroluj Layer terénu vs. groundLayerMask v inspectoru.");
        }
        else
        {
            Debug.Log($"[Troop-DIAG] JeNaValidniZemi - raycast trefil '{hit.collider.name}' " +
                      $"(layer={hit.collider.gameObject.layer}) na výšce {hit.point.y:F2}.");
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

        // Klient-side, čistě lokální čtení Agent.velocity (žádný server round-trip potřeba) -
        // běží na serveru i na všech klientech stejně, protože NavMeshAgent simuluje pohyb lokálně.
        UpdateMovementState();

        if (Time.time >= nextBuildingScan)
        {
            nextBuildingScan = Time.time + buildingDetectionInterval;
            ScanBuildings();
        }

        frameCountSinceUpdate++;

        if(framePerUpdate - frameCountSinceUpdate <= 0)
        {
            UpdateTroop();
            frameCountSinceUpdate = 0;
        }
        
    }

    private void UpdateMovementState()
    {
        if (Agent == null || !Agent.enabled || !Agent.isOnNavMesh)
            return;

        bool moving = Agent.velocity.sqrMagnitude > movingVelocityThreshold * movingVelocityThreshold;

        if (moving != isMoving)
        {
            isMoving = moving;

            if (Animator != null)
            {
                if (isMoving && !string.IsNullOrEmpty(walkAnimationStateName))
                    Animator.Play(walkAnimationStateName);
                else if (!isMoving && !string.IsNullOrEmpty(idleAnimationStateName))
                    Animator.Play(idleAnimationStateName);
            }

            OnMovingStateChanged(isMoving);
        }
    }

    // Override v potomkovi (nebo poslouchej public IsMoving z jiného skriptu), pokud potřebuješ
    // navázat ještě něco dalšího na změnu pohybu (walk animace přes Animator už se řeší výše).
    protected virtual void OnMovingStateChanged(bool isMoving) { }

    public bool IsNearEnough(Vector3 first, Vector3 second)
    {
        return Vector3.Distance(first,second) < SightRange + Kompenzace;
    }

    public bool IsNearEnough(Vector3 first, Building second)
    {
        return Vector3.Distance(first,second.GameObject().transform.position) < SightRange + Kompenzace;
    }

    public bool IsNearEnough(Vector3 first, Troop second)
    {
        return Vector3.Distance(first,second.GameObject().transform.position) < SightRange + Kompenzace;
    }

    public bool IsNearEnough(Troop first, Vector3 second)
    {
        return Vector3.Distance(second,first.GameObject().transform.position) < SightRange + Kompenzace;
    }
    public bool IsNearEnough(Building first, Vector3 second)
    {
        return Vector3.Distance(second,first.GameObject().transform.position) < SightRange + Kompenzace;
    }
    public bool IsNearEnough(Building first, Building second)
    {
        return Vector3.Distance(second.GameObject().transform.position,first.GameObject().transform.position) < SightRange + Kompenzace;
    }

    public bool IsNearEnough(Troop first, Troop second)
    {
        return Vector3.Distance(second.GameObject().transform.position,first.GameObject().transform.position) < SightRange + Kompenzace;
    }


#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, buildingDetectionRadius);
    }
#endif
}