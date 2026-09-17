using TMPro;
using Unity.Netcode;
using UnityEngine;

public class PlayerResources : NetworkBehaviour
{
    public NetworkVariable<int> Corium = new NetworkVariable<int>(100);
    public NetworkVariable<int> Energy = new NetworkVariable<int>(50);
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

        Corium.OnValueChanged += (oldVal, newVal) => UpdateCoriumText(newVal);
        Energy.OnValueChanged += (oldVal, newVal) => UpdateEnergyText(newVal);

        UpdateCoriumText(Corium.Value);
        UpdateEnergyText(Energy.Value);
    }

    void UpdateCoriumText(int value) => coriumText.text = $"Corium: {value}";
    void UpdateEnergyText(int value) => energyText.text = $"Energy: {value}";

    public bool TrySpend(int coriumCost, int energyCost)
    {
        if (Corium.Value < coriumCost || Energy.Value < energyCost)
            return false;

        Corium.Value -= coriumCost;
        Energy.Value -= energyCost;
        return true;
    }

    public void Add(int corium, int energy)
    {
        Corium.Value += corium;
        Energy.Value += energy;
    }
}