using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UpgradePopup : MonoBehaviour
{
    [Header("Text Elements")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text typeText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text effectText;

    [Header("Buttons")]
    [SerializeField] private Button buttonIcon;
    [SerializeField] private Button buttonExit;

    [Header("Icon")]
    [Tooltip("Image ikony umístěná uvnitř tlačítka Button Icon.")]
    [SerializeField] private Image imageIcon;

    [Header("Popup Content")]
    [Tooltip("Parent všech prvků rozbaleného panelu kromě tlačítka Button Icon.")]
    [SerializeField] private GameObject emptyParent;

    public bool IsVisible { get; private set; }

    private UpgradeDefinition currentUpgrade;

    private void Awake()
    {
        if (buttonIcon != null)
        {
            buttonIcon.onClick.AddListener(OpenPopup);
        }

        if (buttonExit != null)
        {
            buttonExit.onClick.AddListener(ClosePopup);
        }

        // Při spuštění zobrazíme pouze tlačítko s ikonou.
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (buttonIcon != null)
        {
            buttonIcon.onClick.RemoveListener(OpenPopup);
        }

        if (buttonExit != null)
        {
            buttonExit.onClick.RemoveListener(ClosePopup);
        }
    }

    /// <summary>
    /// Načte data vylepšení a vyplní příslušné UI prvky.
    /// Po naplnění zůstane panel zavřený.
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

            ClearTextFields();

            if (imageIcon != null)
            {
                imageIcon.sprite = null;
                imageIcon.enabled = false;
            }

            SetVisible(false);
            return;
        }

        currentUpgrade = data;

        // Název vylepšení.
        if (nameText != null)
        {
            nameText.text = data.DisplayName;
        }

        // Typ cíle, na který vylepšení působí.
        if (typeText != null)
        {
            typeText.text = GetTargetDescription(data);
        }

        // Popis vylepšení.
        if (descriptionText != null)
        {
            descriptionText.text = data.Description;
        }

        // Konkrétní efekt a jeho hodnota.
        if (effectText != null)
        {
            effectText.text = GetEffectDescription(data);
        }

        // Ikona vylepšení.
        if (imageIcon != null)
        {
            imageIcon.sprite = data.Icon;
            imageIcon.enabled = data.Icon != null;
        }

        // Po naplnění zobrazíme opět pouze tlačítko s ikonou.
        SetVisible(false);

        Debug.Log(
            $"UpgradePopup: Načteno vylepšení '{data.DisplayName}'.",
            this
        );
    }

    /// <summary>
    /// Přepne viditelnost rozbaleného panelu.
    /// true = zobrazí detaily, false = zobrazí pouze ikonu.
    /// </summary>
    public void SetVisible(bool visible)
    {
        IsVisible = visible;

        if (emptyParent != null)
        {
            emptyParent.SetActive(visible);
        }

        if (buttonIcon != null)
        {
            buttonIcon.gameObject.SetActive(!visible);
        }
    }

    /// <summary>
    /// Otevře rozbalený panel s podrobnostmi.
    /// </summary>
    public void OpenPopup()
    {
        SetVisible(true);
    }

    /// <summary>
    /// Zavře rozbalený panel a vrátí tlačítko s ikonou.
    /// </summary>
    public void ClosePopup()
    {
        SetVisible(false);
    }

    private string GetTargetDescription(UpgradeDefinition data)
    {
        switch (data.TargetType)
        {
            case UpgradeTargetType.Troop:
                return "Jednotka: " +
                       FormatEnumName(data.TroopType.ToString());

            case UpgradeTargetType.Building:
                return "Budova: " +
                       FormatEnumName(data.BuildingType.ToString());

            default:
                return "Neznámý cíl";
        }
    }

    private string GetEffectDescription(UpgradeDefinition data)
    {
        string statName = GetStatDisplayName(data.StatType);
        string value = FormatValue(data.Value, data.ValueType);

        return $"{statName}: {value}";
    }

    private string GetStatDisplayName(UpgradeStatType statType)
    {
        switch (statType)
        {
            case UpgradeStatType.MaxHealth:
                return "Maximální zdraví";

            case UpgradeStatType.Damage:
                return "Poškození";

            case UpgradeStatType.AttackRange:
                return "Dosah útoku";

            case UpgradeStatType.AttackSpeed:
                return "Rychlost útoku";

            case UpgradeStatType.MovementSpeed:
                return "Rychlost pohybu";

            case UpgradeStatType.SightRange:
                return "Dosah vidění";

            case UpgradeStatType.BuildSpeed:
                return "Rychlost stavby";

            case UpgradeStatType.RepairPower:
                return "Síla oprav";

            case UpgradeStatType.MiningSpeed:
                return "Rychlost těžby";

            case UpgradeStatType.CargoCapacity:
                return "Kapacita nákladu";

            default:
                return FormatEnumName(statType.ToString());
        }
    }

    private string FormatValue(float value, UpgradeValueType valueType)
    {
        // Kladné hodnoty budou mít znaménko +.
        string formattedValue = value.ToString(
            "+0.##;-0.##;0",
            CultureInfo.CurrentCulture
        );

        switch (valueType)
        {
            case UpgradeValueType.Percentage:
                return formattedValue + " %";

            case UpgradeValueType.Flat:
            default:
                return formattedValue;
        }
    }

    /// <summary>
    /// Převede například MaxHealth na Max Health
    /// nebo LaserTower na Laser Tower.
    /// </summary>
    private string FormatEnumName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        System.Text.StringBuilder result =
            new System.Text.StringBuilder();

        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];

            if (i > 0 &&
                char.IsUpper(current) &&
                (char.IsLower(value[i - 1]) ||
                 char.IsDigit(value[i - 1])))
            {
                result.Append(' ');
            }

            result.Append(current);
        }

        return result.ToString();
    }

    private void ClearTextFields()
    {
        if (nameText != null)
            nameText.text = "";

        if (typeText != null)
            typeText.text = "";

        if (descriptionText != null)
            descriptionText.text = "";

        if (effectText != null)
            effectText.text = "";
    }
}