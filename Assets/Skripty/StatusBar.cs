using UnityEngine;

// Obecná komponenta pro "bar" nad objektem - HP bar u jednotek/budov, loading bar
// pro spawn jednotek na budovách, progress bar stavby atd. Neřeší síť ani to,
// odkud hodnota přichází - jen vykreslení podle zadaného current/max. Věší se
// jako komponenta vedle toho, kdo bar potřebuje (Troop, Building, ...), a ten
// jen volá SetValue()/SetPercent() kdykoliv se sledovaná hodnota změní.
//
// Do editoru se táhnou jen 2 objekty (pivot a background) - zbylé komponenty
// (root pro skrytí, fill transform, fill renderer) si StatusBar odvodí sám:
//   pivot            - billboard root, natáčí se ke kameře
//   background (dítě pivotu) - pozadí baru; slouží zároveň jako "root" pro
//                       skrytí při plné hodnotě
//   background/[0]   - první child background objektu = fill (sprite s pivotem
//                       Left, škáluje se na X podle %)
public class StatusBar : MonoBehaviour
{
    [Header("Reference (jen 2 objekty)")]
    [Tooltip("Objekt nad objektem, který se natáčí ke kameře (obsahuje background i fill).")]
    [SerializeField] private Transform pivot;
    [Tooltip("Pozadí baru. Musí mít jako svého prvního childa fill sprite (pivot Left) - ten si StatusBar najde sám. Zároveň slouží jako root pro skrytí při plné hodnotě.")]
    [SerializeField] private GameObject background;

    [Header("Chování")]
    [SerializeField] private bool hideWhenFull = true;
    [SerializeField] private Color fullColor = Color.green;
    [SerializeField] private Color emptyColor = Color.red;

    // Odvozeno z background v Awake - fill je jeho první child, fillRenderer
    // je SpriteRenderer na tomhle childovi. Root pro skrytí je rovnou background.
    private Transform fill;
    private SpriteRenderer fillRenderer;

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (background == null)
        {
            Debug.LogWarning($"[StatusBar] {name}: background není přiřazený.");
            return;
        }

        if (background.transform.childCount == 0)
        {
            Debug.LogWarning($"[StatusBar] {name}: background nemá žádného childa (fill).");
            return;
        }

        fill = background.transform.GetChild(0);
        fillRenderer = fill.GetComponent<SpriteRenderer>();

        if (fillRenderer == null)
            Debug.LogWarning($"[StatusBar] {name}: fill '{fill.name}' nemá SpriteRenderer.");
    }

    // Nastaví aktuální stav baru podle current/max (např. Health/MaxHealth).
    // Voláno vlastníkem (Troop, Building, spawner, ...) kdykoliv se hodnota
    // změní - StatusBar sám nic po síti nesynchronizuje, je čistě vizuální.
    public void SetValue(float current, float max)
    {
        if (fill == null || max <= 0f)
            return;

        float pct = Mathf.Clamp01(current / max);

        Vector3 scale = fill.localScale;
        scale.x = pct;
        fill.localScale = scale;

        if (fillRenderer != null)
            fillRenderer.color = Color.Lerp(emptyColor, fullColor, pct);

        if (background != null && hideWhenFull)
            background.SetActive(pct < 1f);
    }

    // Zkratka, pokud už máš rovnou procenta 0..1 - typicky loading/progress bar
    // (spawn jednotky na budově, stavba budovy apod.), kde není přirozený "max".
    public void SetPercent(float pct)
    {
        SetValue(pct, 1f);
    }

    private void LateUpdate()
    {
        if (pivot == null)
            return;

        Camera cam = CameraController.LocalCamera;
        if (cam == null)
            return;

        // Billboard: bar natočený přesně jako kamera, aby byl vždy čitelný en face.
        pivot.rotation = cam.transform.rotation;
    }
}