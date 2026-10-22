using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.InputSystem;
using System.Collections;

public class MainUI : NetworkBehaviour
{
    [Header("World UI")]
    public RectTransform uiPanel;
    public bool isVisible = true;

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

        researchUiController = GetComponent<ResearchUiController>();
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
        ToggleUIVisibility();
    }
}
