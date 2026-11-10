using System.Collections.Generic;
using System.Linq;
using System;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class Cargo : Troop
{
    [System.Serializable]
    public struct PendingBuildRequest
    {
        public int BuildingIndex;
        public Vector3 Position;
        public int Rotation;

        public PendingBuildRequest(int buildingIndex, Vector3 position, int rotation)
        {
            BuildingIndex = buildingIndex;
            Position = position;
            Rotation = rotation;
        }
    }

    private PlayerResources playerResources;
    public event System.Action<Cargo, int, Vector3, int, int> NearBuilding;
    private Building currentBuildTarget;
    private float nextBuildSendTime;
    [SerializeField] private bool GoingToBuilding;
    [SerializeField] private Vector3 BuildingPosition;
    private int BuildingIndex;
    private int BuildingRotation;
    [SerializeField] private List<PendingBuildRequest> pendingBuildQueue = new();
    [Header("Otáčení věže")]
    [Tooltip("Věž se otáčí pouze na klientovi, který vlastní toto Cargo.")]
    [SerializeField] private Transform turretTransform;
    [SerializeField, Min(0f)] private float turretTurnSpeed = 360f;
    public NetworkVariable<Troop.State> status = new(writePerm: NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> turretTargetPosition =
        new(writePerm: NetworkVariableWritePermission.Server);
    public bool HasPendingBuildOrders => GoingToBuilding || pendingBuildQueue.Count > 0;
    public NetworkVariable<bool> IsBuilding = new(writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsRepairing = new(writePerm: NetworkVariableWritePermission.Server);

    [Header("Detekce resources")]
    [SerializeField] private LayerMask ResourceLayerMask;
    [SerializeField] private float resourceDetectionInterval = 0.5f;

    private readonly List<Resource> resourcesInRange = new();
    private readonly Collider[] resourceResults = new Collider[32];
    private float nextResourceScan;

    public IReadOnlyList<Resource> ResourcesInRange => resourcesInRange;

    private Resource currentTarget;
    private float nextHarvestTick;

    [Header("Efekty těžby a předání coria")]
    [Tooltip("ParticleSystem připojený v editoru; při spuštění se odpojí od Cargo.")]
    [SerializeField] private ParticleSystem transferParticle;
    [Tooltip("Pozice a směr, odkud efekt vyletí.")]
    [SerializeField] private Transform transferOrigin;
    [SerializeField] private AudioClip transferSound;
    [Tooltip("AudioSource pro prostorové přehrání zvuku.")]
    [SerializeField] private AudioSource transferAudioSource;
    private Transform transferParticleOriginalParent;

    public NetworkVariable<bool> IsHarvesting = new(writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsDepositing = new(writePerm: NetworkVariableWritePermission.Server);
    private float nextDepositTick;

    /// <summary>Kolik coria už tahle jednotka nasbírala a veze v sobě.</summary>
    public NetworkVariable<int> CoriumCargo = new(writePerm: NetworkVariableWritePermission.Server);

    public override void OnSpawned()
    {
        if (transferParticle != null)
        {
            transferParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            transferParticleOriginalParent = transferParticle.transform.parent;
            if (transferParticle.transform != transform && transferParticle.transform.IsChildOf(transform))
                transferParticle.transform.SetParent(null, true);
        }
        if (IsServer)
            playerResources = NetworkManager.Singleton.ConnectedClients[OwnerClientId]
                .PlayerObject.GetComponent<PlayerResources>();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (transferParticle == null)
            return;

        transferParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (transferParticleOriginalParent != null)
            transferParticle.transform.SetParent(transferParticleOriginalParent, true);
    }

    public bool IsCargoFull => CoriumCargo.Value >= MaxCargo;
    public bool HasCargo => CoriumCargo.Value > 0;


    public override void UpdateTroop()
    {
        UpdateTurretAim();
    }

    private void LateUpdate()
    {
    }

    private void UpdateTurretAim()
    {
        if (!IsOwner || turretTransform == null || status.Value == Troop.State.moving)
            return;

        Vector3 direction = turretTargetPosition.Value - turretTransform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        turretTransform.rotation = Quaternion.RotateTowards(
            turretTransform.rotation,
            desiredRotation,
            turretTurnSpeed * Time.deltaTime);
    }

    public void Harvest(IHarvestable harvestable)
    {
        if (!IsServer || harvestable == null) return;
        turretTargetPosition.Value = harvestable.HarvestTransform.position;
        status.Value = Troop.State.harvesting;
        if (Time.time < nextHarvestTick) return;
        nextHarvestTick = Time.time + 1f;
        harvestable.DepleteResource(this, HarvestCoriumPerSecond);
    }

    public void Deposit(Building building)
    {
        if (!IsServer || building == null) return;
        turretTargetPosition.Value = building.transform.position;
        status.Value = Troop.State.depositing;
        if (building.Type != Building.BuildingType.Core)
        {
            if (Time.time >= nextDepositTick)
            {
                nextDepositTick = Time.time + 1f;
                if (playerResources != null && playerResources.TrySpend(DepositCoriumPerSecond, 0))
                {
                    building.ReceiveCorium(DepositCoriumPerSecond);
                    CoriumCargo.Value -= DepositCoriumPerSecond;
                    PlayTransferEffect(building.transform.position);
                }
            }
        }
        else if (building.Type == Building.BuildingType.Core)
        {
            if (Time.time >= nextDepositTick)
            {
                nextDepositTick = Time.time + 1f;

                int amount = Mathf.Min(DepositCoriumPerSecond, CoriumCargo.Value);
                Core currentCore = building as Core;

                if (amount > 0)
                {
                    currentCore.CoriumReserve(amount, this);
                    CoriumCargo.Value -= amount;
                    PlayTransferEffect(currentCore.transform.position);
                }
            }
        }
    }

    public override void MoveTo(Vector3 destination)
    {
        if (IsServer)
            status.Value = Troop.State.moving;
        base.MoveTo(destination);
    }

    /// <summary>
    /// Server sem připíše vytěžené corium. Volá se výhradně z Resource
    /// (konkrétně z Corium.DepleteResource) poté, co si ověří vlastnictví.
    /// </summary>
    public void AddToCargo(int amount)
    {
        if (!IsServer || amount <= 0)
            return;

        int added = Mathf.Min(amount, MaxCargo - CoriumCargo.Value);
        if (added <= 0)
            return;
        CoriumCargo.Value += added;
        if (currentTarget != null)
            PlayTransferEffect(currentTarget.transform.position);
    }

    private void PlayTransferEffect(Vector3 destination)
    {
        if (!IsServer)
            return;
        Vector3 origin = transferOrigin != null ? transferOrigin.position : transform.position;
        PlayTransferEffectClientRpc(origin, destination);
    }

    [ClientRpc]
    private void PlayTransferEffectClientRpc(Vector3 origin, Vector3 destination)
    {
        if (transferParticle != null)
        {
            transferParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Vector3 direction = destination - origin;
            Quaternion rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : transferParticle.transform.rotation;
            transferParticle.transform.SetPositionAndRotation(origin, rotation);
            transferParticle.Play();
        }

        if (transferAudioSource != null && transferSound != null)
            transferAudioSource.PlayOneShot(transferSound);
    }

    [ServerRpc]
    private void RequestHarvestTargetServerRpc(NetworkObjectReference resourceReference, ServerRpcParams rpcParams = default)
    {
        // bezpečnost - pouze vlastník jednotky může dávat příkazy
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Cargo-DIAG] Těžba odmítnuta: RPC poslal cizí klient {rpcParams.Receive.SenderClientId}, vlastník Cargo={OwnerClientId}.", this);
            return;
        }

        if (!resourceReference.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning("[Cargo-DIAG] Těžba odmítnuta: reference na resource je neplatná.", this);
            return;
        }

        if (!netObj.TryGetComponent(out Resource resource))
        {
            Debug.LogWarning($"[Cargo-DIAG] Těžba odmítnuta: objekt {netObj.name} nemá komponentu Resource.", this);
            return;
        }

        // serverová kontrola: resource musí být skutečně v dosahu Cargou
        if (!resourcesInRange.Contains(resource))
        {
            Debug.LogWarning($"[Cargo-DIAG] Těžba odmítnuta: resource {resource.name} není v evidovaném dosahu Cargo.", this);
            return;
        }

        if (!IsNearEnough(resource.GameObject().transform.position, this))
        {
            Debug.LogWarning($"[Cargo-DIAG] Těžba odmítnuta: resource {resource.name} je příliš daleko.", this);
            return;
        }

        if (resource.Depleted)
        {
            Debug.LogWarning($"[Cargo-DIAG] Těžba odmítnuta: resource {resource.name} je vytěžený.", this);
            return;
        }

        currentTarget = resource;
        turretTargetPosition.Value = resource.transform.position;
        status.Value = Troop.State.harvesting;
        IsHarvesting.Value = true;
        nextHarvestTick = Time.time;
    }
}
