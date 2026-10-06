using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.VisualScripting;
/// <summary>
/// Edge-scrolling + akční ovládání kamery (RTS styl) + zoom ortografické kamery.
/// Skript patří na PRÁZDNÝ PARENT objekt, který se pohybuje.
/// Hlavní kamera je jeho child a do pole "Cílová kamera" se přiřadí z editoru.
/// Akce se mapují přes Input Action Asset (přetáhni konkrétní akce do polí níže).
/// Vyžaduje balíček "Input System" a
/// Project Settings > Player > Active Input Handling = "Input System Package (New)".
/// </summary>
[DisallowMultipleComponent]
public class CameraController : NetworkBehaviour
{
    [Header("Reference")]
    [Tooltip("Hlavní kamera (child tohoto objektu). Používá se pro určení směru pohybu a pro zoom.")]
    [SerializeField] private Camera cilovaKamera;
    [SerializeField] private AudioListener audioListener;
    [SerializeField] private BuildingPlacementController buildingPlacementController;

    [Header("Nastavení okraje obrazovky")]
    [Tooltip("Kolik pixelů od okraje obrazovky spustí posun kamery.")]
    [SerializeField, Min(0f)] private float hranicePixelu = 20f;
    [Tooltip("Zapnout/vypnout posun kamery přes okraj obrazovky myší.")]
    [SerializeField] private bool povolitEdgeScroll = true;

    [Header("Rychlost pohybu")]
    [Tooltip("Maximální rychlost pohybu kamery.")]
    [SerializeField, Min(0f)] private float maxRychlost = 20f;
    [Tooltip("Jak rychle kamera zrychluje na maximální rychlost.")]
    [SerializeField, Min(0f)] private float akcelerace = 40f;
    [Tooltip("Jak rychle kamera zpomaluje na nulu, když není žádný vstup.")]
    [SerializeField, Min(0f)] private float deakcelerace = 60f;

    [Tooltip("Rychlost zoomu v jednotkách ortho size za sekundu (frame-rate nezávislé).")]
    [SerializeField, Min(0f)] private float rychlostZoomu = 10f;
    [Tooltip("Minimální velikost ortho size.")]
    [SerializeField, Min(0.01f)] private float minVelikost = 3f;
    [Tooltip("Maximální velikost ortho size.")]
    [SerializeField, Min(0.01f)] private float maxVelikost = 30f;
    [Tooltip("Plynulost přiblížení/oddálení (vyšší = rychlejší dojezd na cílovou hodnotu).")]
    [SerializeField, Min(0.0001f)] private float plynulostZoomu = 10f;

    [Header("Volitelné")]
    [Tooltip("Pokud kurzor opustí okno hry, edge-scroll se zastaví (akce fungují dál).")]
    [SerializeField] private bool zastavitMimoOkno = true;

    [Header("Výška nad terénem")]
    [Tooltip("Zapnout/vypnout automatické udržování výšky nad terénem.")]
    [SerializeField] private bool udrzovatVyskuNadZemi = true;
    [Tooltip("Požadovaná výška objektu (kamery) nad povrchem terénu.")]
    [SerializeField, Min(0f)] private float pozadovanaVyskaNadZemi = 15f;
    [Tooltip("Vrstva terénu použitá pro raycast zjišťující výšku země pod kamerou.")]
    [SerializeField] private LayerMask vrstvaTerenu;
    [Tooltip("Kolik snímků (frames) uplyne mezi jednotlivými raycasty pro zjištění výšky terénu. Nemusí se dělat každý frame, terén se nemění.")]
    [SerializeField, Min(1)] private int intervalRaycastuSnimky = 30;
    [Tooltip("Maximální vzdálenost raycastu směrem dolů pro detekci terénu.")]
    [SerializeField, Min(0f)] private float maxVzdalenostRaycastu = 500f;
    [Tooltip("Plynulost přesunu na cílovou výšku (vyšší = rychlejší dojezd, stejný princip jako u zoomu).")]
    [SerializeField, Min(0.0001f)] private float plynulostVysky = 5f;

    [Header("Hranice mapy")]
    [Tooltip("Omezit pohyb kamery na čtvercovou oblast kolem středu mapy.")]
    [SerializeField] private bool omezitHranice = true;
    [Tooltip("Střed mapy (world space). Používají se pouze složky X a Z, Y se ignoruje.")]
    [SerializeField] private Vector3 stredMapy = Vector3.zero;
    [Tooltip("Polovina délky strany čtverce – jak daleko od středu se kamera může dostat v každém směru (v jednotkách world space).")]
    [SerializeField, Min(0f)] private float polovinaStranyMapy = 50f;
    public bool povolitZoom;

