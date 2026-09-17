using UnityEngine;
using UnityEngine.InputSystem;

public class TroopActions : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private Selector selector;
    [SerializeField] private Camera mainCamera;

    [Header("Input")]
    [SerializeField] private InputActionReference confirmMoveAction; // klik potvrzující cíl pohybu

    [Header("Raycast na zem")]
    [SerializeField] private LayerMask groundLayerMask; // stejný princip jako u BuildingSelector/TroopSpawner
    [SerializeField] private float maxRaycastDistance = 200f;

    private Troop troopToMove;
    private bool waitingForMoveTarget = false;

    /// <summary>
    /// Zavolá se z UI tlačítka "Move". Zjistí, jaký troop je aktuálně vybraný
    /// v Selectoru, a začne čekat na klik pro určení cíle pohybu.
    /// </summary>
    public void BeginMoveSelection()
    {
        Troop selectedTroop = selector.SelectedTroop;
        if (selectedTroop == null)
        {
            Debug.Log("Není vybraný žádný troop, pohyb nelze zadat.");
            return;
        }

        // Pokud už jednou čekáme na cíl, zruš to a začni znovu s novým troopem
        CancelMoveSelection();

        troopToMove = selectedTroop;
        waitingForMoveTarget = true;

        confirmMoveAction.action.Enable();
        confirmMoveAction.action.performed += OnMoveTargetConfirmed;

        Debug.Log($"Čekám na kliknutí pro cíl pohybu jednotky {troopToMove.name}");
    }

    private void OnMoveTargetConfirmed(InputAction.CallbackContext ctx)
    {
        if (!waitingForMoveTarget || troopToMove == null)
        {
            CancelMoveSelection();
            return;
        }

        // Klik na UI (např. jiné tlačítko) se nepočítá jako cíl ve světě
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance, groundLayerMask))
        {
            Debug.Log($"Cíl pohybu: {hit.point}, posílám jednotce {troopToMove.name}");
            troopToMove.RequestMove(hit.point);
        }

        CancelMoveSelection();
    }

    private void CancelMoveSelection()
    {
        waitingForMoveTarget = false;
        troopToMove = null;

        if (confirmMoveAction != null)
            confirmMoveAction.action.performed -= OnMoveTargetConfirmed;
    }

    private void OnDestroy()
    {
        if (confirmMoveAction != null)
            confirmMoveAction.action.performed -= OnMoveTargetConfirmed;
    }
}