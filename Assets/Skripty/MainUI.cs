using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.InputSystem;
using Unity.VisualScripting;
using System.Collections;

public class MainUI : NetworkBehaviour
{
    [Header("World UI")]
    public RectTransform uiPanel;
    private bool isVisible = false;
    private Vector3 position = Vector3.zero;

    [Header("Research UI")]
    public GameObject researchParent;
    public bool isResearchVisible = false;
    private ResearchUiController researchUiController;

    private void Start()
    {
        // Nastavení počáteční viditelnosti research panelu
        SetResearchVisibility(isResearchVisible);

        if (!IsOwner)
            return;

        StartCoroutine(WaitForInputManager());
        researchUiController = GetComponent<ResearchUiController>();
    }

    private IEnumerator WaitForInputManager()
    {
        yield return new WaitUntil(() => InputManager.StaticClass != null);

        InputManager.StaticClass.Controls.UI.RightClick.started += uiClicked;
        InputManager.StaticClass.Controls.UI.RightClick.canceled += uiClose;
    }

    private void Update()
    {
        if (isVisible && uiPanel != null)
        {
            Vector3 screenPos = Camera.main.WorldToScreenPoint(
                position + new Vector3(0, 2, 0)
            );

            uiPanel.position = screenPos;
        }
    }

    public void uiClose(InputAction.CallbackContext context)
    {
        uiClose();
    }

    public void uiClose()
    {
        isVisible = false;

        if (uiPanel != null)
            uiPanel.GameObject().SetActive(false);
    }

    private void uiClicked(InputAction.CallbackContext context)
    {
        if (Camera.main == null || Mouse.current == null)
            return;

        Ray ray = Camera.main.ScreenPointToRay(
            Mouse.current.position.ReadValue()
        );

        position = Vector3.zero;

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            position = hit.point;
        }

        isVisible = !isVisible;

        if (uiPanel != null)
            uiPanel.GameObject().SetActive(isVisible);
    }

    // Zapne nebo vypne celý Research panel.
    // Po zapnutí panelu se spawnou všechny prefaby doktrín, které jsou v seznamu doktrine types ve skriptu doctrine data,
    // spawnou se podle nastavitelného gridu vedle sebe, na nastavitelný empty parent, než narazí na konec gridu, pak se spawnou pod ně a tak dále.
    // od všech spawnutých doktrín (None se nespawnuje) se začne odebírat event zda li byli stisknuty, a potom se zjistí jaká byla stisknuta,
    // a podle toho se ve skriptu player technology zavolá funkce server rpc o požadavek vybrání doktríny,
    // server ten požadavek zpracuje a zavolá client rpc na všechny hráče, aby se jim přeplo na doktrínu, která byla vybrána,
    // a aby se zobrazily a spawly se všechny researchy, které jsou v seznamu researchů vybrané doktríny, stejně jako se spawnuly doktríny, 
    // jen místo doktrín se spawnou researchy, a od všech researchů se začne odebírat event zda li byli stisknuty, a potom se zjistí jaký byl stisknutý,
    public void SetResearchVisibility(bool visible)
    {
        isResearchVisible = visible;

        if (researchParent != null)
        {
            researchParent.SetActive(isResearchVisible);
        }
    }

    // Přepne viditelnost Research panelu.
    public void ToggleResearchVisibility()
    {
        SetResearchVisibility(!isResearchVisible);
    }
}
