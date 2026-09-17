using UnityEngine;

public class Radar : Building
{
    public override BuildingType Type => BuildingType.Radar;

    [Header("Radar specific")]
    [SerializeField] private float revealRadius = 25f;

    public override void OnBuilt()
    {
        // TODO: odkrýt mlhu války v revealRadius kolem transform.position
        // Pokud máš FogOfWar systém, zavolej ho tady, typicky jen pro ownera:
        // FogOfWarManager.Instance.RevealArea(transform.position, revealRadius, OwnerClientId);
    }

    // TODO: volitelně periodická kontrola OverlapSphere pro odhalení
    // neviditelných/stealth jednotek nepřítele v dosahu
}