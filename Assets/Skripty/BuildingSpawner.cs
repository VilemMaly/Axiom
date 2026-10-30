using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BuildingSpawner : NetworkBehaviour
{
    public int coriumPerSecond = 1;
    public StatusBar troopLoading;

    [Header("Efekty při dokončení jednotky")]
    [SerializeField] private AudioSource completionAudioSource;
    [SerializeField] private AudioClip completionSound;
    [SerializeField] private Animator completionAnimator;

    private static readonly int IsFinishedTrigger = Animator.StringToHash("isFinished");

    [Header("Povolené typy jednotek")]
    [SerializeField] private List<TroopType> allowedTroopTypes = new List<TroopType>();

    /// <summary>Raised once for each unit completed by this building.</summary>
    public event Action<int, Building> loadingFinished;

    private readonly Queue<int> troopQueue = new Queue<int>();
    private readonly Dictionary<int, Troop> troopDefinitions = new Dictionary<int, Troop>();
    [SerializeField] public Transform spawnPoint;
    [SerializeField] public Transform rallyPoint;
    private float time;
    private int troopCost;
    private int troopCostMax;
    private PlayerResources resources;
    private Building currentBuilding;

    public bool HasQueuedTroops => troopQueue.Count > 0;

    public bool CanSpawn(TroopType troopType) => allowedTroopTypes.Contains(troopType);

    public int CountQueuedTroopsOfType(TroopType troopType)
    {
        int count = 0;
        foreach (int troopIndex in troopQueue)
        {
            if (troopDefinitions.TryGetValue(troopIndex, out Troop troop) &&
                troop != null && troop.Type == troopType)
            {
                count++;
            }
        }

        return count;
    }

    public override void OnNetworkSpawn()
    {
        currentBuilding = GetComponent<Building>();

        if (IsServer && NetworkManager.Singleton.ConnectedClients.TryGetValue(OwnerClientId, out var ownerClient) &&
            ownerClient.PlayerObject != null)
        {
            resources = ownerClient.PlayerObject.GetComponent<PlayerResources>();
        }
    }

    public override void OnNetworkDespawn()
    {
        loadingFinished = null;
        troopQueue.Clear();
        troopDefinitions.Clear();
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer || troopQueue.Count == 0 || currentBuilding == null || !currentBuilding.IsOperational)
            return;

        if (troopCost <= 0)
        {
            CompleteCurrentTroop();
            return;
        }

        time += Time.deltaTime;
        if (time < 1f)
            return;

        time -= 1f;
        int amountToSpend = Mathf.Min(coriumPerSecond, troopCost);
        if (resources == null || !resources.TrySpend(amountToSpend, 0))
            return;

        troopCost -= amountToSpend;
        UpdateLoadingStatus();

        if (troopCost <= 0)
            CompleteCurrentTroop();
    }

    /// <summary>Adds an indexed troop request to this building's production queue.</summary>
    public bool EnqueueTroop(int troopIndex, Troop troop)
    {
        if (!IsServer || troopIndex < 0 || troop == null || !CanSpawn(troop.Type) || coriumPerSecond <= 0 || troop.Cost < 0)
            return false;

        troopDefinitions[troopIndex] = troop;
        troopQueue.Enqueue(troopIndex);

        if (troopQueue.Count == 1)
            BeginCurrentTroop();

        return true;
    }

    private void BeginCurrentTroop()
    {
        if (troopQueue.Count == 0)
            return;

        int troopIndex = troopQueue.Peek();
        if (!troopDefinitions.TryGetValue(troopIndex, out Troop troop) || troop == null)
        {
            Debug.LogError($"[BuildingSpawner] Chybí definice jednotky pro index {troopIndex}.", this);
            troopQueue.Dequeue();
            if (troopQueue.Count > 0)
                BeginCurrentTroop();
            else
                ClearQueue();
            return;
        }

        troopCost = troop.Cost;
        troopCostMax = troop.Cost;
        time = 0f;
        UpdateLoadingStatus();
    }

    private void CompleteCurrentTroop()
    {
        if (troopQueue.Count == 0)
            return;

        int completedTroopIndex = troopQueue.Dequeue();
        bool hasNextTroop = troopQueue.Count > 0;

        if (hasNextTroop)
            BeginCurrentTroop();
        else
        {
            troopCost = 0;
            troopCostMax = 0;
            time = 0f;
        }

        loadingFinished?.Invoke(completedTroopIndex, currentBuilding);
        PlayCompletionEffectsClientRpc();

        if (currentBuilding.Type == Building.BuildingType.Core && currentBuilding is Core core)
            core.OpenAndCloseClientRpc();

        if (!hasNextTroop)
            ClearQueue();
    }

    [ClientRpc]
    private void PlayCompletionEffectsClientRpc()
    {
        if (completionAudioSource != null && completionSound != null)
            completionAudioSource.PlayOneShot(completionSound);

        if (completionAnimator != null)
            completionAnimator.SetTrigger(IsFinishedTrigger);
    }

    private void ClearQueue()
    {
        troopQueue.Clear();
        troopDefinitions.Clear();
        troopCost = 0;
        troopCostMax = 0;
        time = 0f;
        loadingFinished = null;
        UpdateLoadingStatus();
    }

    private void UpdateLoadingStatus()
    {
        if (troopLoading != null)
            troopLoading.SetValue(troopCost, troopCostMax);

        updateStatusClientRpc(troopCost, troopCostMax);
    }

    [ClientRpc]
    private void updateStatusClientRpc(int cost, int costmax)
    {
        if (troopLoading != null)
            troopLoading.SetValue(cost, costmax);
    }
}
