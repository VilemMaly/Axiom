using TMPro;
using Unity.Netcode;
using UnityEngine;

public class PlayerResources : NetworkBehaviour
{
    public NetworkVariable<int> Corium = new NetworkVariable<int>(100);
    public NetworkVariable<int> Energy = new NetworkVariable<int>(50);
    public NetworkVariable<int> MaxCorium = new NetworkVariable<int>(200);
    public NetworkVariable<int> MaxEnergy = new NetworkVariable<int>(100);
    public NetworkVariable<int> Cores = new NetworkVariable<int>(0);
    public NetworkVariable<int> Atlas = new NetworkVariable<int>(0);

    [Header("HUD refs")]
    [SerializeField] private TMP_Text coriumText;
    [SerializeField] private TMP_Text energyText;
    [SerializeField] private GameObject hudRoot; // celý HUD canvas/panel

    public override void OnNetworkSpawn()
    {
        // HUD vidí jen vlastník, ne soupeř
        if (!IsOwner)
        {
            hudRoot.SetActive(false);
            return;
        }

        Corium.OnValueChanged += (_, _) => UpdateCoriumText();
        Energy.OnValueChanged += (oldVal, newVal) => UpdateEnergyText(newVal);
        MaxCorium.OnValueChanged += (_, _) => UpdateCoriumText();

        UpdateCoriumText();
        UpdateEnergyText(Energy.Value);
    }

    void UpdateCoriumText() => coriumText.text = $"Corium: {Corium.Value} / {MaxCorium.Value}";
    void UpdateEnergyText(int value) => energyText.text = $"Energy: {value}";

    public bool TrySpend(int coriumCost, int energyCost)
    {
        if (coriumCost <= 0 && energyCost <= 0)
        {
            return true; // Není co utratit, takže úspěch
        }
        /*
        Debug.Log($"[PlayerResources] {name} hráč {OwnerClientId} se snaží utratit: " +
            $"{coriumCost} coria, {energyCost} energie. " +
            $"Aktuálně má: {Corium.Value} coria, {Energy.Value} energie.");*/
        if (Corium.Value < coriumCost || Energy.Value < energyCost)
            return false;

        Corium.Value -= coriumCost;
        Energy.Value -= energyCost;
        /*
        Debug.Log($"[PlayerResources] {name} hráč {OwnerClientId} utratil: " +
            $"{coriumCost} coria, {energyCost} energie. " +
            $"Zbývá: {Corium.Value} coria, {Energy.Value} energie.");*/
        return true;
    }

    public void Add(int corium, int energy)
    {
        if(Corium.Value + corium < MaxCorium.Value)
            Corium.Value += corium;
        if(Energy.Value + energy < MaxEnergy.Value)
            Energy.Value += energy;
    }

    /// <summary>
    /// Zvýší maximální kapacitu coria (volá Storage při dostavění).
    /// </summary>
    public void AddMaxCorium(int amount)
    {
        MaxCorium.Value += amount;
        Debug.Log($"[PlayerResources] {name} přidal +{amount} max coria hráči {OwnerClientId}. " +
        $"Nový max: {MaxCorium.Value}");
    }

    public void AddMaxEnergy(int amount)
    {
        MaxEnergy.Value += amount;
        Debug.Log($"[PlayerResources] {name} přidal +{amount} max energie hráči {OwnerClientId}. " +
        $"Nový max: {MaxEnergy.Value}");
    }

    /// <summary>
    /// Sníží maximální kapacitu coria (volá Storage při zničení).
    /// Aktuální corium se ořízne, pokud přesáhne nový limit.
    /// </summary>
    public void RemoveMaxEnergy(int amount)
    {
        MaxEnergy.Value -= amount;
    }

    /// <summary>
    /// Sníží maximální kapacitu coria (volá Storage při zničení).
    /// Aktuální corium se ořízne, pokud přesáhne nový limit.
    /// </summary>
    public void RemoveMaxCorium(int amount)
    {
        MaxCorium.Value = Mathf.Max(0, MaxCorium.Value - amount);
        if (Corium.Value > MaxCorium.Value)
            Corium.Value = MaxCorium.Value;
    }
}