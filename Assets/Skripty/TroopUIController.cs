using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[Serializable]
public class TroopSprites
{
    public TroopType troopType;
    public Sprite troopSprite;
}

public class TroopUIController : MonoBehaviour
{
    private enum GroundOrder { None, Defend, Attack, Harvest }

    [SerializeField] private List<TroopSprites> troopSprites;
    [SerializeField] private List<TroopSlot> troopSlots;
    private TroopGroup selectedTroopGroup;

    [Header("Ground order cursors")]
    [SerializeField] private Sprite defendHoverSprite;
    [SerializeField] private Sprite attackHoverSprite;
    [SerializeField] private Sprite harvestHoverSprite;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask groundLayerMask;
    [SerializeField] private float maxGroundRaycastDistance = 200f;

    public Image hoverImage;
    private bool hovering = false;

    [SerializeField] private TroopSpawner troopSpawner;
    private InputSystem_Actions controls;

    private Vector2 mousePosition;

    private TroopType currentTroopType;

    // po stisknutí levého tlačítka jde ikona posouvat, a vtáhnout do požadovaného políčka, indexu v troop groupu a po uvolnění
    // levého tlačítka se snaží server spawnout tu jednotku, jestli to je možné, tak se změní obrázek na ten aktuálně zvolený

    [SerializeField] private GraphicRaycaster graphicRaycaster;
    [SerializeField] private EventSystem eventSystem;

    private PointerEventData pointerData;
    private List<RaycastResult> results = new List<RaycastResult>();
    private bool missingUiReferencesLogged;
    private GroundOrder pendingGroundOrder;

    private void Awake()
    {
        ResolveUiReferences();
    }

    private bool ResolveUiReferences()
    {
        // Player is spawned at runtime, so a scene EventSystem cannot be
        // assigned to this prefab asset in the Inspector.
        if (eventSystem == null)
            eventSystem = EventSystem.current;

        if (graphicRaycaster == null)
            graphicRaycaster = GetComponentInChildren<GraphicRaycaster>(true);

        if (eventSystem == null || graphicRaycaster == null)
        {
            if (!missingUiReferencesLogged)
            {
                Debug.LogError("TroopUIController needs an active EventSystem and a GraphicRaycaster to detect troop UI drops.", this);
                missingUiReferencesLogged = true;
            }
            return false;
        }

        if (pointerData == null)
            pointerData = new PointerEventData(eventSystem);

        missingUiReferencesLogged = false;
        return true;
    }

    public GameObject GetUIAtPosition(Vector2 screenPosition)
    {
        if (!ResolveUiReferences())
            return null;

        pointerData.position = screenPosition;

        results.Clear();
        graphicRaycaster.Raycast(pointerData, results);

        if (results.Count > 0)
        {
            return results[0].gameObject;
        }
        else if (results.Count == 1)
        {
            return results[0].gameObject;
        }

        return null;
    }
    
    public void SetSelectedTroopGroup(TroopGroup troopGroup)
    {
        if(selectedTroopGroup != null)
            selectedTroopGroup.UnitDespawned -= TroopDeleted;
        selectedTroopGroup = troopGroup;
        if (selectedTroopGroup == null) return;
        UpdateTroopUI();
        selectedTroopGroup.UnitDespawned += TroopDeleted;
    }

    public void SetSelectedTroopGroupById(int groupId)
    {
        TroopInteractor interactor = GetComponent<TroopInteractor>();
        if (interactor == null) interactor = GetComponentInParent<TroopInteractor>();
        if (interactor == null)
        {
            Debug.LogWarning("TroopUIController cannot request a troop group without a TroopInteractor.", this);
            return;
        }
        interactor.RequestActivateGroup(groupId);
    }

    public void RegisterTroopGroup(TroopGroup troopGroup)
    {
        if (troopSpawner == null) troopSpawner = GetComponent<TroopSpawner>();
        if (troopSpawner != null) troopSpawner.RegisterTroopGroup(troopGroup);
    }

    private Sprite GetTroopSprite(TroopType troopType)
    {
        if (troopSprites == null)
            return null;

        foreach (var troopSprite in troopSprites)
        {
            if (troopSprite.troopType == troopType)
            {
                return troopSprite.troopSprite;
            }
        }
        return null;
    }

