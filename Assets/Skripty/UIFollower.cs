using UnityEngine;

public class UIFollower : MonoBehaviour
{
    [SerializeField] private RectTransform panelContainer; // rodič, který obsahuje všechny building panely
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 2f, 0f);

    private Transform target;

    public void Start()
    {
        Hide();
    }
    public void ShowFor(Transform buildingTransform)
    {
        target = buildingTransform;
        panelContainer.gameObject.SetActive(true);
    }

    public void Hide()
    {
        target = null;
        panelContainer.gameObject.SetActive(false);
    }

    public void LateUpdate()
    {
        if (target == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(target.position + worldOffset);
        panelContainer.position = screenPos;
    }
}