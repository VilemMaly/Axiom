using Unity.Netcode;
using UnityEngine;

public class Gate : Building
{
    public override BuildingType Type => BuildingType.Gate;

    [Header("Gate – detekce přátelských jednotek")]
    [Tooltip("Radius, ve kterém brána hledá přátelské jednotky pro otevření.")]
    [SerializeField] private float detectionRadius = 8f;

    [Tooltip("Layer maska pro detekci jednotek (troopů).")]
    [SerializeField] private LayerMask troopLayerMask;

    [Tooltip("Jak často (v sekundách) se skenuje okolí.")]
    [SerializeField] private float scanInterval = 0.3f;

    [Header("Gate – vizuál")]
    [Tooltip("Animator na bráně – stavy 'Open' a 'Close' (nepovinné, pokud brána nemá animaci).")]
    [SerializeField] private Animator gateAnimator;

    [Tooltip("Collider brány, který se vypne/zapne při otevření/zavření.")]
    [SerializeField] private Collider gateCollider;

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
        ApplyGateState(IsOpen.Value);
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
        ApplyGateState(newValue);
    }

    /// <summary>
    /// Aplikuje vizuální a fyzický stav brány.
    /// </summary>
    private void ApplyGateState(bool open)
    {
        // Collider – vypnout při otevření, aby jednotky prošly
        if (gateCollider != null)
            gateCollider.enabled = !open;

        // Animator – přehrát odpovídající stav (pokud existuje)
        if (gateAnimator != null)
        {
            if (open)
                gateAnimator.Play("Open");
            else
                gateAnimator.Play("Close");
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
#endif
}
