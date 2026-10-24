using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
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

    [Header("Napojení zdí")]
    [Tooltip("Vrstva zdí a bran, které se mohou spojit zámky.")]
    [SerializeField] private LayerMask wallLayerMask;
    [SerializeField] private float wallSearchRadius = 2f;

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
    public bool IsPlacingBuilding => _isPlacing;
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
    private bool _wallLineMode;
    private bool _wallLineStarted;
    private Vector3 _wallLineStart;
    private float _wallLineRotation;
    private float _wallPieceLength;
    private int _wallLinePieceCount;
    private GameObject _wallLineAnchorGhost;
    private readonly List<GameObject> _wallLineGhosts = new List<GameObject>();

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;
    }

    private void Start()
    {
        troopManager = GetComponent<TroopInteractor>();
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
        _wallLineMode = buildablePrefabs[prefabIndex].GetComponent<Wall>() != null;
        _wallLineStarted = false;

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
        foreach (var netObj in ghost.GetComponentsInParent<NetworkObject>())
            Destroy(netObj);

        // musí se zobrazit správný model
        ghost.GetComponent<BuildingMaterial>().ApplyMaterial(true, (int)OwnerClientId);
        // Nesmí mít ani žádný NavMesh Obstacle v rodiči, jinak se jednotky bugují.
        var navmesh = ghost.GetComponentInParent<NavMeshObstacle>();
        if (navmesh != null)
            Destroy(navmesh);
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

        // V řadě držíme natočení první zdi. Kolečko myši už ho nemění.
        if (!_wallLineStarted)
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

            Building ghostBuilding = _ghostInstance.GetComponent<Building>();
            Wall ghostWall = ghostBuilding as Wall;
            bool hasLocks = TryGetLocks(ghostBuilding, out _, out _);
            if (hasLocks && !_wallLineStarted)
                SnapBuildingToNearbyLock(ghostBuilding);

            _isCurrentPositionValid = IsPositionValid(_currentPlacementPosition);
            UpdateGhostColor(_isCurrentPositionValid);

            if (ghostWall != null && _wallLineMode && _wallLineStarted)
                UpdateWallLine(ghostWall);
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
        UpdateGhostColor(valid, _ghostInstance);
    }

    private void UpdateGhostColor(bool valid, GameObject ghost)
    {
        var mat = valid ? ghostValidMaterial : ghostInvalidMaterial;
        if (mat == null || ghost == null) return;

        foreach (var renderer in ghost.GetComponentsInChildren<Renderer>())
            renderer.material = mat;
    }

    private void OnConfirmPerformed(InputAction.CallbackContext ctx)
    {
        if (!_isPlacing) return;

        if (_wallLineMode && _ghostInstance.GetComponent<Wall>() != null)
        {
            if (!_wallLineStarted)
            {
                if (!_isCurrentPositionValid) return;
                _wallLineStart = _currentPlacementPosition;
                _wallLineStarted = true;
                _wallLinePieceCount = 1;
                _wallLineRotation = _ghostRotationY;
                Wall firstWall = _ghostInstance.GetComponent<Wall>();
                _wallPieceLength = Vector3.Distance(firstWall.leftLock.position, firstWall.rightLock.position);
                _wallLineAnchorGhost = Instantiate(buildablePrefabs[_selectedPrefabIndex]);
                PrepareGhostVisual(_wallLineAnchorGhost);
                _wallLineAnchorGhost.transform.SetPositionAndRotation(
                    _wallLineStart, Quaternion.Euler(0f, _ghostRotationY, 0f));
                return;
            }

            if (_wallLinePieceCount < 1) return;
            PlaceWallLine();
            CancelPlacement();
            return;
        }

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

    // Vrátí zámky ze zdi nebo brány.
    private bool TryGetLocks(Building building, out Transform left, out Transform right)
    {
        left = null;
        right = null;
        if (building is Wall wall)
        {
            left = wall.leftLock;
            right = wall.rightLock;
        }
        else if (building is Gate gate)
        {
            left = gate.leftLock;
            right = gate.rightLock;
        }
        return left != null && right != null;
    }

    // Posune ghost tak, aby se jeho nejbližší zámek dotkl zámku zdi nebo brány.
    private void SnapBuildingToNearbyLock(Building ghostBuilding)
    {
        if (!TryGetLocks(ghostBuilding, out Transform ghostLeft, out Transform ghostRight)) return;

        Collider[] nearby = Physics.OverlapSphere(ghostBuilding.transform.position, wallSearchRadius, wallLayerMask);
        float bestDistance = float.MaxValue;
        Vector3 move = Vector3.zero;

        foreach (Collider col in nearby)
        {
            Building other = col.GetComponentInParent<Building>();
            if (other == null || other == ghostBuilding || !TryGetLocks(other, out Transform otherLeft, out Transform otherRight)) continue;

            Transform[] ghostLocks = { ghostLeft, ghostRight };
            Transform[] otherLocks = { otherLeft, otherRight };
            foreach (Transform ghostLock in ghostLocks)
            foreach (Transform otherLock in otherLocks)
            {
                float distance = Vector3.Distance(ghostLock.position, otherLock.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    move = otherLock.position - ghostLock.position;
                }
            }
        }

        if (bestDistance < wallSearchRadius)
        {
            ghostBuilding.transform.position += move;
            if (_ghostInstance != null && ghostBuilding.gameObject == _ghostInstance)
                _currentPlacementPosition = _ghostInstance.transform.position;
        }
    }

    // Vytvoří náhled dalších zdí podél směru od prvního bodu ke kurzoru.
    private void UpdateWallLine(Wall wall)
    {
        Vector3 direction = _currentPlacementPosition - _wallLineStart;
        direction.y = 0f;
        float length = direction.magnitude;
        if (length < 0.1f || _wallPieceLength < 0.1f)
        {
            _wallLinePieceCount = 1;
            _ghostInstance.SetActive(false);
            ClearWallLineGhosts();
            return;
        }
        direction.Normalize();
        _wallLineAnchorGhost.transform.rotation = Quaternion.Euler(0f, _wallLineRotation, 0f);

        _wallLinePieceCount = 1 + Mathf.FloorToInt(length / _wallPieceLength);
        int extraPieces = _wallLinePieceCount - 1;
        SetWallLineGhostCount(Mathf.Max(0, extraPieces - 1));
        _ghostInstance.SetActive(extraPieces > 0);

        Wall previousWall = _wallLineAnchorGhost.GetComponent<Wall>();
        for (int i = 0; i < extraPieces; i++)
        {
            GameObject piece = i < _wallLineGhosts.Count ? _wallLineGhosts[i] : _ghostInstance;
            Vector3 pos = _wallLineStart + direction * _wallPieceLength * (i + 1);
            pos.y = _wallLineStart.y;
            piece.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, _wallLineRotation, 0f));
            Wall pieceWall = piece.GetComponent<Wall>();
            SnapWallLocksTogether(previousWall, pieceWall);
            UpdateGhostColor(IsPositionValid(piece.transform.position), piece);
            previousWall = pieceWall;
        }

    }

    // Posune další zeď tak, aby se její nejbližší zámek dotkl zámku předchozí zdi.
    private void SnapWallLocksTogether(Wall previousWall, Wall nextWall)
    {
        if (previousWall == null || nextWall == null || nextWall.leftLock == null || nextWall.rightLock == null ||
            previousWall.leftLock == null || previousWall.rightLock == null) return;

        Transform[] previousLocks = { previousWall.leftLock, previousWall.rightLock };
        Transform[] nextLocks = { nextWall.leftLock, nextWall.rightLock };
        float closestDistance = float.MaxValue;
        Vector3 move = Vector3.zero;

        foreach (Transform previousLock in previousLocks)
        foreach (Transform nextLock in nextLocks)
        {
            float distance = Vector3.Distance(previousLock.position, nextLock.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                move = previousLock.position - nextLock.position;
            }
        }

        nextWall.transform.position += move;
    }

    private void SetWallLineGhostCount(int count)
    {
        while (_wallLineGhosts.Count < count)
        {
            GameObject piece = Instantiate(buildablePrefabs[_selectedPrefabIndex]);
            PrepareGhostVisual(piece);
            _wallLineGhosts.Add(piece);
        }
        while (_wallLineGhosts.Count > count)
        {
            int last = _wallLineGhosts.Count - 1;
            Destroy(_wallLineGhosts[last]);
            _wallLineGhosts.RemoveAt(last);
        }
    }

    private void PlaceWallLine()
    {
        int rotation = Mathf.RoundToInt(_wallLineRotation);
        troopManager.Build(_wallLineStart, _selectedPrefabIndex, Building.BuildingType.Wall, rotation);
        foreach (GameObject piece in _wallLineGhosts)
            troopManager.Build(piece.transform.position, _selectedPrefabIndex, Building.BuildingType.Wall, rotation);
        if (_wallLinePieceCount > 1)
            troopManager.Build(_ghostInstance.transform.position, _selectedPrefabIndex, Building.BuildingType.Wall, rotation);
    }

    private void ClearWallLineGhosts()
    {
        foreach (GameObject piece in _wallLineGhosts)
            if (piece != null) Destroy(piece);
        _wallLineGhosts.Clear();
    }

    private void ClearWallLinePreviews()
    {
        ClearWallLineGhosts();
        if (_wallLineAnchorGhost != null)
        {
            Destroy(_wallLineAnchorGhost);
            _wallLineAnchorGhost = null;
        }
    }

    private void CancelPlacement()
    {
        _isPlacing = false;
        _selectedPrefabIndex = -1;
        _wallLineMode = false;
        _wallLineStarted = false;
        _wallLinePieceCount = 0;
        ClearWallLinePreviews();
        if (_ghostInstance != null)
        {
            Destroy(_ghostInstance);
            _ghostInstance = null;
        }
    }

}
