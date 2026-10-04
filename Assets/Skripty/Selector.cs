using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using static Building;
using System;
using System.Collections; // pro IEnumerator, přidej nahoru, pokud tam ještě není
using System.Collections.Generic; // přidej nahoru, pokud tam ještě není

public class Selector : NetworkBehaviour
{
    
    [Header("Výběr troopů obdélníkem")]
    [SerializeField] private LayerMask groundLayerMask;
    [SerializeField] private Material selectionBoxMaterial; // volitelné, jinak default modrá
    [SerializeField] private float boxVisualHeight = 0.05f;

    private enum TroopBoxState { None, FirstPointSet, TroopsSelected }
    private TroopBoxState boxState = TroopBoxState.None;
    private Vector3 boxFirstPoint;
    private readonly List<Troop> selectedTroops = new();

    private GameObject selectionBoxObject;
    private Mesh selectionBoxMesh;
    [Header("Input")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask buildingLayer;
    [SerializeField] private LayerMask troopLayer;
    [SerializeField] private LayerMask resourceLayer;
    [SerializeField] private float maxRaycastDistance = 200f;

    [Header("UI panely - budovy")]
    [SerializeField] private GameObject corePanel;
    [SerializeField] private GameObject laserTowerPanel;
    [SerializeField] private GameObject coriumMinerPanel;
    [SerializeField] private GameObject researchPanel;
    [SerializeField] private GameObject factoryPanel;
    [SerializeField] private GameObject wallPanel;
    [SerializeField] private GameObject radarPanel;
    [SerializeField] private GameObject enemyBuildingPanel;
    [SerializeField] private GameObject enemyTroopPanel;

    [Header("UI panely - troopi (zatím jen Atlas)")]
    [SerializeField] private GameObject atlasPanel;
    [SerializeField] private GameObject harvesterPanel;
    [SerializeField] private GameObject photonPanel;

    [SerializeField] private UIFollower uiFollower;

    private Building selectedBuilding;
    private Troop selectedTroop;

    public Building SelectedBuilding => selectedBuilding;
    public Troop SelectedTroop => selectedTroop;

    /// <summary>Vyvolá se po dokončení box-selectu (i s prázdným seznamem, pokud nic nenašel).</summary>
    public event Action<IReadOnlyList<Troop>> OnTroopsSelected;
    /// <summary>Vyvolá se, když se probíhající box-select zruší cancel akcí.</summary>
    public event Action OnBoxSelectCancelled;

    private GameObject activePanel;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        StartCoroutine(SubscribeWhenReady());
    }

    private IEnumerator SubscribeWhenReady()
    {
        yield return new WaitUntil(() => InputManager.StaticClass != null);

        InputManager.StaticClass.Controls.UI.Click.performed += OnSelectStarted;
        InputManager.StaticClass.Controls.UI.Click.canceled += OnSelectCanceled;
        InputManager.StaticClass.Controls.UI.RightClick.performed += OnCancelPerformed;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
            return;
        InputManager.StaticClass.Controls.UI.Click.performed -= OnSelectStarted;
        InputManager.StaticClass.Controls.UI.Click.canceled -= OnSelectStarted;
        InputManager.StaticClass.Controls.UI.RightClick.performed -= OnCancelPerformed;
    }

    /// <summary>Mouse/tlačítko DOLŮ - jediná role: případně založit první bod box-selectu.</summary>
    private void OnSelectStarted(InputAction.CallbackContext ctx)
    {
        if (IsPointerOverUI())
            return;

        // Box-select smí začít tažením jen z čistého None stavu a jen když stisk
        // netrefil budovu/trooba - jinak by to kradlo klik, který má na Up
        // dokončit normální select/pohyb (viz OnSelectCanceled).
        if (boxState != TroopBoxState.None)
            return;

        if (ClickHitsSelectableObject())
            return;

        if (TryGetGroundPoint(out Vector3 point))
            StartBoxSelect(point);
    }

