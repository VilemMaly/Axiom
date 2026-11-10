using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Server-owned record of resources known to one player's forces.</summary>
public class ResourceManager : NetworkBehaviour
{
    [Serializable]
    public class KnownResource
    {
        public Resource Resource;
        public Vector3 LastKnownPosition;
        public int LastKnownAmount;
        public float LastUpdatedTime;
    }

    [SerializeField] private List<KnownResource> knownResources = new();
    public IReadOnlyList<KnownResource> KnownResources => knownResources;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            RefreshRegistration();
    }

    /// <summary>Rebuild this player's resource index from resources currently in the scene.</summary>
    public void RefreshRegistration()
    {
        if (!IsServer) return;

        var resources = FindObjectsByType<Corium>(FindObjectsSortMode.None);
        var present = new HashSet<Resource>();
        foreach (Corium corium in resources)
        {
            if (corium == null || !corium.IsSpawned) continue;
            present.Add(corium);
            Observe(corium);
        }

        for (int i = knownResources.Count - 1; i >= 0; i--)
        {
            KnownResource record = knownResources[i];
            Resource resource = record != null ? record.Resource : null;
            if (resource == null || !resource.IsSpawned || resource is Corium && !present.Contains(resource))
                knownResources.RemoveAt(i);
        }
    }

    /// <summary>Checks only visible Coria and performs a world refresh if the index is stale.</summary>
    public void ValidateVisibleResources(IReadOnlyList<Corium> visibleResources)
    {
        if (!IsServer || visibleResources == null) return;
        bool stale = false;
        foreach (Corium corium in visibleResources)
        {
            if (corium == null || !corium.IsSpawned) continue;
            KnownResource record = knownResources.Find(x => x != null && x.Resource == corium);
            if (record == null || record.LastKnownAmount != corium.CoriumAmount.Value ||
                record.LastKnownPosition != corium.transform.position)
            {
                stale = true;
                break;
            }
        }

        if (stale)
            RefreshRegistration();
    }

    public void Observe(Resource resource)
    {
        if (!IsServer || resource == null || !resource.IsSpawned) return;
        KnownResource record = knownResources.Find(x => x != null && x.Resource == resource);
        if (record == null) { record = new KnownResource { Resource = resource }; knownResources.Add(record); }
        Vector3 position = resource.transform.position;
        int amount = resource.CoriumAmount.Value;
        if (record.LastKnownPosition == position && record.LastKnownAmount == amount) return;
        record.LastKnownPosition = position;
        record.LastKnownAmount = amount;
        record.LastUpdatedTime = Time.time;
    }

    public Resource FindNearestKnown(Vector3 from)
    {
        if (!IsServer) return null;
        Resource best = null; float bestDistance = float.MaxValue;
        for (int i = knownResources.Count - 1; i >= 0; i--)
        {
            KnownResource r = knownResources[i];
            if (r == null || r.Resource == null || !r.Resource.IsSpawned) { knownResources.RemoveAt(i); continue; }
            if (r.LastKnownAmount <= 0 || r.Resource.Depleted || r.Resource.CoriumAmount.Value <= 0) continue;
            float d = (r.LastKnownPosition - from).sqrMagnitude;
            if (d < bestDistance) { best = r.Resource; bestDistance = d; }
        }
        return best;
    }
}
