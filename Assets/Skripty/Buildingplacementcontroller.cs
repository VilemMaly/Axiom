using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// KLIENTSKÁ, čistě vizuální logika umisťování budov (ghost + výběr pozice).
/// Samotná validace a spawn budovy na síti se řeší jinde (server-authoritative,
/// viz Build.cs a sdílené statické metody v Building.cs) - tenhle skript sám
/// o sobě nic nesíťuje ani nespawnuje.
///
/// POSTUP POUŽITÍ:
/// 1) Tento skript pověs na hráčův síťový objekt (ten, který má NetworkObject a patří konkrétnímu
///    klientovi - typicky "PlayerController" prefab spawnutý pro každého hráče).
/// 2) Do "Buildable Prefabs" v inspectoru přetáhni všechny prefaby budov, které chceš umět stavět
///    (index musí odpovídat pořadí, které očekává TroopInteractor.Build / Build.cs).
/// 3) Zavolej BeginPlacement(index) např. z UI tlačítka "Postavit kasárna" -> spustí se duch (ghost),
///    který sleduje kurzor, snapuje na grid a zarovná se na zem podle svého kolideru.
/// 4) Hráč potvrdí umístění Input Actionem (výchozí: levé tlačítko myši) -> zavolá se
///    troopManager.Build(...), který žádost pošle na server (Build.cs). Server má finální slovo -
///    lokální IsPositionValid tady je jen rychlá vizuální nápověda (barva ghosta), ne autorita.
/// </summary>
public class BuildingPlacementController : NetworkBehaviour
{
    public event Action CloseUI;
    [Header("Prefaby, které lze stavět (musí být zaregistrované v Network Prefabs Listu)")]
    [SerializeField] private List<GameObject> buildablePrefabs = new List<GameObject>();

    [Header("Ghost (průhledná preview) nastavení")]
    [Tooltip("Materiál pro validní / nevalidní pozici ghosta")]
    [SerializeField] private Material ghostValidMaterial;
    [SerializeField] private Material ghostInvalidMaterial;

    [Header("Raycast / umístění")]
    [SerializeField] private LayerMask groundLayerMask;
    [SerializeField] private LayerMask obstacleLayerMask; // co blokuje stavbu (jednotky, jiné budovy...)
    [SerializeField] private Camera playerCamera; // pokud necháš prázdné, vezme se Camera.main

    [Header("Grid snap (nastavitelné z editoru)")]
    [SerializeField] private bool snapToGrid = true;
    [SerializeField] private float gridCellSize = 1f;
    [SerializeField] private Vector3 gridOrigin = Vector3.zero;

    [Header("Rotace ghosta (kolečko myši)")]
    [Tooltip("O kolik stupňů se ghost otočí na jedno cvaknutí kolečka")]
    [SerializeField] private float rotationStep = 90f;
    [Tooltip("Ochranná doba mezi dvěma otočeními, ať jedno cvaknutí kolečka neotočí ghost víckrát za sebou")]
    [SerializeField] private float scrollRotationCooldown = 0.15f;

    [Header("Player Spawn / Core limit")]
    [SerializeField] private float maxCoreDistanceFromSpawn = 30f;

