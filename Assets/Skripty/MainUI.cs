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
    private InputSystem_Actions controls;

    [SerializeField] private GameObject buildingsPanel;

    [Header("Troop UI")]
    [SerializeField] private GameObject troopPanel;
    public bool TroopOn = false;

    public override void OnNetworkSpawn()
    {
        // Nastavení počáteční viditelnosti UI panelů.
        SetUIVisibility(isVisible);
        if (isResearchVisible)
            SetActiveTab("research");
        else if (BuildingOn)
            SetActiveTab("buildings");
        else if (TroopOn)
            SetActiveTab("troop");
        else
            SetActiveTab(null);


        if (!IsOwner)
            return;

        researchUiController = GetComponent<ResearchUiController>();
        StartCoroutine(WaitForInputManager());
    }

    private IEnumerator WaitForInputManager()
    {
        yield return new WaitUntil(() => InputManager.StaticClass != null);
        if (InputManager.StaticClass != null && InputManager.StaticClass.Controls != null)
        {
            controls = InputManager.StaticClass.Controls;
            controls.UI.Exit.performed += OnExitPerformed;
        }
    }
    private void OnDestroy()
    {
        if (controls != null)
            controls.UI.Exit.performed -= OnExitPerformed;
    }

    private void OnExitPerformed(InputAction.CallbackContext context)
    {
        if (isResearchVisible)
            ToggleResearchVisibility();
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
        SetActiveTab(BuildingOn ? null : "buildings");
    }

    // Přepne Troop panel.
    public void ToggleTroop()
    {
        SetActiveTab(TroopOn ? null : "troop");
    }

    // Přepne viditelnost World UI panelu.
    public void ToggleUIVisibility()
    {
        SetUIVisibility(!isVisible);
    }

    // Zapne nebo vypne celý Research panel.
    public void SetResearchVisibility(bool visible)
    {
        if (visible)
            SetActiveTab("research");
        else if (isResearchVisible)
            SetActiveTab(null);
    }

    // Přepne viditelnost Research panelu.
    public void ToggleResearchVisibility()
    {
        SetResearchVisibility(!isResearchVisible);
        ToggleUIVisibility();
    }

    // Udržuje obsahové panely vzájemně výlučné.
    private void SetActiveTab(string tab)
    {
        isResearchVisible = tab == "research";
        BuildingOn = tab == "buildings";
        TroopOn = tab == "troop";

        if (researchParent != null)
            researchParent.SetActive(isResearchVisible);
        if (buildingsPanel != null)
            buildingsPanel.SetActive(BuildingOn);
        if (troopPanel != null)
            troopPanel.SetActive(TroopOn);
    }
}
