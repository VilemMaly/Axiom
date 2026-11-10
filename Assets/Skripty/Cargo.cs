using System.Collections.Generic;
using System.Linq;
using System;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class Cargo : Troop
{
    public enum WorkStatus { Idle, Moving, Harvesting, Depositing, Building }
    public NetworkVariable<WorkStatus> Status = new(writePerm: NetworkVariableWritePermission.Server);
    public Resource AssignedResource { get; private set; }
    public Building AssignedBuilding { get; private set; }
    public Vector3 WorkDestination { get; private set; }
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
    public void RefreshResourceKnowledge(ResourceManager manager)
    {
        if (!IsServer || manager == null) return;
        ScanForResources();
        foreach (Resource resource in resourcesInRange) manager.Observe(resource);
    }

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
    private Core currentCore;
    private Building currentDepositBuilding;
    private float nextDepositTick;

    public Building currentBase;
    private Resource selectedResource;
    private Resource aiMoveResource;
    private Core aiMoveCore;

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

    public override void BuildBuilding(Building building)
    {
        if (!IsServer || building == null) return;
        currentBuildTarget = building;
        nextBuildSendTime = Time.time + 1f;
        IsBuilding.Value = true;
        IsRepairing.Value = false;
    }

    public override void RepairBuilding(Building building)
    {
        if (!IsServer || building == null) return;
        currentBuildTarget = building;
        IsRepairing.Value = true;
        IsBuilding.Value = false;
    }

    public override void StopCurrentTask()
    {
        base.StopCurrentTask();
        if (IsServer) CancelWork();
        else if (IsOwner) CancelHarvestingForBuildServerRpc();
    }

    public override void OnSelectResource(Resource resource)
    {
        base.OnSelectResource(resource);
        selectedResource = resource;
        Debug.Log($"[Cargo-DIAG] {name} OnSelectResource -> selectedResource={(resource != null ? resource.name : "null")} | IsOwner={IsOwner} IsServer={IsServer}");
    }
    public override void OnSelectBuilding(Building building)
    {
        base.OnSelectBuilding(building);
        if (building.Type == Building.BuildingType.Core)
        {
            currentBase = building;
        }
        else
        {
            Debug.Log("Není vybraný core building");
        }
        
    }

    public bool IsCargoFull => CoriumCargo.Value >= MaxCargo;
    public bool HasCargo => CoriumCargo.Value > 0;

    /// <summary>AI command: move to a resource and begin harvesting when in work range.</summary>
    public void OrderHarvest(Resource resource)
    {
        if (!IsServer || resource == null || !resource.IsSpawned || resource.Depleted || IsCargoFull || IsHarvesting.Value) return;
        selectedResource = resource;
        AssignedResource = resource; AssignedBuilding = null; WorkDestination = resource.transform.position;
        if (IsNearEnough(resource.transform.position, transform.position))
        {
            aiMoveResource = null;
            BeginHarvestServer(resource);
            return;
        }
        if (aiMoveResource != resource)
        {
            aiMoveResource = resource;
            aiMoveCore = null;
            RequestMove(resource.transform.position);
            Status.Value = WorkStatus.Moving;
        }
    }

    /// <summary>AI command: carry corium to a friendly Core and deposit it.</summary>
    public void OrderDeposit(Core core)
    {
        if (!IsServer || core == null || !core.IsSpawned || !HasCargo || IsDepositing.Value) return;
        currentBase = core;
        AssignedBuilding = core; AssignedResource = null; WorkDestination = core.transform.position;
        if (IsNearEnough(core.transform.position, transform.position))
        {
            aiMoveCore = null;
            if (core.OwnerClientId == OwnerClientId && core.IsOperational)
            { currentCore = core; currentDepositBuilding = core; IsDepositing.Value = true; Status.Value = WorkStatus.Depositing; nextDepositTick = Time.time; }
            return;
        }
        if (aiMoveCore != core)
        {
            aiMoveCore = core;
            aiMoveResource = null;
            RequestMove(core.transform.position);
            Status.Value = WorkStatus.Moving;
        }
    }

    public void OrderDepositStorage(Building building)
    {
        if (!IsServer || building == null || !building.IsSpawned || building.OwnerClientId != OwnerClientId ||
            building.Type != Building.BuildingType.Storage || !building.IsOperational || !HasCargo) return;
        if (Status.Value == WorkStatus.Moving && AssignedBuilding == building &&
            Vector3.Distance(transform.position, building.transform.position) > SightRange) return;
        AssignedBuilding = building; AssignedResource = null; WorkDestination = building.transform.position;
        if (Vector3.Distance(transform.position, building.transform.position) <= SightRange)
        { currentDepositBuilding = building; currentCore = null; IsDepositing.Value = true; Status.Value = WorkStatus.Depositing; nextDepositTick = Time.time; }
        else { RequestMove(building.transform.position); Status.Value = WorkStatus.Moving; }
    }

    public void OrderBuild(Building building)
    {
        if (!IsServer || building == null || !building.IsSpawned || building.IsConstructionComplete.Value || !HasCargo) return;
        StopHarvesting(); StopDepositing();
        currentBuildTarget = building; AssignedBuilding = building; AssignedResource = null;
        WorkDestination = building.transform.position;
        if (Vector3.Distance(transform.position, building.transform.position) > SightRange)
        {
            RequestMove(building.transform.position); Status.Value = WorkStatus.Moving;
        }
        else { IsBuilding.Value = true; IsRepairing.Value = false; nextBuildSendTime = Time.time; Status.Value = WorkStatus.Building; }
    }

    public void RequestMoveToBuild(Vector3 position, int buildingIndex, int rotation)
    {
        CancelHarvestingForBuildServerRpc();
        if (GoingToBuilding || IsBuilding.Value)
        {
            pendingBuildQueue.Add(new PendingBuildRequest(buildingIndex, position, rotation));
            return;
        }
        GoingToBuilding = true;
        RequestMove(position);
        BuildingPosition = position;
        BuildingIndex = buildingIndex;
        BuildingRotation = rotation;
        if (BuildingPosition == Vector3.zero)
        {
            Debug.LogWarning("[Cargo-DIAG] Příkaz ke stavbě zrušen: cílová pozice je Vector3.zero, která se používá jako sentinel pro neplatnou pozici.", this);
            GoingToBuilding = false;
        }
    }

    [ServerRpc]
    private void CancelHarvestingForBuildServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        CancelWork();
    }

    private void CancelWork()
    {
        StopHarvesting(); StopDepositing(); StopBuilding();
        AssignedResource = null; AssignedBuilding = null; aiMoveResource = null; aiMoveCore = null;
        Status.Value = WorkStatus.Idle;
    }

    public override void UpdateTroop()
    {
        if (!IsServer) return;
        if (AssignedResource != null && !IsHarvesting.Value && !IsCargoFull &&
            Vector3.Distance(transform.position, AssignedResource.transform.position) <= SightRange)
            BeginHarvestServer(AssignedResource);
        if (AssignedBuilding != null && !AssignedBuilding.IsConstructionComplete.Value && !IsBuilding.Value &&
            Vector3.Distance(transform.position, AssignedBuilding.transform.position) <= SightRange && HasCargo)
        { currentBuildTarget = AssignedBuilding; IsBuilding.Value = true; Status.Value = WorkStatus.Building; }
        UpdateConstruction();
        UpdateTurretAim();
        if (IsBuilding.Value || HasPendingBuildOrders) return;
        UpdateHarvesting();
    }

    private void UpdateTurretAim()
    {
        if (!IsOwner || turretTransform == null)
            return;

        Transform target = null;

        // Budova se vybírá stejně jako v klientské části UpdateConstruction.
        // Serverová currentBuildTarget není dostupná klientům, proto ji zde nehledáme.
        foreach (Building building in BuildingsInRange)
        {
            if (building == null || building.IsConstructionComplete.Value)
                continue;

            target = building.transform;
            break;
        }

        // Když není poblíž stavba, věž míří na zvolený zdroj coria.
        if (target == null && CoriumCargo.Value < MaxCargo && selectedResource != null &&
            !selectedResource.Depleted && selectedResource.CoriumAmount.Value > 0)
            target = selectedResource.transform;

        if (target == null)
            return;

        Vector3 direction = target.position - turretTransform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        turretTransform.rotation = Quaternion.RotateTowards(
            turretTransform.rotation,
            desiredRotation,
            turretTurnSpeed * Time.deltaTime);
    }

    private void UpdateConstruction()
    {
        if (!IsServer) return;

        if (IsOwner && !IsBuilding.Value && !GoingToBuilding && pendingBuildQueue.Count > 0)
        {
            PendingBuildRequest next = pendingBuildQueue[0];
            pendingBuildQueue.RemoveAt(0);
            RequestMoveToBuild(next.Position, next.BuildingIndex, next.Rotation);
        }

        if (IsBuilding.Value)
        {
            if (currentBuildTarget == null || currentBuildTarget.IsConstructionComplete.Value)
            {
                StopBuilding();
                return;
            }
            if (Time.time >= nextBuildSendTime)
            {
                nextBuildSendTime = Time.time + 1f;
                int amount = Mathf.Min(BuildCoriumPerSecond, CoriumCargo.Value, currentBuildTarget.MissingCorium);
                if (amount > 0)
                {
                    currentBuildTarget.ReceiveCorium(amount);
                    CoriumCargo.Value -= amount;
                    PlayTransferEffect(currentBuildTarget.transform.position);
                }
            }
        }
    }

    private void StopBuilding()
    {
        currentBuildTarget = null;
        IsBuilding.Value = false;
        IsRepairing.Value = false;
        if (Status.Value == WorkStatus.Building) Status.Value = WorkStatus.Idle;
    }

    [ServerRpc]
    private void RequestBuildServerRpc(NetworkObjectReference buildingReference, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != OwnerClientId)
        {
            Debug.LogWarning($"[Cargo-DIAG] Stavba odmítnuta: RPC poslal cizí klient {sender}, vlastník Cargo={OwnerClientId}.", this);
            return;
        }
        if (!buildingReference.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning("[Cargo-DIAG] Stavba odmítnuta: reference na budovu je neplatná.", this);
            return;
        }
        if (!netObj.TryGetComponent(out Building building))
        {
            Debug.LogWarning($"[Cargo-DIAG] Stavba odmítnuta: objekt {netObj.name} nemá komponentu Building.", this);
            return;
        }
        if (!BuildingsInRange.Contains(building))
        {
            Debug.LogWarning($"[Cargo-DIAG] Stavba odmítnuta: budova {building.name} není v dosahu Cargo.", this);
            return;
        }
        if (building.IsConstructionComplete.Value)
        {
            Debug.LogWarning($"[Cargo-DIAG] Stavba odmítnuta: budova {building.name} už je dokončená.", this);
            return;
        }
        BuildBuilding(building);
    }

    [ServerRpc]
    public void RequestRepairServerRpc(NetworkObjectReference buildingReference, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != OwnerClientId)
        {
            Debug.LogWarning($"[Cargo-DIAG] Oprava odmítnuta: RPC poslal cizí klient {sender}, vlastník Cargo={OwnerClientId}.", this);
            return;
        }
        if (!buildingReference.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning("[Cargo-DIAG] Oprava odmítnuta: reference na budovu je neplatná.", this);
            return;
        }
        if (!netObj.TryGetComponent(out Building building))
        {
            Debug.LogWarning($"[Cargo-DIAG] Oprava odmítnuta: objekt {netObj.name} nemá komponentu Building.", this);
            return;
        }
        if (!BuildingsInRange.Contains(building))
        {
            Debug.LogWarning($"[Cargo-DIAG] Oprava odmítnuta: budova {building.name} není v dosahu Cargo.", this);
            return;
        }
        RepairBuilding(building);
    }

    private void BeginBuild()
    {
        GoingToBuilding = false;
        NearBuilding?.Invoke(this, BuildingIndex, BuildingPosition, BuildingRotation, pendingBuildQueue.Count);
        BuildingPosition = Vector3.zero;
    }

    private void UpdateHarvesting()
    {
        // Detekce resources kolem sebe běží na všech instancích stejně jako
        // detekce budov v Troop.ScanBuildings - server tak má vlastní
        // autoritativní seznam, klient (owner) ho používá k výběru cíle.
        if (IsServer && CoriumCargo.Value < MaxCargo)
            ScanForResources();

        if (!IsServer)
            return;

        if (IsHarvesting.Value)
        {
            if (currentTarget == null || currentTarget.Depleted || CoriumCargo.Value >= MaxCargo)
            {
                StopHarvesting();
                return;
            }

            if (Time.time >= nextHarvestTick)
            {
                nextHarvestTick = Time.time + 1f;

                int amount = Mathf.Min(HarvestCoriumPerSecond, MaxCargo - CoriumCargo.Value);

                if (amount > 0)
                {
                    // Čistě serverové volání - žádné RPC, resource i harvester
                    // tu žijí ve stejném procesu (na serveru), takže stačí
                    // přímý C# call.
                    currentTarget.DepleteResource(this, amount);
                Status.Value = WorkStatus.Harvesting;
                }
            }
        }

        if (IsDepositing.Value)
        {
            if (currentDepositBuilding == null || !currentDepositBuilding.IsSpawned || CoriumCargo.Value <= 0)
            {
                StopDepositing();
                return;
            }

            if (Time.time >= nextDepositTick)
            {
                nextDepositTick = Time.time + 1f;

                int amount = Mathf.Min(DepositCoriumPerSecond, CoriumCargo.Value);

                if (amount > 0)
                {
                    // Stejně jako u těžby - čistě serverové volání, core i harvester
                    // žijí ve stejném procesu na serveru.
                    if (currentCore != null) currentCore.CoriumReserve(amount, this);
                    else if (NetworkManager.Singleton.ConnectedClients.TryGetValue(OwnerClientId, out var owner) && owner.PlayerObject != null)
                        owner.PlayerObject.GetComponent<PlayerResources>()?.Add(amount, 0);
                    CoriumCargo.Value -= amount;
                    Status.Value = CoriumCargo.Value <= 0 ? WorkStatus.Idle : WorkStatus.Depositing;
                    PlayTransferEffect(currentDepositBuilding.transform.position);
                }
            }
        }
    }


    private void ScanForResources()
    {
        if (Time.time < nextResourceScan)
            return;

        nextResourceScan = Time.time + resourceDetectionInterval;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            WorkRange,
            resourceResults,
            ResourceLayerMask);

        HashSet<Resource> currentResources = new();

        for (int i = 0; i < count; i++)
        {
            if (resourceResults[i] == null)
                continue;

            Resource resource = resourceResults[i].GetComponentInParent<Resource>();

            if (resource == null)
                continue;

            currentResources.Add(resource);

            if (!resourcesInRange.Contains(resource))
            {
                resourcesInRange.Add(resource);
                Debug.Log($"[Cargo] {name} detekuje resource {resource.name} v dosahu.");
            }
        }

        for (int i = resourcesInRange.Count - 1; i >= 0; i--)
        {
            Resource resource = resourcesInRange[i];

            if (resource == null || !currentResources.Contains(resource))
            {
                if (resource != null)
                    Debug.Log($"[Cargo] {name} ztrácí resource {resource.name} z dosahu.");

                resourcesInRange.RemoveAt(i);
            }
        }
    }

    [ServerRpc]
    private void RequestDepositCoriumServerRpc(NetworkObjectReference coreReference, ServerRpcParams rpcParams = default)
    {
        // bezpečnost - pouze vlastník jednotky může dávat příkazy
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Cargo-DIAG] Vyložení odmítnuto: RPC poslal cizí klient {rpcParams.Receive.SenderClientId}, vlastník Cargo={OwnerClientId}.", this);
            return;
        }

        if (!coreReference.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning("[Cargo-DIAG] Vyložení odmítnuto: reference na Core je neplatná.", this);
            return;
        }

        if (!netObj.TryGetComponent(out Core core))
        {
            Debug.LogWarning($"[Cargo-DIAG] Vyložení odmítnuto: objekt {netObj.name} není Core.", this);
            return;
        }

        // serverová kontrola: core musí být skutečně v dosahu Cargou
        // (budovy v dosahu sleduje Troop.ScanBuildings, stejný list jako SearchForCore)
        if (!BuildingsInRange.Contains(core))
        {
            Debug.LogWarning($"[Cargo-DIAG] Vyložení odmítnuto: Core {core.name} není v evidovaném dosahu Cargo.", this);
            return;
        }
        if(Vector3.Distance(core.GameObject().transform.position, this.GameObject().transform.position) > SightRange + 1)
        {
            Debug.LogWarning($"[Cargo-DIAG] Vyložení odmítnuto: Core {core.name} je fyzicky příliš daleko.", this);
            return;
        }

        // core musí patřit stejnému hráči jako harvester
        if (core.OwnerClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Cargo-DIAG] Vyložení odmítnuto: Core patří hráči {core.OwnerClientId}, Cargo hráči {OwnerClientId}.", this);
            return;
        }

        if (CoriumCargo.Value <= 0)
        {
            Debug.LogWarning("[Cargo-DIAG] Vyložení odmítnuto: Cargo nemá žádné corium.", this);
            return;
        }

        currentCore = core;
        currentDepositBuilding = core;
        IsDepositing.Value = true;
        nextDepositTick = Time.time;
    }

    private void StopHarvesting()
    {
        currentTarget = null;
        IsHarvesting.Value = false;
        if (Status.Value == WorkStatus.Harvesting) Status.Value = WorkStatus.Idle;
    }

    private void StopDepositing()
    {
        currentCore = null;
        currentDepositBuilding = null;
        IsDepositing.Value = false;
        if (Status.Value == WorkStatus.Depositing) Status.Value = WorkStatus.Idle;
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
        IsHarvesting.Value = true;
        AssignedResource = resource; Status.Value = WorkStatus.Harvesting;
        nextHarvestTick = Time.time;
    }

    private void BeginHarvestServer(Resource resource)
    {
        if (!IsServer || resource == null || !resource.IsSpawned || resource.Depleted ||
            Vector3.Distance(transform.position, resource.transform.position) > SightRange || CoriumCargo.Value >= MaxCargo)
            return;
        currentTarget = resource; AssignedResource = resource; IsHarvesting.Value = true;
        Status.Value = WorkStatus.Harvesting; nextHarvestTick = Time.time;
    }
}