    /// <summary>Mouse/tlačítko NAHORU - dokončí box-select, zadá pohyb, nebo vybere budovu/trooba.</summary>
    private void OnSelectCanceled(InputAction.CallbackContext ctx)
    {
        if (IsPointerOverUI())
            return;

        // Právě probíhá tažení obdélníku (první bod padl na Down) - Up ho dokončí.
        if (boxState == TroopBoxState.FirstPointSet)
        {
            if (TryGetGroundPoint(out Vector3 point))
                FinishBoxSelect(point);
            else
                CancelBoxSelect(); // myš mimo mapu/zem při puštění - bezpečně zrušit
            return;
        }

        // Troopi jsou už vybraní z předchozího obdélníku - klik teď zadává cíl pohybu.
        if (boxState == TroopBoxState.TroopsSelected)
        {
            if (TryGetGroundPoint(out Vector3 groundPoint))
            {
                IssueMoveCommand(groundPoint);
                CancelBoxSelect();
            }
            return;
        }

        // boxState == None - normální klik: vyber budovu/trooba pod kurzorem, jinak deselect.
        Ray ray = GetMouseRay();
        LayerMask combinedMask = buildingLayer | troopLayer;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance, combinedMask))
        {
            Building building = hit.collider.GetComponentInParent<Building>();
            if (building != null)
            {
                HandleBuildingClicked(building);
                return;
            }

            Troop troop = hit.collider.GetComponentInParent<Troop>();
            if (troop != null)
            {
                HandleTroopClicked(troop);
                return;
            }
        }

        Deselect();
    }

    private bool ClickHitsSelectableObject()
    {
        LayerMask combinedMask = buildingLayer | troopLayer;
        return Physics.Raycast(GetMouseRay(), maxRaycastDistance, combinedMask);
    }

    private void OnCancelPerformed(InputAction.CallbackContext ctx)
    {
        if (boxState == TroopBoxState.None)
            return;

        Debug.Log("[Selector] Box-select zrušen cancel akcí.");
        CancelBoxSelect();
        OnBoxSelectCancelled?.Invoke();
    }

private bool IsPointerOverUI()
{
    return UnityEngine.EventSystems.EventSystem.current != null &&
           UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
}

private Ray GetMouseRay()
{
    Vector2 mousePos = Mouse.current.position.ReadValue();
    return mainCamera.ScreenPointToRay(mousePos);
}

// ---------- Box select troopů ----------

private void StartBoxSelect(Vector3 point)
{
    Deselect(); // schová případný panel budovy/trooba
    boxFirstPoint = point;
    boxState = TroopBoxState.FirstPointSet;
    ShowSelectionBoxVisual();
    Debug.Log("[Selector] První bod výběru nastaven.");
}

private void FinishBoxSelect(Vector3 point)
{
    Rect boxXZ = GetXZRect(boxFirstPoint, point);
    selectedTroops.Clear();

    ulong myId = NetworkManager.Singleton.LocalClientId;
    Troop[] allTroops = FindObjectsByType<Troop>(FindObjectsSortMode.None);

    foreach (Troop troop in allTroops)
    {
        if (troop == null || troop.OwnerClientId != myId)
            continue;

        Vector3 pos = troop.transform.position;
        if (boxXZ.Contains(new Vector2(pos.x, pos.z)))
            selectedTroops.Add(troop);
    }

    Debug.Log($"[Selector] Vybráno {selectedTroops.Count} troopů.");

    HideSelectionBoxVisual();
    boxState = selectedTroops.Count > 0 ? TroopBoxState.TroopsSelected : TroopBoxState.None;

    OnTroopsSelected?.Invoke(selectedTroops);
}

private void IssueMoveCommand(Vector3 destination)
{
    int groupSize = selectedTroops.Count;
    for (int i = 0; i < selectedTroops.Count; i++)
    {
        if (selectedTroops[i] != null)
        {
            selectedTroops[i]?.RequestMove(destination, i, groupSize);
        }
        else
        {
            Debug.LogWarning($"[Selector] Troop na indexu {i} je null, nelze poslat příkaz k pohybu.");
        }
    }

    Debug.Log($"[Selector] Poslal jsem {groupSize} troopů na {destination}.");
}

// Čekají na příští select klik (mouse up) a teprve pak vrátí výsledek raycastu přes callback.
// Použití z jiného skriptu: StartCoroutine(selector.IWantABuilding(building => { ... }));

public IEnumerator IWantABuilding(Action<Building> callback)
{
    // podle layerů a raycastu zjistit, jestli je pod kurzorem budova, a vrátit ji. Pokud není, vrátit null.
    // měla by být použitelná pro ostatní skripty, které potřebují zjistit, na jakou budovu hráč kliknul (např. pro interakci s budovou nebo zjištění info o ní).
    bool clicked = false;
    void OnClick(InputAction.CallbackContext ctx) => clicked = true;

    InputManager.StaticClass.Controls.UI.Click.canceled += OnClick;
    yield return new WaitUntil(() => clicked);
    InputManager.StaticClass.Controls.UI.Click.canceled -= OnClick;

    Building result = null;
    if (!IsPointerOverUI() && Physics.Raycast(GetMouseRay(), out RaycastHit hit, maxRaycastDistance, buildingLayer))
        result = hit.collider.GetComponentInParent<Building>();
    Debug.Log($"hitnul jsem {result.Type}");
    callback?.Invoke(result);
}