    private float aktualniRychlost = 0f;
    private float cilovaVelikostZoomu;

    private int pocitadloSnimkuVysky = 0;
    private float cilovaVyskaY;
    private bool mameCilovouVysku = false;

    /// <summary>
    /// Statická reference na kameru lokálního hráče. Nastavuje ji sám vlastník
    /// v Start() (do statiky nelze přiřadit z inspectoru) a uklízí v OnDestroy().
    /// Ostatní skripty (např. Troop) ji používají místo drahého/nespolehlivého Camera.main.
    /// </summary>
    public static Camera LocalCamera { get; private set; }

    private void Reset()
    {
        cilovaKamera = GetComponentInChildren<Camera>();
    }

    private void Start()
    {
        if(!IsOwner)
        {
            Destroy(cilovaKamera.gameObject);
            enabled = false;
            return;
        }

        if (cilovaKamera != null && cilovaKamera.orthographic)
            cilovaVelikostZoomu = cilovaKamera.orthographicSize;

        if (udrzovatVyskuNadZemi)
            ZjistiVyskuTerenu();

        LocalCamera = cilovaKamera;
    }

    private void OnDestroy()
    {
        // Uklidit static referenci, ale jen pokud pořád ukazuje na tuhle instanci -
        // jinak bys mohl vynulovat kameru jinému (pozdě-joinujícímu) hráči.
        if (LocalCamera == cilovaKamera)
            LocalCamera = null;
    }

    private void Update()
    {
        if (cilovaKamera == null)
        {
            Debug.LogWarning("CameraController: Není přiřazená kamera v inspektoru.", this);
            return;
        }

        ZpracujPohyb();
        ZpracujZoom();
        ZpracujVyskuNadZemi();
    }

    private void ZpracujPohyb()
    {
        Vector2 smerEdge = povolitEdgeScroll ? ZjistiSmerOdOkraju() : Vector2.zero;
        Vector2 smerAkci = InputManager.StaticClass != null ? InputManager.StaticClass.Controls.Player.Move.ReadValue<Vector2>() : Vector2.zero;

        Vector2 smer2D = smerEdge + smerAkci;
        if (smer2D.sqrMagnitude > 1f)
            smer2D.Normalize();

        bool jeVstup = smer2D.sqrMagnitude > 0.0001f;
        float cilovaRychlost = jeVstup ? maxRychlost : 0f;
        float rychlostZmeny = jeVstup ? akcelerace : deakcelerace;

        // MoveTowards škáluje podle Time.deltaTime -> nezávislé na FPS
        aktualniRychlost = Mathf.MoveTowards(aktualniRychlost, cilovaRychlost, rychlostZmeny * Time.deltaTime);

        if (aktualniRychlost <= 0.0001f)
        {
            OmezPozici();
            return;
        }

        Transform kamTransform = cilovaKamera.transform;

        Vector3 dopredu = kamTransform.forward;
        dopredu.y = 0f;
        dopredu.Normalize();

        Vector3 doprava = kamTransform.right;
        doprava.y = 0f;
        doprava.Normalize();

        Vector3 smer3D = (doprava * smer2D.x + dopredu * smer2D.y).normalized;

        // Posun škálovaný Time.deltaTime -> stejná ujetá vzdálenost za sekundu bez ohledu na FPS
        transform.position += smer3D * aktualniRychlost * Time.deltaTime;

        OmezPozici();
    }

    /// <summary>
    /// Ořízne pozici parent objektu (kamery) na čtvercovou oblast kolem stredMapy.
    /// </summary>
    private void OmezPozici()
    {
        if (!omezitHranice)
            return;

        Vector3 pozice = transform.position;

        pozice.x = Mathf.Clamp(pozice.x, stredMapy.x - polovinaStranyMapy, stredMapy.x + polovinaStranyMapy);
        pozice.z = Mathf.Clamp(pozice.z, stredMapy.z - polovinaStranyMapy, stredMapy.z + polovinaStranyMapy);

        transform.position = pozice;
    }