    private TroopSlot GetTroopSlot(int spriteIndex)
    {
        return troopSlots == null
            ? null
            : troopSlots.Find(slot => slot != null && slot.SlotIndex == spriteIndex);
    }

    private void UpdateTroopUI()
    {
        if (selectedTroopGroup == null)
            return;

        foreach (var troopSlot in troopSlots)
        {
            if (troopSlot != null && troopSlot.icon != null)
            {
                troopSlot.icon.sprite = null;
                troopSlot.hasTroop = false;
            }
                troopSlot.icon.gameObject.SetActive(troopSlot.hasTroop);
        }
        // Aktualizace obrázků jednotek podle vybrané skupiny
        foreach (var troop in selectedTroopGroup.Units)
        {
            TroopSlot troopSlot = GetTroopSlot(troop.groupSpriteIndex);
            Sprite troopSprite = GetTroopSprite(troop.Type);

            if (troopSlot != null && troopSlot.icon != null)
            {
                troopSlot.icon.sprite = troopSprite;
                troopSlot.hasTroop = true;
                troopSlot.icon.gameObject.SetActive(true);
            }
            
        }
    }

    public void TroopHovering(TroopType troopType)
    {
        if (hoverImage != null)
        {
            Sprite troopSprite = GetTroopSprite(troopType);
            if (troopSprite != null)
            {
                hoverImage.sprite = troopSprite;
                hoverImage.gameObject.SetActive(true);
                hovering = true;
                currentTroopType = troopType;
            }
        }
    }

    public void MouseMoved(Vector2 position)
    {
        mousePosition = position;
        if (hovering)
        {
            hoverImage.transform.position = mousePosition;
        }
    }

    public void MouseCursorPressed()
    {
        GameObject uiElement = GetUIAtPosition(mousePosition);
        if (uiElement != null)
        {
            TroopSpawnButton troopButton = uiElement.GetComponent<TroopSpawnButton>();
            if (troopButton != null)
            {
                TroopType troopType = troopButton.troopType;
                TroopHovering(troopType);
            }
        }
    }

    public void MouseCursorReleased()
    {
        if (pendingGroundOrder != GroundOrder.None)
        {
            CompleteGroundOrder();
            return;
        }

        GameObject uiElement = GetUIAtPosition(mousePosition);
        if (uiElement != null)
        {
            TroopSlot troopSlot = uiElement.GetComponent<TroopSlot>();
            if (troopSlot != null && hovering)
            {
                // Zde můžete implementovat logiku pro spawnování jednotky na serveru
                // Například zavolat metodu na serveru s typem jednotky a indexem slotu
                Debug.Log($"Spawning troop of type {hoverImage.sprite.name} in slot.");
                if (selectedTroopGroup == null || troopSpawner == null)
                    return;
                troopSpawner.RequestSpawnTroopOfType(currentTroopType, selectedTroopGroup, troopSlot.SlotIndex);


            }
        }
        if (hovering)
        {
            hoverImage.gameObject.SetActive(false);
            hovering = false;
        }
    }

    /// <summary>Začne výběr cílového bodu pro rozkaz bránit.</summary>
    public void Defend() => BeginGroundOrder(GroundOrder.Defend, defendHoverSprite);

    /// <summary>Začne výběr cílového bodu pro rozkaz útočit.</summary>
    public void Attack() => BeginGroundOrder(GroundOrder.Attack, attackHoverSprite);

    /// <summary>Začne výběr cílového bodu pro rozkaz těžit.</summary>
    public void Harvest() => BeginGroundOrder(GroundOrder.Harvest, harvestHoverSprite);

    private void BeginGroundOrder(GroundOrder order, Sprite cursorSprite)
    {
        if (selectedTroopGroup == null)
        {
            Debug.LogWarning($"Nelze zadat rozkaz {order}: není vybraná troop group.", this);
            return;
        }
        if (hoverImage == null || cursorSprite == null)
        {
            Debug.LogWarning($"Nelze zadat rozkaz {order}: hover image nebo jeho sprite není nastavený.", this);
            return;
        }

        pendingGroundOrder = order;
        hovering = false;
        hoverImage.sprite = cursorSprite;
        hoverImage.gameObject.SetActive(true);
        hoverImage.transform.position = mousePosition;
    }

