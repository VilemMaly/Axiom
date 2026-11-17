using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ResourceManager : NetworkBehaviour
{
    private readonly HashSet<Resource> resources = new();

    public HashSet<Resource> Resources => resources;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        resources.Clear();

        foreach (Resource resource in FindObjectsByType<Resource>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            resources.Add(resource);
        }
    }

    public bool RegisterScrap(Scrap scrap)
    {
        Debug.Log("[ResourceManager] Registrace scrap");
        scrap.resourceManager = this;
        return resources.Add(scrap);
    }

    public void UnRegisterScrap(Scrap scrap)
    {
        Debug.Log("[ResourceManager] Deregistrace scrap");
        resources.Remove(scrap);
    }
}
