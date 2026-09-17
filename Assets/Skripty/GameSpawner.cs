using Unity.Netcode;
using UnityEngine;

public class GameSpawner : NetworkBehaviour
{
    public GameObject playerPrefab;

    public Transform spawn1;
    public Transform spawn2;
    public GameObject spawn1Visual;
    public GameObject spawn2Visual;

    [Header("Minimap")]
    [Tooltip("The real minimap camera. Not exposed to the player - only referenced here.")]
    [SerializeField] Camera minimapCamera;
    [SerializeField] LayerMask placementLayerMask;

    public override void OnNetworkSpawn()
    {
        // Every client (and the server) subscribes locally so a click on THIS
        // client's minimap UI gets forwarded through that client's own
        // GameSpawner instance via ServerRpc. The camera always sits under
        // the player object and follows it, so moving the object IS moving
        // the view - no separate camera-jump logic needed.
        Debug.Log($"[GameSpawner] Subscribing to minimap click event on {name}.");
        MinimapClickUI.OnMinimapClicked += HandleMinimapClicked;

        if(NetworkManager.Singleton.LocalClientId == 0)
        {
            spawn1Visual.SetActive(true);
            spawn2Visual.SetActive(false);
        }
        else
        {
            spawn1Visual.SetActive(false);
            spawn2Visual.SetActive(true);
        }
        
        if (!IsServer)
            return;

        SpawnPlayers();
    }

    public override void OnNetworkDespawn()
    {
        MinimapClickUI.OnMinimapClicked -= HandleMinimapClicked;
    }

    void HandleMinimapClicked(Vector2 normalizedClick)
    {
        Debug.Log($"[GameSpawner] Received minimap click at {normalizedClick} on {name}. Forwarding to server.");
        RequestMinimapMoveServerRpc(normalizedClick);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestMinimapMoveServerRpc(Vector2 normalizedClick, ServerRpcParams rpcParams = default)
    {
        Debug.Log($"[GameSpawner] Server received minimap click at {normalizedClick} from client {rpcParams.Receive.SenderClientId} on {name}. Moving player object.");
        if (minimapCamera == null)
            return;
        minimapCamera.enabled = true; // Enable the minimap camera to render

        // ViewportPointToRay expects 0..1 across the camera's own viewport,
        // which lines up with the normalized UV from the RawImage as long as
        // the RenderTexture is rendered from this same camera without cropping.
        Ray ray = minimapCamera.ViewportPointToRay(new Vector3(normalizedClick.x, normalizedClick.y, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit, minimapCamera.farClipPlane, placementLayerMask))
            return;

        // Trust the RPC sender ID, never a client-supplied player ID - this
        // guarantees a client can only ever move their own player object.
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out var client))
            return;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null)
            return; // this client's player hasn't spawned yet - nothing to move

        playerObject.transform.position = hit.point;

        // No NetworkTransform on this prefab, so replicate this one-off move
        // manually. Broadcast to everyone (default ClientRpcParams) - not just
        // the sender - so the opponent also sees the object move.
        MovePlayerClientRpc(playerObject.NetworkObjectId, hit.point);
    }

    [ClientRpc]
    void MovePlayerClientRpc(ulong movedPlayerNetworkObjectId, Vector3 newPosition)
    {
        // The server already set its own copy above - only clients need this.
        if (IsServer)
            return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(movedPlayerNetworkObjectId, out var networkObject))
            return;

        networkObject.transform.position = newPosition;
    }

    void SpawnPlayers()
    {
        var p1 = Instantiate(playerPrefab, spawn1.position, Quaternion.identity);
        p1.GetComponent<NetworkObject>().SpawnAsPlayerObject(0);


        var p2 = Instantiate(playerPrefab, spawn2.position, Quaternion.identity);
        p2.GetComponent<NetworkObject>().SpawnAsPlayerObject(1);
    }
}