    private void CompleteGroundOrder()
    {
        // Spotřebuj klik i při kliknutí na UI nebo mimo zem, aby režim nezůstal viset.
        GroundOrder order = pendingGroundOrder;
        pendingGroundOrder = GroundOrder.None;
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            HideHoverImage();
            return;
        }

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null || !Physics.Raycast(mainCamera.ScreenPointToRay(mousePosition), out RaycastHit hit,
                maxGroundRaycastDistance, groundLayerMask))
        {
            HideHoverImage();
            return;
        }

        TroopGroup group = selectedTroopGroup;
        HideHoverImage();
        if (group == null) return;

        switch (order)
        {
            case GroundOrder.Defend:
                group.SetTask(TroopGroupTask.Defend, hit.point);
                break;
            case GroundOrder.Attack:
                group.SetTask(TroopGroupTask.Attack, hit.point);
                break;
            case GroundOrder.Harvest:
                group.SetTask(TroopGroupTask.Harvest, hit.point);
                break;
        }
    }

    private void HideHoverImage()
    {
        if (hoverImage != null) hoverImage.gameObject.SetActive(false);
        hovering = false;
    }

    private void TroopSpawnRejected(int groupId, int spriteIndex)
    {
        // Rejection can arrive after the player selected another group. Reconcile
        // the requested slot with the actual group contents.
        if (selectedTroopGroup == null || selectedTroopGroup.GroupId != groupId)
            return;

        TroopSlot rejectedSlot = GetTroopSlot(spriteIndex);
        if (rejectedSlot == null || rejectedSlot.icon == null)
            return;

        Troop existingTroop = null;
        foreach (Troop troop in selectedTroopGroup.Units)
        {
            if (troop != null && troop.groupSpriteIndex == spriteIndex)
            {
                existingTroop = troop;
                break;
            }
        }

        rejectedSlot.hasTroop = existingTroop != null;
        rejectedSlot.icon.sprite = existingTroop != null ? GetTroopSprite(existingTroop.Type) : null;
        rejectedSlot.icon.gameObject.SetActive(existingTroop != null);
    }

    private void TroopSpawnAccepted(int groupId, int spriteIndex, TroopType troopType)
    {
        if (selectedTroopGroup == null || selectedTroopGroup.GroupId != groupId)
            return;

        TroopSlot acceptedSlot = GetTroopSlot(spriteIndex);
        if (acceptedSlot == null || acceptedSlot.icon == null)
            return;

        acceptedSlot.icon.sprite = GetTroopSprite(troopType);
        acceptedSlot.hasTroop = true;
        acceptedSlot.icon.gameObject.SetActive(true);
    }

    private void TroopDeleted(Troop troop)
    {
        if (troop == null || selectedTroopGroup == null)
            return;

        if (troop.Group == selectedTroopGroup)
        {
            TroopSlot troopSlot = GetTroopSlot(troop.groupSpriteIndex);
            if (troopSlot != null && troopSlot.icon != null)
            {
                troopSlot.icon.sprite = null;
                troopSlot.icon.gameObject.SetActive(false);
            }
        }
    }

    void Start()
    {
        hoverImage.gameObject.SetActive(false);

        troopSpawner = GetComponent<TroopSpawner>();
        if(troopSpawner == null)
        {
            Debug.LogError("TroopSpawner reference is not set in TroopUIController.");
        }
        if (troopSpawner != null)
        {
            troopSpawner.SpawnRequestRejected += TroopSpawnRejected;
            troopSpawner.SpawnRequestAccepted += TroopSpawnAccepted;
        }

        StartCoroutine(WaitForInputManager());
    }

    private void OnDestroy()
    {
        if (troopSpawner != null)
        {
            troopSpawner.SpawnRequestRejected -= TroopSpawnRejected;
            troopSpawner.SpawnRequestAccepted -= TroopSpawnAccepted;
        }
        if (selectedTroopGroup != null)
            selectedTroopGroup.UnitDespawned -= TroopDeleted;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private IEnumerator WaitForInputManager()
    {
        yield return new WaitUntil(() => InputManager.StaticClass != null);
        if (InputManager.StaticClass != null && InputManager.StaticClass.Controls != null)
        {
            controls = InputManager.StaticClass.Controls;
            controls.UI.Point.performed += ctx => MouseMoved(ctx.ReadValue<Vector2>());
            controls.UI.Click.performed += ctx => MouseCursorPressed();
            controls.UI.Click.canceled += OnClickReleased;
        }
    }

    private void OnClickReleased(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
    {
        MouseCursorReleased();
    }
}
