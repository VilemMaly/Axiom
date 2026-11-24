using UnityEngine;
using Unity.Netcode;

public class Radar : Building
{
    public override BuildingType Type => BuildingType.Radar;

    [Header("Radar specific")]
    [SerializeField] private float revealRadius = 25f;

    // Vlastní stav radaru synchronizuje dostupnost energie i s klienty.
    public NetworkVariable<bool> HasEnoughElectricity = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float normalSightRange;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Base OnNetworkSpawn může aplikovat výzkumné bonusy k dosahu.
        normalSightRange = SightRange;
        HasEnoughElectricity.OnValueChanged += OnElectricityValueChanged;
        ApplySightRange(HasEnoughElectricity.Value);
    }

    public override void OnNetworkDespawn()
    {
        HasEnoughElectricity.OnValueChanged -= OnElectricityValueChanged;
        base.OnNetworkDespawn();
    }

    protected override void OnElectricityAvailabilityChanged(bool hasEnoughEnergy)
    {
        if (!IsServer)
            return;

        if (HasEnoughElectricity.Value != hasEnoughEnergy)
            HasEnoughElectricity.Value = hasEnoughEnergy;

        // Server si také nastavuje vlastní lokální dosah.
        ApplySightRange(hasEnoughEnergy);
    }

    private void OnElectricityValueChanged(bool previousValue, bool newValue)
    {
        // Klienti aktualizují lokální sight range podle síťového stavu radaru.
        if (!IsServer)
            ApplySightRange(newValue);
    }

    private void ApplySightRange(bool hasEnoughEnergy)
    {
        SightRange = hasEnoughEnergy ? normalSightRange : 0f;

        if (visibilityMap != null)
        {
            float mapScale = SightRange / 2f;
            visibilityMap.transform.localScale = new Vector3(mapScale, mapScale, mapScale);
        }
    }

    public override void OnBuilt()
    {
        // TODO: odkrýt mlhu války v revealRadius kolem transform.position
        // Pokud máš FogOfWar systém, zavolej ho tady, typicky jen pro ownera:
        // FogOfWarManager.Instance.RevealArea(transform.position, revealRadius, OwnerClientId);
    }

    // TODO: volitelně periodická kontrola OverlapSphere pro odhalení
    // neviditelných/stealth jednotek nepřítele v dosahu
}
