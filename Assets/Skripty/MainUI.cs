
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
    public bool isVisible = false;
    private Vector3 position = Vector3.zero;

    [Header("Research UI")]
    public GameObject researchParent;
    public bool isResearchVisible = false;
    private ResearchUiController researchUiController;

    private void Start()
    {
        // Nastavení počáteční viditelnosti UI panelů.
        SetUIVisibility(isVisible);
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
    }

    public void uiClose(InputAction.CallbackContext context)
    {
        uiClose();
    }

    public void uiClose()
    {
        SetUIVisibility(false);
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

        SetUIVisibility(!isVisible);
    }

    // Zapne nebo vypne World UI panel podle bool hodnoty.
    public void SetUIVisibility(bool visible)
    {
        isVisible = visible;

        if (uiPanel != null)
        {
            uiPanel.gameObject.SetActive(isVisible);
        }
    }

    // Přepne viditelnost World UI panelu.
    public void ToggleUIVisibility()
    {
        SetUIVisibility(!isVisible);
    }

    // Zapne nebo vypne celý Research panel.
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
