using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Mina – v pravidelném intervalu skenuje okolí a hledá nepřátelské Troopy a Budovy
/// (ty, které nevlastní stejný hráč jako mina). Jakmile něco najde, odpálí se zápalná
/// pojistka (fuseTime) a po jejím uplynutí zavolá Boom.Explode().
/// Veškerá logika běží POUZE na serveru (UpdateBuilding volá Building.Update jen na serveru).
/// Na prefab miny musí být přidaná i komponenta Boom (nastav v ní radius výbuchu a damage).
/// </summary>
[RequireComponent(typeof(Boom))]
public class Mine : Building
{
    public override BuildingType Type => BuildingType.Mine;

    [Header("Detekce nepřátel")]
    [Tooltip("Poloměr, ve kterém mina detekuje nepřátele (v editoru vidět jako žlutý gizmo).")]
    [SerializeField] private float detectionRadius = 5f;
    [Tooltip("Jak často (v sekundách) mina skenuje okolí.")]
    [SerializeField] private float scanInterval = 1f;
    [SerializeField] private LayerMask troopLayerMask;
    [SerializeField] private LayerMask buildingLayerMask;

    [Header("Výbuch")]
    [Tooltip("Za jak dlouho po detekci nepřítele mina exploduje.")]
    [SerializeField] private float fuseTime = 2f;
    [Tooltip("Pokud je zapnuto, mina se odpojí (zruší odpočet), když nepřátelé odejdou z radiusu před výbuchem.")]
    [SerializeField] private bool cancelFuseIfTargetsLeave = false;
    [SerializeField] private Boom boom;

    // Serverový stav
    private float nextScanTime;
    private bool fuseActive;
    private float explodeTime;
    private bool exploded;

    private readonly Collider[] scanResults = new Collider[64];

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (boom == null)
            boom = GetComponent<Boom>();

        nextScanTime = Time.time + scanInterval;
        // Pokud není owner, renderer miny se vypne aby se neukazovala cizím hráčům (nebo AI).
        if (!IsOwner)
        {
            GetComponent<Renderer>().enabled = false;
        }
    }

    /// <summary>
    /// Volá se z Building.Update() pouze na serveru a jen když je budova dostavěná a funkční.
    /// </summary>
    public override void UpdateBuilding()
    {
        if (exploded)
            return;

        // Odpočet pojistky běží každý frame, aby výbuch přišel přesně v čase
        // (ne zaokrouhlený na scanInterval).
        if (fuseActive && Time.time >= explodeTime)
        {
            Detonate();
            return;
        }

        if (Time.time < nextScanTime)
            return;

        nextScanTime = Time.time + scanInterval;

        bool enemyInRange = ScanForEnemies();

        if (enemyInRange)
        {
            if (!fuseActive)
            {
                fuseActive = true;
                explodeTime = Time.time + fuseTime;
                Debug.Log($"[Mine] Nepřítel v dosahu, výbuch za {fuseTime:F1}s.");
            }
        }
        else if (fuseActive && cancelFuseIfTargetsLeave)
        {
            fuseActive = false;
            Debug.Log("[Mine] Nepřátelé odešli, pojistka zrušena.");
        }
    }

    /// <summary>
    /// Koulový sken okolí. Vrací true, pokud je v radiusu aspoň jeden Troop nebo Building,
    /// který nepatří vlastníkovi miny.
    /// </summary>
    private bool ScanForEnemies()
    {
        // Troopy
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, detectionRadius, scanResults, troopLayerMask);

        for (int i = 0; i < count; i++)
        {
            if (scanResults[i] == null) continue;

            Troop troop = scanResults[i].GetComponentInParent<Troop>();
            if (troop != null && troop.OwnerClientId != OwnerClientId)
                return true;
        }

        // Budovy
        count = Physics.OverlapSphereNonAlloc(
            transform.position, detectionRadius, scanResults, buildingLayerMask);

        for (int i = 0; i < count; i++)
        {
            if (scanResults[i] == null) continue;

            Building building = scanResults[i].GetComponentInParent<Building>();
            if (building != null && building != this && building.OwnerClientId != OwnerClientId)
                return true;
        }

        return false;
    }

    private void Detonate()
    {
        // Flag nastavit PŘED výbuchem – chrání před opakovaným spuštěním
        // (např. domino efekt, kdy exploze poškodí tuhle minu).
        exploded = true;
        fuseActive = false;

        if (boom != null)
        {
            // Nepoškodí jednotky ani budovy hráče, který minu vlastní.
            boom.Explode(transform.position, OwnerClientId);
        }
        else
        {
            Debug.LogError("[Mine] Chybí komponenta Boom na prefabu!");
        }

        // Mina se po výbuchu odstraní (DestroyBuilding kontroluje IsSpawned,
        // takže je bezpečné i když už byla zničena dominem).
        DestroyBuilding();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Žlutá = detekční radius miny
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Červená = radius výbuchu (z komponenty Boom)
        Boom b = boom != null ? boom : GetComponent<Boom>();
        if (b != null)
            b.DrawGizmo(transform.position);
    }
#endif
}