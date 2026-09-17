using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class Factory : Building
{
    public override BuildingType Type => BuildingType.Factory;

    [Header("Factory specific")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform rallyPoint;
    [SerializeField] private List<TroopData> availableTroops;

    private Queue<int> productionQueue = new Queue<int>();
    private float productionTimer;
    private bool isProducing;

    public override void UpdateBuilding()
    {
        if (!IsOperational || !IsServer || !isProducing) return;

        productionTimer -= Time.deltaTime;
        if (productionTimer <= 0f)
            SpawnNextTroop();
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestTrainTroopServerRpc(int troopId, ServerRpcParams rpcParams = default)
    {
        var data = availableTroops.Find(t => t.id == troopId);
        if (data == null) return;

        if (!OwnerResources.TrySpend(data.coriumCost, data.energyCost))
            return; // server odmítne, fronta se nenaplní

        productionQueue.Enqueue(troopId);

        if (!isProducing)
            StartNextProduction();
    }

    private void StartNextProduction()
    {
        if (productionQueue.Count == 0)
        {
            isProducing = false;
            return;
        }

        isProducing = true;
        var data = availableTroops.Find(t => t.id == productionQueue.Peek());
        productionTimer = data.trainTime;
    }

    private void SpawnNextTroop()
    {
        int troopId = productionQueue.Dequeue();
        var data = availableTroops.Find(t => t.id == troopId);

        var instance = Instantiate(data.prefab, spawnPoint.position, spawnPoint.rotation);
        instance.GetComponent<NetworkObject>().SpawnWithOwnership(OwnerClientId);

        // TODO: po spawnu poslat jednotku na rallyPoint
        // instance.GetComponent<Troop>().MoveTo(rallyPoint.position);

        StartNextProduction();
    }
}

[CreateAssetMenu(fileName = "NewTroop", menuName = "RTS/Troop Data")]
public class TroopData : ScriptableObject
{
    public int id;
    public string troopName;
    public GameObject prefab;
    public int coriumCost;
    public int energyCost;
    public float trainTime;
}