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
    public bool BuildingOn = false;
    private ResearchUiController researchUiController;

    [SerializeField] private GameObject buildingsPanel;

    [Header("Troop UI")]
    [SerializeField] private GameObject troopPanel;
    public bool TroopOn = false;

    private void Start()
    {
        // Nastavení počáteční viditelnosti UI panelů.
        SetUIVisibility(isVisible);
        SetResearchVisibility(isResearchVisible);

        if (buildingsPanel != null)
            buildingsPanel.SetActive(BuildingOn);

        if (troopPanel != null)
            troopPanel.SetActive(TroopOn);

        if (!IsOwner)
            return;

        researchUiController = GetComponent<ResearchUiController>();
    }

    // Zapne nebo vypne World UI panel podle bool hodnoty.
    public void SetUIVisibility(bool visible)
    {
        isVisible = visible;

        if (uiPanel != null)
            uiPanel.gameObject.SetActive(isVisible);
    }

    // Přepne Building panel.
    public void ToggleBuilding()
    {
        BuildingOn = !BuildingOn;

        if (buildingsPanel != null)
            buildingsPanel.SetActive(BuildingOn);
    }

    // Přepne Troop panel.
    public void ToggleTroop()
    {
        TroopOn = !TroopOn;

        if (troopPanel != null)
            troopPanel.SetActive(TroopOn);
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
            researchParent.SetActive(isResearchVisible);
    }

    // Přepne viditelnost Research panelu.
    public void ToggleResearchVisibility()
    {
        SetResearchVisibility(!isResearchVisible);
        ToggleUIVisibility();
    }
}