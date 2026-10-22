using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UpgradePopup : MonoBehaviour
{

    [Header("Buttons")]
    [SerializeField] private Button buttonIcon;

    [Header("Icon")]
    [Tooltip("Image ikony umístěná uvnitř tlačítka Button Icon.")]
    [SerializeField] private Image imageIcon;
    public event Action<UpgradeDefinition> Opened;

    public bool IsVisible { get; private set; }

    private UpgradeDefinition currentUpgrade;

    private void Awake()
    {
        if (buttonIcon != null)
        {
            buttonIcon.onClick.AddListener(IconClick);
        }
    }

    private void OnDestroy()
    {
        if (buttonIcon != null)
        {
            buttonIcon.onClick.RemoveListener(IconClick);
        }
    }

    private void IconClick()
    {
        Opened?.Invoke(currentUpgrade);
    }

    /// <summary>
    /// Fill jen nastaví typ tohoto upgradu, ať jde potom získat když se na něj klikne
    /// </summary>
    public void Fill(UpgradeDefinition data)
    {
        if (data == null)
        {
            Debug.LogError(
                "UpgradePopup: Fill obdržel null místo UpgradeDefinition.",
                this
            );

            currentUpgrade = null;

            if (imageIcon != null)
            {
                imageIcon.sprite = null;
                imageIcon.enabled = false;
            }
            return;
        }

        currentUpgrade = data;
        buttonIcon.image.sprite = data.Icon;

    }

}