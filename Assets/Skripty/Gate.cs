using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class Gate : Building
{
    public override BuildingType Type => BuildingType.Gate;

    [Header("Napojení na zdi a další brány")]
    [SerializeField] public Transform leftLock;
    [SerializeField] public Transform rightLock;

    [Header("Gate – detekce přátelských jednotek")]
    [Tooltip("Radius, ve kterém brána hledá přátelské jednotky pro otevření.")]
    [SerializeField] private float detectionRadius = 8f;

    [Tooltip("Layer maska pro detekci jednotek (troopů).")]
    [SerializeField] private LayerMask troopLayerMask;

    [Tooltip("Jak často (v sekundách) se skenuje okolí.")]
    [SerializeField] private float scanInterval = 0.3f;

    [Header("Gate – vizuál")]
    [Tooltip("Animator brány s bool parametrem GateClosed.")]
    [SerializeField] private Animator gateAnimator;

    [Tooltip("Collider brány, který se vypne/zapne při otevření/zavření.")]
    [SerializeField] private Collider gateCollider;
    [SerializeField] private NavMeshObstacle obstacle;

    private static readonly int GateClosedHash = Animator.StringToHash("GateClosed");

    /// <summary>
    /// Synchronizovaný stav brány – true = otevřená, false = zavřená.
    /// Server-only write; všichni klienti čtou.
    /// </summary>
    public NetworkVariable<bool> IsOpen = new(
        writePerm: NetworkVariableWritePermission.Server);

    private float nextScanTime;
    private readonly Collider[] scanResults = new Collider[32];

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Při změně stavu aktualizovat vizuál a collider na všech klientech
        IsOpen.OnValueChanged += OnGateStateChanged;

        // Nastavit počáteční stav (zavřená)
        ApplyGateStateClientRpc(IsOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        IsOpen.OnValueChanged -= OnGateStateChanged;
        base.OnNetworkDespawn();
    }

    public override void OnBuilt()
    {
        // Po dostavění je brána defaultně zavřená
        if (IsServer)
            IsOpen.Value = false;
    }

    /// <summary>
    /// Serverová logika – skenuje okolí a otevírá/zavírá bránu
    /// podle přítomnosti přátelských jednotek.
    /// Volá se z Building.Update() jen když IsConstructionComplete.
    /// </summary>
    public override void UpdateBuilding()
    {
        if (!IsServer) return;

        if (Time.time < nextScanTime) return;
        nextScanTime = Time.time + scanInterval;

        bool friendlyNearby = ScanForFriendlyTroops();

        // Změnit stav jen pokud se liší – NetworkVariable posílá update jen při změně
        if (IsOpen.Value != friendlyNearby)
            IsOpen.Value = friendlyNearby;
    }

    /// <summary>
    /// Hledá jednotky (Troop) v detectionRadius, které mají stejné OwnerClientId
    /// jako tato brána (= patří stejnému hráči v síti).
    /// </summary>
    private bool ScanForFriendlyTroops()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            detectionRadius,
            scanResults,
            troopLayerMask);

        for (int i = 0; i < count; i++)
        {
            if (scanResults[i] == null) continue;

            Troop troop = scanResults[i].GetComponentInParent<Troop>();
            if (troop == null) continue;

            // Kontrola vlastnictví – brána se otevírá jen pro jednotky
            // patřící stejnému hráči (OwnerClientId z Netcode)
            if (troop.OwnerClientId == OwnerClientId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Callback při změně NetworkVariable – běží na všech klientech.
    /// </summary>
    private void OnGateStateChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[Gate] {previousValue} -> {newValue} | Gate={gameObject.name}");
        ApplyGateStateClientRpc(newValue);
    }

    /// <summary>
    /// Aplikuje vizuální a fyzický stav brány.
    /// </summary>
    [ClientRpc]
    private void ApplyGateStateClientRpc(bool open)
    {
        // Collider – vypnout při otevření, aby jednotky prošly
        if (gateCollider != null)
            gateCollider.enabled = !open;
        obstacle.enabled = !open;
        if (gateAnimator != null)
            gateAnimator.SetBool(GateClosedHash, !open);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
#endif
}
