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
    [SerializeField] private List<TroopSprites> troopSprites;
    [SerializeField] private List<TroopSlot> troopSlots;
    TroopGroup selectedTroopGroup;

    public Image hoverImage;
    private bool hovering = false;

    [SerializeField] private TroopSpawner troopSpawner;
    private InputSystem_Actions controls;

    private Vector2 mousePosition;

    private TroopSlot currentSlot;
    private TroopType currentTroopType;

    // po stisknutí levého tlačítka jde ikona posouvat, a vtáhnout do požadovaného políčka, indexu v troop groupu a po uvolnění
    // levého tlačítka se snaží server spawnout tu jednotku, jestli to je možné, tak se změní obrázek na ten aktuálně zvolený

    [SerializeField] private GraphicRaycaster graphicRaycaster;
    [SerializeField] private EventSystem eventSystem;

    private PointerEventData pointerData;
    private List<RaycastResult> results = new List<RaycastResult>();
    private bool missingUiReferencesLogged;

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
        GameObject uiElement = GetUIAtPosition(mousePosition);
        if (uiElement != null)
        {
            TroopSlot troopSlot = uiElement.GetComponent<TroopSlot>();
            if (troopSlot != null && hovering)
            {
                // Zde můžete implementovat logiku pro spawnování jednotky na serveru
                // Například zavolat metodu na serveru s typem jednotky a indexem slotu
                Debug.Log($"Spawning troop of type {hoverImage.sprite.name} in slot.");
                foreach (var troop in selectedTroopGroup.Units)
                {
                    if (troop.groupSpriteIndex == troopSlot.SlotIndex)
                    {
                        Debug.Log($"Slot {troopSlot.SlotIndex} is already occupied by a troop of type {troop.Type}. Cannot spawn another troop here.");
                        return; // Slot is already occupied, do not spawn
                    }
                }
                currentSlot = troopSlot;
                troopSpawner.RequestSpawnTroopOfType(currentTroopType, selectedTroopGroup, troopSlot.SlotIndex);

                // Po úspěšném spawnování můžete aktualizovat ikonu slotu
                currentSlot.icon.sprite = hoverImage.sprite;
                currentSlot.icon.gameObject.SetActive(true);
                currentSlot.hasTroop = true;


            }
        }
        if (hovering)
        {
            hoverImage.gameObject.SetActive(false);
            hovering = false;
        }
    }

    private void TroopSpawnRejected(int groupId, int spriteIndex)
    {
        // Rejection can arrive after the player selected another group or
        // submitted another request. Reconcile the requested slot with the
        // actual group contents instead of relying on mutable currentSlot.
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
            troopSpawner.SpawnRequestRejected += TroopSpawnRejected;

        StartCoroutine(WaitForInputManager());
    }

    private void OnDestroy()
    {
        if (troopSpawner != null)
            troopSpawner.SpawnRequestRejected -= TroopSpawnRejected;
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
            controls.UI.Click.canceled += ctx => MouseCursorReleased();
        }
    }
}
