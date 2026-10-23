
using System;
using UnityEngine;
using UnityEngine.UI;

public class DoctrinePopup : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler
{
    [Header("Button")]
    [SerializeField] private Button iconButton;

    [Header("Icon")]
    [SerializeField] private Image iconImage;

    public event Action<DoctrineDefinition> Opened;
    public event Action<DoctrineDefinition> Hovered;

    public bool IsVisible { get; private set; }

    private DoctrineDefinition currentDoctrine;

    private void Awake()
    {
        if (iconButton != null)
            iconButton.onClick.AddListener(IconClick);
    }

    private void OnDestroy()
    {
        if (iconButton != null)
            iconButton.onClick.RemoveListener(IconClick);
    }

    private void IconClick()
    {
        Opened?.Invoke(currentDoctrine);
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData) => Hovered?.Invoke(currentDoctrine);

    /// <summary>
    /// Nastaví doktrínu, kterou lze následně získat po kliknutí.
    /// </summary>
    public void Fill(DoctrineDefinition data)
    {
        if (data == null)
        {
            Debug.LogError(
                "DoctrinePopup: Fill obdržel null místo DoctrineDefinition.",
                this
            );

            currentDoctrine = null;

            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }

            return;
        }

        currentDoctrine = data;

        if (iconImage != null)
        {
            iconImage.sprite = data.Icon;
            iconImage.enabled = data.Icon != null;
        }
    }
}
