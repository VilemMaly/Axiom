using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Plain (non-networked) MonoBehaviour that sits next to the RawImage showing
/// the minimap camera's RenderTexture. It only converts a UI click into a
/// normalized 0..1 coordinate on the image and raises a local C# event.
///
/// Deliberately NOT a NetworkBehaviour and NOT on the player prefab:
/// this way it exists on every client's canvas from scene load, regardless
/// of whether that client's player object has spawned yet. Whoever needs
/// the network round-trip (GameSpawner) subscribes to this event and does
/// the ServerRpc call itself.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MinimapClickUI : MonoBehaviour, IPointerClickHandler
{
    public static event Action<Vector2> OnMinimapClicked;

    RectTransform rectTransform;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out var localPoint))
            return;

        Rect rect = rectTransform.rect;

        // Normalize to 0..1 across the RawImage regardless of its size/pivot.
        float u = (localPoint.x - rect.x) / rect.width;
        float v = (localPoint.y - rect.y) / rect.height;

        // Clamp defensively - clicks right on the border can land a hair outside 0..1.
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);

        Debug.Log($"[MinimapClickUI] Clicked at normalized UV {u:F3}, {v:F3} on minimap.");
        OnMinimapClicked?.Invoke(new Vector2(u, v));
    }
}