public IEnumerator IWantATroop(Action<Troop> callback)
{
    bool clicked = false;
    void OnClick(InputAction.CallbackContext ctx) => clicked = true;

    InputManager.StaticClass.Controls.UI.Click.canceled += OnClick;
    yield return new WaitUntil(() => clicked);
    InputManager.StaticClass.Controls.UI.Click.canceled -= OnClick;

    Troop result = null;
    if (!IsPointerOverUI() && Physics.Raycast(GetMouseRay(), out RaycastHit hit, maxRaycastDistance, troopLayer))
        result = hit.collider.GetComponentInParent<Troop>();
    Debug.Log($"hitnul jsem {result.Type}");
    callback?.Invoke(result);
}

public IEnumerator IWantAResource(Action<Resource> callback)
{
    bool clicked = false;
    void OnClick(InputAction.CallbackContext ctx) => clicked = true;

    InputManager.StaticClass.Controls.UI.Click.canceled += OnClick;
    yield return new WaitUntil(() => clicked);
    InputManager.StaticClass.Controls.UI.Click.canceled -= OnClick;

    Resource result = null;
    if (!IsPointerOverUI() && Physics.Raycast(GetMouseRay(), out RaycastHit hit, maxRaycastDistance, resourceLayer))
        result = hit.collider.GetComponentInParent<Resource>();
    //Debug.Log($"hitnul jsem resource a má {result.CoriumAmount.Value} coria");
    Debug.Log("corium");
    callback?.Invoke(result);
}
// Čeká na příští select klik a vrátí bod na zemi (nebo null, pokud klik netrefil
// terén / byl nad UI). Použití: StartCoroutine(selector.IWantAGroundPoint(point => { ... }));
public IEnumerator IWantAGroundPoint(Action<Vector3?> callback)
{
    bool clicked = false;
    void OnClick(InputAction.CallbackContext ctx) => clicked = true;

    InputManager.StaticClass.Controls.UI.Click.canceled += OnClick;
    yield return new WaitUntil(() => clicked);
    InputManager.StaticClass.Controls.UI.Click.canceled -= OnClick;

    Vector3? result = null;
    if (!IsPointerOverUI() && TryGetGroundPoint(out Vector3 point))
        result = point;

    callback?.Invoke(result);
}

private void CancelBoxSelect()
{
    boxState = TroopBoxState.None;
    selectedTroops.Clear();
    HideSelectionBoxVisual();
}

private Rect GetXZRect(Vector3 a, Vector3 b)
{
    float xMin = Mathf.Min(a.x, b.x);
    float xMax = Mathf.Max(a.x, b.x);
    float zMin = Mathf.Min(a.z, b.z);
    float zMax = Mathf.Max(a.z, b.z);
    return new Rect(xMin, zMin, xMax - xMin, zMax - zMin);
}

// ---------- Vizuál obdélníku ----------

private void ShowSelectionBoxVisual()
{
    if (selectionBoxObject == null)
    {
        selectionBoxObject = new GameObject("SelectionBoxVisual");
        MeshFilter mf = selectionBoxObject.AddComponent<MeshFilter>();
        MeshRenderer mr = selectionBoxObject.AddComponent<MeshRenderer>();

        selectionBoxMesh = new Mesh();
        mf.mesh = selectionBoxMesh;
        mr.material = selectionBoxMaterial != null ? selectionBoxMaterial : CreateDefaultTransparentBlue();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    selectionBoxObject.SetActive(true);
}

private void HideSelectionBoxVisual()
{
    if (selectionBoxObject != null)
        selectionBoxObject.SetActive(false);
}

private void UpdateSelectionBoxVisual(Vector3 a, Vector3 b)
{
    if (selectionBoxMesh == null)
        return;

    Rect rect = GetXZRect(a, b);
    float y = Mathf.Max(a.y, b.y) + boxVisualHeight;

    Vector3 p0 = new Vector3(rect.xMin, y, rect.yMin);
    Vector3 p1 = new Vector3(rect.xMax, y, rect.yMin);
    Vector3 p2 = new Vector3(rect.xMax, y, rect.yMax);
    Vector3 p3 = new Vector3(rect.xMin, y, rect.yMax);

    selectionBoxMesh.Clear();
    selectionBoxMesh.vertices = new[] { p0, p1, p2, p3 };
    // dvě strany trojúhelníků, ať je vidět shora i zdola
    selectionBoxMesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
    selectionBoxMesh.RecalculateNormals();
    selectionBoxMesh.RecalculateBounds();
}

private Material CreateDefaultTransparentBlue()
{
    Material mat = new Material(Shader.Find("Sprites/Default"));
    mat.color = new Color(0.2f, 0.5f, 1f, 0.25f);
    return mat;
}
private void Update()
{
    if (!IsOwner)
        return;

    if (boxState == TroopBoxState.FirstPointSet && TryGetGroundPoint(out Vector3 currentPoint))
    {
        UpdateSelectionBoxVisual(boxFirstPoint, currentPoint);
    }
}

private bool TryGetGroundPoint(out Vector3 point)
{
    point = default;
    Ray ray = GetMouseRay();

    if (Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance, groundLayerMask))
    {
        point = hit.point;
        return true;
    }
    return false;
}

    // ---------- Budovy ----------

