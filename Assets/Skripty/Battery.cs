using UnityEngine;

public class Battery : Building
{
    public override BuildingType Type => BuildingType.Battery;


    [Header("Storage – kapacita")]
    [Tooltip("O kolik se zvýší maximální kapacita coria hráče, když je sklad dostavěný.")]
    [SerializeField] private int CapacityBonus = 100;
    [SerializeField] private Boom boom;

    private bool bonusApplied = false;

    public override void OnBuilt()
    {
        ApplyBonus();
    }

    /// <summary>
    /// Serverová logika – sklad nemá aktivní tick, jen pasivní bonus.
    /// </summary>
    public override void UpdateBuilding()
    {
        // Sklad nemá žádnou aktivní logiku – bonus se aplikuje jednorázově v OnBuilt().
    }

    /// <summary>
    /// Při zničení skladu se bonus odebere – hráč ztratí kapacitu.
    /// </summary>
    protected override void DestroyBuilding()
    {
        boom.Explode(gameObject);
        RemoveBonus();
        base.DestroyBuilding();
    }

    private void ApplyBonus()
    {
        if (!IsServer || OwnerResources == null || bonusApplied) return;

        OwnerResources.AddMaxEnergy(CapacityBonus);
        bonusApplied = true;

        Debug.Log($"[Storage] {name} přidal +{CapacityBonus} max energie hráči {OwnerClientId}. " +
                  $"Nový max: {OwnerResources.MaxEnergy.Value}");
    }

    private void RemoveBonus()
    {
        if (!IsServer || OwnerResources == null || !bonusApplied) return;

        OwnerResources.RemoveMaxEnergy(CapacityBonus);
        bonusApplied = false;

        Debug.Log($"[Storage] {name} odebral -{CapacityBonus} max energie hráči {OwnerClientId}. " +
                  $"Nový max: {OwnerResources.MaxEnergy.Value}");
    }
}