    /// <summary>
    /// Každých <see cref="intervalRaycastuSnimky"/> snímků pošle raycast dolů a zjistí,
    /// v jaké výšce je terén. Mezi jednotlivými raycasty se objekt plynule (Lerp)
    /// přesouvá na naposledy zjištěnou cílovou výšku, takže to nevypadá trhaně,
    /// i když se výška terénu kontroluje jen občas.
    /// </summary>
    private void ZpracujVyskuNadZemi()
    {
        if (!udrzovatVyskuNadZemi)
            return;

        pocitadloSnimkuVysky++;
        if (pocitadloSnimkuVysky >= intervalRaycastuSnimky)
        {
            pocitadloSnimkuVysky = 0;
            ZjistiVyskuTerenu();
        }

        if (!mameCilovouVysku)
            return;

        Vector3 pozice = transform.position;
        pozice.y = Mathf.Lerp(pozice.y, cilovaVyskaY, 1f - Mathf.Exp(-plynulostVysky * Time.deltaTime));
        transform.position = pozice;
    }

    /// <summary>
    /// Pošle raycast směrem dolů z bodu vysoko nad aktuální pozicí (aby fungoval i po
    /// prudké změně terénu) a nastaví cilovaVyskaY na výšku zásahu + požadovaný odstup.
    /// Pokud raycast nic netrefí (mimo terén, díra v collideru...), poslední zjištěná
    /// výška se zachová, aby kamera nespadla do nekonečna.
    /// </summary>
    private void ZjistiVyskuTerenu()
    {
        Vector3 pocatekRaycastu = transform.position + Vector3.up * maxVzdalenostRaycastu * 0.5f;

        if (Physics.Raycast(pocatekRaycastu, Vector3.down, out RaycastHit zasah, maxVzdalenostRaycastu, vrstvaTerenu))
        {
            cilovaVyskaY = zasah.point.y + pozadovanaVyskaNadZemi;
            mameCilovouVysku = true;
        }
        else
        {
            Debug.LogWarning("CameraController: Raycast na terén nic netrefil, výška se nemění.", this);
        }
    }

    private Vector2 ZjistiSmerOdOkraju()
    {
        if (Mouse.current == null)
            return Vector2.zero;

        Vector2 pozice = Mouse.current.position.ReadValue();

        if (zastavitMimoOkno && !JeKurzorVOkne(pozice))
            return Vector2.zero;

        Vector2 smer = Vector2.zero;

        if (pozice.x <= hranicePixelu)
            smer.x = -1f;
        else if (pozice.x >= Screen.width - hranicePixelu)
            smer.x = 1f;

        if (pozice.y <= hranicePixelu)
            smer.y = -1f;
        else if (pozice.y >= Screen.height - hranicePixelu)
            smer.y = 1f;

        return smer.normalized;
    }

    private bool JeKurzorVOkne(Vector2 pozice)
    {
        return pozice.x >= 0f && pozice.x <= Screen.width &&
               pozice.y >= 0f && pozice.y <= Screen.height;
    }

    private void ZpracujZoom()
    {
        if (buildingPlacementController != null && buildingPlacementController.IsPlacingBuilding)
            return;
        if (!povolitZoom || !cilovaKamera.orthographic)
            return;

        float smerZoomu = 0f;
        if (InputManager.StaticClass != null && InputManager.StaticClass.Controls.Player.Zoom.IsPressed()) smerZoomu -= 1f; // menší ortho size = přiblížení
        if (InputManager.StaticClass != null && InputManager.StaticClass.Controls.Player.Unzoom.IsPressed()) smerZoomu += 1f;     // větší ortho size = oddálení

        if (Mathf.Abs(smerZoomu) > 0.0001f)
        {
            // Násobeno Time.deltaTime -> rychlost zoomu je nezávislá na FPS
            cilovaVelikostZoomu += smerZoomu * rychlostZoomu * Time.deltaTime;
            cilovaVelikostZoomu = Mathf.Clamp(cilovaVelikostZoomu, minVelikost, maxVelikost);
        }

        cilovaKamera.orthographicSize = Mathf.Lerp(
            cilovaKamera.orthographicSize,
            cilovaVelikostZoomu,
            1f - Mathf.Exp(-plynulostZoomu * Time.deltaTime));
    }

    private void OnDrawGizmosSelected()
    {
        if (!omezitHranice)
            return;

        Gizmos.color = Color.yellow;
        Vector3 stred = new Vector3(stredMapy.x, transform.position.y, stredMapy.z);
        Vector3 velikost = new Vector3(polovinaStranyMapy * 2f, 0.1f, polovinaStranyMapy * 2f);
        Gizmos.DrawWireCube(stred, velikost);
    }
}