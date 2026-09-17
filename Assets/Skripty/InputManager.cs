using UnityEngine;

public class InputManager : MonoBehaviour
{
    public static InputManager StaticClass { get; private set; }

    public InputSystem_Actions Controls { get; private set; }

    private void Awake()
    {
        StaticClass = this;
        Controls = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        Controls.Enable();
    }

    private void OnDisable()
    {
        Controls.Disable();
    }
}