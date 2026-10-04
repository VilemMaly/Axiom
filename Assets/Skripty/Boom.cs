using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Obecný výbuch – koulový sken okolí, najde Troop a Building, uloží je do listů
/// a rozdá damage podle vzdálenosti od středu (uprostřed nejvíc).
/// Použití: vytvoř objekt (nebo [SerializeField] pole) a zavolej Explode(gameObject).
/// Běží POUZE na serveru (na klientech se nic nestane).
/// </summary>
[Serializable]
public class Boom : MonoBehaviour
{
    [Header("Poloměr výbuchu.")]
    [Tooltip("Poloměr výbuchu.")]
    public float radius = 6f;
    public LayerMask troopLayerMask;
    public LayerMask buildingLayerMask;

    [Header("Damage")]
    [Tooltip("Damage ve středu výbuchu.")]
    public int maxDamage = 100;
    [Tooltip("Damage na samém okraji radiusu.")]
    public int minDamage = 0;
    [Tooltip("Průběh útlumu: X = vzdálenost (0 střed, 1 okraj), Y = 1 plný damage, 0 minDamage.")]
    public AnimationCurve falloff = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
    [Tooltip("Násobič damage proti budovám (např. obléhací výbuch).")]
    public float buildingDamageMultiplier = 1f;
    [Tooltip("Násobič damage proti jednotkám.")]
    public float troopDamageMultiplier = 1f;

    // Výsledky posledního výbuchu
    public readonly List<Troop> TroopsHit = new();
    public readonly List<Building> BuildingsHit = new();

    private readonly Collider[] scanResults = new Collider[128];

    public Boom() { }

    public Boom(float radius, int maxDamage, LayerMask troopMask, LayerMask buildingMask)
    {
        this.radius = radius;
        this.maxDamage = maxDamage;
        troopLayerMask = troopMask;
        buildingLayerMask = buildingMask;
    }

    // ---------- Veřejné overloady ----------

    /// <summary>Výbuch na pozici GameObjectu, ublíží všem.</summary>
    public void Explode(GameObject source)
    {
        if (source == null) return;
        ExplodeInternal(source.transform.position, null);
    }

    /// <summary>Výbuch na pozici GameObjectu, ignoruje jednotky a budovy hráče ignoreClientId.</summary>
    public void Explode(GameObject source, ulong ignoreClientId)
    {
        if (source == null) return;
        ExplodeInternal(source.transform.position, ignoreClientId);
    }

    /// <summary>Výbuch na dané pozici, ublíží všem.</summary>
    public void Explode(Vector3 center)
    {
        ExplodeInternal(center, null);
    }

    /// <summary>Výbuch na dané pozici, ignoruje jednotky a budovy hráče ignoreClientId.</summary>
    public void Explode(Vector3 center, ulong ignoreClientId)
    {
        ExplodeInternal(center, ignoreClientId);
    }

    // ---------- Společná logika ----------

    private void ExplodeInternal(Vector3 center, ulong? ignoreClientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        Scan(center, ignoreClientId);
        ApplyDamage(center);
    }

    private void Scan(Vector3 center, ulong? ignoreClientId)
    {
        TroopsHit.Clear();
        BuildingsHit.Clear();

        // Troopy
        int count = Physics.OverlapSphereNonAlloc(center, radius, scanResults, troopLayerMask);
        for (int i = 0; i < count; i++)
        {
            if (scanResults[i] == null) continue;

            Troop troop = scanResults[i].GetComponentInParent<Troop>();
            if (troop == null || TroopsHit.Contains(troop)) continue;
            if (ignoreClientId.HasValue && troop.OwnerClientId == ignoreClientId.Value) continue;

            TroopsHit.Add(troop);
        }

        // Budovy
        count = Physics.OverlapSphereNonAlloc(center, radius, scanResults, buildingLayerMask);
        for (int i = 0; i < count; i++)
        {
            if (scanResults[i] == null) continue;

            Building building = scanResults[i].GetComponentInParent<Building>();
            if (building == null || BuildingsHit.Contains(building)) continue;
            if (ignoreClientId.HasValue && building.OwnerClientId == ignoreClientId.Value) continue;

            BuildingsHit.Add(building);
        }
    }

    private void ApplyDamage(Vector3 center)
    {
        // Iterace odzadu – TakeDamage může objekt despawnout
        for (int i = TroopsHit.Count - 1; i >= 0; i--)
        {
            Troop troop = TroopsHit[i];
            if (troop == null) continue;

            int damage = CalculateDamage(center, troop.gameObject, troopDamageMultiplier);
            if (damage > 0)
                troop.TakeDamage(damage);
        }

        for (int i = BuildingsHit.Count - 1; i >= 0; i--)
        {
            Building building = BuildingsHit[i];
            if (building == null) continue;

            int damage = CalculateDamage(center, building.gameObject, buildingDamageMultiplier);
            if (damage > 0)
                building.TakeDamage(damage);
        }
    }

    /// <summary>
    /// Damage podle vzdálenosti: t = 0 ve středu, 1 na okraji. Vzdálenost se měří
    /// k nejbližšímu bodu collideru (velké budovy se tak nepočítají jen podle pivotu).
    /// </summary>
    private int CalculateDamage(Vector3 center, GameObject target, float multiplier)
    {
        Vector3 closest = target.transform.position;
        Collider col = target.GetComponentInChildren<Collider>();
        if (col != null)
            closest = col.bounds.ClosestPoint(center);

        float distance = Vector3.Distance(center, closest);
        float t = Mathf.Clamp01(distance / radius);

        float factor = Mathf.Clamp01(falloff.Evaluate(t));
        float damage = Mathf.Lerp(minDamage, maxDamage, factor) * multiplier;

        return Mathf.RoundToInt(damage);
    }

#if UNITY_EDITOR
    /// <summary>Volitelně zavolej z OnDrawGizmosSelected() vlastníka.</summary>
    public void DrawGizmo(Vector3 center)
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, radius);
    }
#endif
}