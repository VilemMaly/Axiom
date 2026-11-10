using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ResourceManager : NetworkBehaviour
{
    private readonly List<Resource> resources = new();

    public IReadOnlyList<Resource> Resources => resources;

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
}