    public NetworkVariable<Vector3> SpawnPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
);


    // ---- runtime stav (jen na klientovi, který staví) ----
    public MainUI mainUI;
    private GameObject _ghostInstance;
    private int _selectedPrefabIndex = -1;
    private bool _isPlacing = false;
    private bool _isCurrentPositionValid = false;
    private Vector3 _currentPlacementPosition;
    private TroopInteractor troopManager;

    // Aktuální Y rotace ghosta (ve stupních), měněná kolečkem myši po krocích rotationStep.
    private float _ghostRotationY = 0f;
    private float _scrollCooldownTimer = 0f;

    // Vzdálenost od pivotu prefabu ke spodní hraně jeho kolideru (dopočítáno z
    // Collider.bounds při vytvoření ghosta). Použije se k tomu, aby ghost stál
    // na zemi a nebyl napůl pod mapou, i když má prefab pivot uprostřed modelu.
    private float _ghostGroundOffset = 0f;

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;
    }

    private void Start()
    {
        troopManager = GetComponent<TroopInteractor>();
        CloseUI += mainUI.uiClose;
    }

    public override void OnNetworkSpawn()
    {
        // Server si uloží spawn hráče
        if (IsServer)
        {
            SpawnPosition.Value = transform.position;
        }

        // Tenhle skript má reagovat na input jen u hráče, kterému patří
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        StartCoroutine(WaitForInputManager());
    }

        private IEnumerator WaitForInputManager()
    {
        yield return new WaitUntil(() => InputManager.StaticClass != null);
        InputManager.StaticClass.Controls.UI.Click.performed += OnConfirmPerformed;
        InputManager.StaticClass.Controls.UI.RightClick.performed += OnCancelPerformed;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        InputManager.StaticClass.Controls.UI.Click.performed -= OnConfirmPerformed;
        InputManager.StaticClass.Controls.UI.RightClick.performed -= OnCancelPerformed;
    }

    public void buildCore()
    {
        BeginPlacement(0);
    }
    public void buildLaserTower()
    {
        BeginPlacement(3);
    }
    public void buildCoriumMiner()
    {
        BeginPlacement(2);
    }
    public void buildWall()
    {
        BeginPlacement(7);
    }
    public void buildRadar()
    {
        BeginPlacement(4);
    }
    public void buildFactory()
    {
        BeginPlacement(1);
    }
    public void buildResearchLab()
    {
        BeginPlacement(5);
    }
    public void buildEnergyPlant()
    {
        BeginPlacement(6);
    }

    /// <summary>
    /// Zavolej z UI (např. kliknutí na ikonu budovy), aby se spustilo umisťování.
    /// </summary>
    public void BeginPlacement(int prefabIndex)
    {
        if (!IsOwner) return;
        if (prefabIndex < 0 || prefabIndex >= buildablePrefabs.Count) return;

        CancelPlacement(); // ukliď případný předchozí ghost

        _selectedPrefabIndex = prefabIndex;
        _isPlacing = true;
        _ghostRotationY = 0f;
        _scrollCooldownTimer = 0f;

        _ghostInstance = Instantiate(buildablePrefabs[prefabIndex]);
        PrepareGhostVisual(_ghostInstance);
        CloseUI?.Invoke();
    }

    /// <summary>
    /// DŮLEŽITÝ PŘEDPOKLAD O PREFABU: gameplay skripty jako "Building" (a jejich collidery,
    /// pokud mají blokovat pohyb/kolize) musí být v prefabu uložené jako VYPNUTÉ (enabled = false).
    /// Aktivují se samy až v Building.OnNetworkSpawn() (viz ukázka Building.cs), a to identicky
    /// na serveru i na klientech, jakmile je objekt opravdu nasíťovaný. Ghost tak díky výchozímu
    /// stavu prefabu vůbec nepotřebuje ruční vypínání gameplay logiky - jen se postará,
    /// aby se nechoval jako síťový objekt.
    /// </summary>
    private void PrepareGhostVisual(GameObject ghost)
    {
        // Ghost nikdy nesmí být NetworkObject - není spawnutý přes Netcode, takže by mátl
        // cokoliv, co by na něj případně sáhlo (např. GetComponent<NetworkObject>()).
        foreach (var netObj in ghost.GetComponentsInChildren<NetworkObject>())
            Destroy(netObj);

        // Stejný výpočet zarovnání na zem jako při reálném spawnu (Building.SpawnAndAssignOwnership),
        // aby ghost přesně odpovídal tomu, kde budova nakonec skutečně vznikne.
        Collider[] colliders = ghost.GetComponentsInChildren<Collider>();
        _ghostGroundOffset = Building.ComputeGroundOffset(ghost);

        if (colliders.Length == 0)
        {
            Debug.LogWarning($"[BuildingPlacementController] Prefab '{ghost.name}' nemá žádný Collider - " +
                              "nelze spočítat zarovnání na zem, ghost použije pivot budovy tak, jak je.");
        }

        // Pojistka navíc, kdyby prefab náhodou neměl collider vypnutý defaultně -
        // ghost se nesmí srážet s ničím ani blokovat raycast na zem.
        foreach (var col in colliders)
            col.enabled = false;
    }

    private void Update()
    {
        if (!_isPlacing || _ghostInstance == null) return;

        HandleRotationInput();

        if (TryGetGroundPoint(out Vector3 groundPoint))
        {
            _currentPlacementPosition = snapToGrid ? SnapToGrid(groundPoint) : groundPoint;

            // Y dorovnáme podle offsetu z PrepareGhostVisual, aby spodek kolideru
            // ležel na groundPoint.y, ne pivot objektu.
            _currentPlacementPosition.y = groundPoint.y + _ghostGroundOffset;
            _ghostInstance.transform.SetPositionAndRotation(
                _currentPlacementPosition,
                Quaternion.Euler(0f, _ghostRotationY, 0f));

            _isCurrentPositionValid = IsPositionValid(_currentPlacementPosition);
            UpdateGhostColor(_isCurrentPositionValid);
        }
    }

    /// <summary>
    /// Kolečko myši otáčí ghosta po krocích rotationStep (výchozí 90°).
    /// Cooldown zajistí, že jedno cvaknutí kolečka = jedno otočení, ne víc najednou.
    /// </summary>
    private void HandleRotationInput()
    {
        if (_scrollCooldownTimer > 0f)
            _scrollCooldownTimer -= Time.deltaTime;

        float scrollY = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        if (_scrollCooldownTimer <= 0f && Mathf.Abs(scrollY) > 0.01f)
        {
            _ghostRotationY += Mathf.Sign(scrollY) * rotationStep;
            _ghostRotationY = Mathf.Repeat(_ghostRotationY, 360f);
            _scrollCooldownTimer = scrollRotationCooldown;
        }
    }

    private bool TryGetGroundPoint(out Vector3 point)
    {
        point = Vector3.zero;
        if (playerCamera == null) return false;

        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Ray ray = playerCamera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayerMask))
        {
            point = hit.point;
            return true;
        }
        return false;
    }

    private Vector3 SnapToGrid(Vector3 worldPos)
    {
        Vector3 local = worldPos - gridOrigin;
        float x = Mathf.Round(local.x / gridCellSize) * gridCellSize;
        float z = Mathf.Round(local.z / gridCellSize) * gridCellSize;
        return new Vector3(x, worldPos.y, z) + new Vector3(gridOrigin.x, 0f, gridOrigin.z);
    }

    private bool IsPositionValid(Vector3 pos)
    {
        float checkRadius = gridCellSize * 0.5f;

        // kontrola kolizí
        if (Physics.CheckSphere(pos + Vector3.up * 0.5f, checkRadius, obstacleLayerMask))
            return false;


        // kontrola vzdálenosti Core od spawnu
        if (_selectedPrefabIndex >= 0 &&
            buildablePrefabs[_selectedPrefabIndex].GetComponent<Building>().Type == Building.BuildingType.Core)
        {
            float distance = Vector3.Distance(
                SpawnPosition.Value,
                pos
            );

            if (distance > maxCoreDistanceFromSpawn)
                return false;
        }

        return true;
}

    private void UpdateGhostColor(bool valid)
    {
        var mat = valid ? ghostValidMaterial : ghostInvalidMaterial;
        if (mat == null) return;

        foreach (var renderer in _ghostInstance.GetComponentsInChildren<Renderer>())
            renderer.material = mat;
    }

    private void OnConfirmPerformed(InputAction.CallbackContext ctx)
    {
        if (!_isPlacing) return;
        if (!_isCurrentPositionValid) return; // klient si to lokálně "ověří", server má finální slovo


        // Samotné umístění (validace + spawn) řeší server přes Build.cs / TroopInteractor -
        // tenhle skript se stará jen o ghost, vizuál a výběr pozice.
        int rotationDegrees = Mathf.RoundToInt(_ghostRotationY);
        troopManager.Build(_currentPlacementPosition, _selectedPrefabIndex, _ghostInstance.GetComponent<Building>().Type, rotationDegrees);
        CancelPlacement();
    }

    private void OnCancelPerformed(InputAction.CallbackContext ctx)
    {
        CancelPlacement();
    }

    private void CancelPlacement()
    {
        _isPlacing = false;
        _selectedPrefabIndex = -1;
        if (_ghostInstance != null)
        {
            Destroy(_ghostInstance);
            _ghostInstance = null;
        }
    }

}