using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DoctrinePopup : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button iconButton;
    [SerializeField] private Image iconImage;

    [SerializeField] private GameObject detailsPanel;

    [SerializeField] private Button acceptButton;
    [SerializeField] private Button declineButton;

    [SerializeField] private TMP_Text doctrineNameText;
    [SerializeField] private TMP_Text doctrineDescriptionText;

    [Header("Behavior")]
    [SerializeField] private bool destroyAfterAccept = true;

    private DoctrineDefinition currentDoctrine;
    private bool decisionMade;

    /// <summary>
    /// True, pokud je karta rozbalená.
    /// </summary>
    public bool IsExpanded { get; private set; }

    /// <summary>
    /// První parametr = vybraná doktrína.
    /// Druhý parametr = true při přijetí, false při odmítnutí.
    /// </summary>
    public event Action<DoctrineDefinition, bool> OnDecisionMade;

    private void Awake()
    {
        // Pokud Image nebyl přiřazen v Inspectoru,
        // automaticky ho zkusíme najít na tlačítku Icon.
        if (iconImage == null && iconButton != null)
        {
            iconImage = iconButton.GetComponent<Image>();
        }
    }

    private void OnEnable()
    {
        if (iconButton != null)
        {
            iconButton.onClick.RemoveListener(ToggleDetails);
            iconButton.onClick.AddListener(ToggleDetails);
        }

        if (acceptButton != null)
        {
            acceptButton.onClick.RemoveListener(HandleAccept);
            acceptButton.onClick.AddListener(HandleAccept);
        }

        if (declineButton != null)
        {
            declineButton.onClick.RemoveListener(HandleDecline);
            declineButton.onClick.AddListener(HandleDecline);
        }

        SetExpanded(false);
        UpdateButtonStates();
    }

    private void OnDisable()
    {
        if (iconButton != null)
            iconButton.onClick.RemoveListener(ToggleDetails);

        if (acceptButton != null)
            acceptButton.onClick.RemoveListener(HandleAccept);

        if (declineButton != null)
            declineButton.onClick.RemoveListener(HandleDecline);
    }

    /// <summary>
    /// Naplní kartu daty vybrané doktríny.
    /// </summary>
    public void Fill(DoctrineDefinition doctrine)
    {
        currentDoctrine = doctrine;
        decisionMade = false;

        if (currentDoctrine == null)
        {
            Debug.LogError(
                "DoctrineSelector: Byla předána neplatná doktrína.",
                this
            );

            if (doctrineNameText != null)
                doctrineNameText.text = string.Empty;

            if (doctrineDescriptionText != null)
                doctrineDescriptionText.text = string.Empty;

            if (iconImage != null)
                iconImage.sprite = null;

            SetExpanded(false);
            UpdateButtonStates();
            return;
        }

        // Nastavení názvu a popisu doktríny.
        if (doctrineNameText != null)
            doctrineNameText.text = doctrine.DisplayName;

        if (doctrineDescriptionText != null)
            doctrineDescriptionText.text = doctrine.Description;

        // Načtení ikony z DoctrineDefinition.
        if (iconImage != null)
        {
            iconImage.sprite = doctrine.Icon;
            iconImage.enabled = doctrine.Icon != null;
        }
        else
        {
            Debug.LogWarning(
                "DoctrineSelector: Není přiřazen Image pro ikonu doktríny.",
                this
            );
        }

        SetExpanded(false);
        UpdateButtonStates();
    }

    /// <summary>
    /// Přepne mezi zavřenou a rozbalenou kartou.
    /// </summary>
    private void ToggleDetails()
    {
        if (currentDoctrine == null || decisionMade)
            return;

        SetExpanded(!IsExpanded);
    }

    /// <summary>
    /// Změní viditelnost ikony a detailního panelu.
    /// </summary>
    private void SetExpanded(bool expanded)
    {
        IsExpanded = expanded;

        if (iconButton != null)
            iconButton.gameObject.SetActive(!expanded);

        if (detailsPanel != null)
            detailsPanel.SetActive(expanded);
    }

    /// <summary>
    /// Přijetí doktríny.
    /// </summary>
    private void HandleAccept()
    {
        if (currentDoctrine == null || decisionMade)
            return;

        decisionMade = true;

        SetExpanded(false);
        UpdateButtonStates();

        OnDecisionMade?.Invoke(currentDoctrine, true);

        if (destroyAfterAccept)
            Destroy(gameObject);
    }

    /// <summary>
    /// Odmítnutí doktríny / zavření detailního panelu.
    /// </summary>
    private void HandleDecline()
    {
        if (currentDoctrine == null || decisionMade)
            return;

        OnDecisionMade?.Invoke(currentDoctrine, false);

        // Zavře panel a znovu zobrazí tlačítko Icon.
        SetExpanded(false);
    }

    private void UpdateButtonStates()
    {
        bool canInteract = currentDoctrine != null && !decisionMade;

        if (iconButton != null)
            iconButton.interactable = canInteract;

        if (acceptButton != null)
            acceptButton.interactable = canInteract;

        if (declineButton != null)
            declineButton.interactable = canInteract;
    }
}