private void HandleBuildingClicked(Building building)
{
    if (selectedBuilding == building)
    {
        Deselect();
        return;
    }

    HideActivePanel();
    selectedTroop = null;
    selectedBuilding = building;

    // Nepřátelská budova
    if (building.OwnerClientId != NetworkManager.Singleton.LocalClientId)
    {
        activePanel = enemyBuildingPanel;

        if (activePanel != null)
        {
            activePanel.SetActive(true);

            uiFollower.ShowFor(building.transform);
            uiFollower.LateUpdate();

            EnemyBuildingStatus status = activePanel.GetComponent<EnemyBuildingStatus>();
            if (status != null)
            {
                // TODO: až budeš mít PlayerName systém, nahraď tímto skutečné jméno hráče.
                status.UpdateStatus(building, $"Player {building.OwnerClientId}");
            }
        }

        return;
    }

    // Vlastní budova
    activePanel = GetPanelForBuildingType(building.Type);

    if (activePanel != null)
    {
        Debug.Log($"Zobrazujeme panel pro budovu typu {building.Type}");
        activePanel.SetActive(true);

        uiFollower.ShowFor(building.transform);
        uiFollower.LateUpdate();

        BuildingStatus status = activePanel.GetComponent<BuildingStatus>();
        if (status != null)
            status.UpdateStatus(building);
    }
}

    private GameObject GetPanelForBuildingType(BuildingType type)
    {
        switch (type)
        {
            case BuildingType.Core: return corePanel;
            case BuildingType.LaserTower: return laserTowerPanel;
            case BuildingType.Factory: return factoryPanel;
            case BuildingType.Wall: return wallPanel;
            case BuildingType.Radar: return radarPanel;
            default: return null;
        }
    }

    // ---------- Troopi ----------

    private void HandleTroopClicked(Troop troop)
    {
        // TODO: stejně jako u budov - pokud troop.OwnerClientId != LocalClientId,
        // zobrazit jen read-only info o cizí jednotce

        if (selectedTroop == troop)
        {
            Deselect();
            return;
        }

        HideActivePanel();
        selectedBuilding = null;

            // Nepřátelská budova
        if (troop.OwnerClientId != NetworkManager.Singleton.LocalClientId)
        {
            activePanel = enemyTroopPanel;

            if (activePanel != null)
            {
                activePanel.SetActive(true);

                uiFollower.ShowFor(troop.transform);
                uiFollower.LateUpdate();

                EnemyTroopStatus status = activePanel.GetComponent<EnemyTroopStatus>();
                if (status != null)
                {
                    // TODO: až budeš mít PlayerName systém, nahraď tímto skutečné jméno hráče.
                    status.UpdateStatus(troop, $"Player {troop.OwnerClientId}");
                }
            }

            return;
        }

        selectedTroop = troop;
        activePanel = GetPanelForTroopType(troop.Type);

        if (activePanel != null)
        {
            Debug.Log($"Zobrazujeme panel pro trooba typu {troop.Type}");
            activePanel.SetActive(true);
            uiFollower.ShowFor(troop.transform);
            uiFollower.LateUpdate();

            TroopStatus status = activePanel.GetComponent<TroopStatus>();
            if (status != null)
                status.UpdateStatus(troop);
        }
    }

    private GameObject GetPanelForTroopType(TroopType type)
    {
        switch (type)
        {
            case TroopType.Atlas: return atlasPanel;
            case TroopType.Photon: return photonPanel; // TODO: vytvořit vlastní panel pro photon
            case TroopType.Harvester: return harvesterPanel; // TODO: vytvořit vlastní panel pro harvester
            // sem se budou přidávat další typy troopů
            default: return null;
        }
    }

    // ---------- Společné ----------

    private void HideActivePanel()
    {
        if (activePanel != null)
            activePanel.SetActive(false);
        activePanel = null;
    }

    private void Deselect()
    {
        HideActivePanel();
        selectedBuilding = null;
        selectedTroop = null;
        uiFollower.Hide();
    }
}