using UnityEngine;

public class Storage : Building
{
    public override BuildingType Type => BuildingType.Storage;

    [Header("Storage – kapacita")]
    [Tooltip("O kolik se zvýší maximální kapacita coria hráče, když je sklad dostavěný.")]
    [SerializeField] private int coriumCapacityBonus = 100;

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
        RemoveBonus();
        base.DestroyBuilding();
    }

    private void ApplyBonus()
    {
        if (!IsServer || OwnerResources == null || bonusApplied) return;

        OwnerResources.AddMaxCorium(coriumCapacityBonus);
        bonusApplied = true;

        Debug.Log($"[Storage] {name} přidal +{coriumCapacityBonus} max coria hráči {OwnerClientId}. " +
                  $"Nový max: {OwnerResources.MaxCorium.Value}");
    }

    private void RemoveBonus()
    {
        if (!IsServer || OwnerResources == null || !bonusApplied) return;

        OwnerResources.RemoveMaxCorium(coriumCapacityBonus);
        bonusApplied = false;

        Debug.Log($"[Storage] {name} odebral -{coriumCapacityBonus} max coria hráči {OwnerClientId}. " +
                  $"Nový max: {OwnerResources.MaxCorium.Value}");
    }
}
