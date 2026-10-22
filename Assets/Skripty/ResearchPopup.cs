
using System;
using UnityEngine;
using UnityEngine.UI;

public class ResearchPopup : MonoBehaviour
{
    [Header("Button")]
    [SerializeField] private Button iconButton;

    [Header("Icon")]
    [SerializeField] private Image iconImage;

    public event Action<ResearchDefinition> Opened;

    public bool IsVisible { get; private set; }

    private ResearchDefinition currentResearch;

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
        Opened?.Invoke(currentResearch);
    }

    /// <summary>
    /// Nastaví výzkum, který lze následně získat po kliknutí.
    /// </summary>
    public void Fill(ResearchDefinition data)
    {
        if (data == null)
        {
            Debug.LogError(
                "ResearchPopup: Fill obdržel null místo ResearchDefinition.",
                this
            );

            currentResearch = null;

            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }

            return;
        }

        currentResearch = data;

        if (iconImage != null)
        {
            iconImage.sprite = data.Icon;
            iconImage.enabled = data.Icon != null;
        }
    }
}
