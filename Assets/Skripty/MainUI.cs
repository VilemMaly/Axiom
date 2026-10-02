//Attach this script to a GameObject. Attach a Renderer and Button component to the same GameObject for this example.
//This script will change the Color of the GameObject as well as output messages to the Console saying which function was run by the UnityAction.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System;
using System.Collections;
using Unity.VisualScripting;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class MainUI : NetworkBehaviour
{
    public RectTransform uiPanel;
    private bool isVisible = false;
    private Vector3 position = Vector3.zero;

    void Start()
    {
        if(!IsOwner)
            return;
        StartCoroutine(WaitForInputManager());
    }

    private IEnumerator WaitForInputManager()
    {
        yield return new WaitUntil(() => InputManager.StaticClass != null);
        InputManager.StaticClass.Controls.UI.RightClick.started += uiClicked;
        InputManager.StaticClass.Controls.UI.RightClick.canceled += uiClose;
    }
    public void Update()
    {
        if(isVisible)
        {
            Vector3 screenPos = Camera.main.WorldToScreenPoint(position + new Vector3(0,2,0));
            uiPanel.position = screenPos;
        }
        
    }
    public void uiClose(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        isVisible = false;
        uiPanel.GameObject().SetActive(isVisible);
    }

    public void uiClose()
    {
        isVisible = false;
        uiPanel.GameObject().SetActive(isVisible);
    }

    private void uiClicked(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        position = Vector3.zero;
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                position = hit.point;
            }
        isVisible = !isVisible;
        uiPanel.GameObject().SetActive(isVisible);
    }

}
