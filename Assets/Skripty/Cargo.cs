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
    private float nextDebugLog; // jen pro diagnostiku, neovlivňuje logiku

    public NetworkVariable<bool> IsHarvesting = new(writePerm: NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsDepositing = new(writePerm: NetworkVariableWritePermission.Server);
    private Core currentCore;
    private float nextDepositTick;

    public bool AutoHarvestEnabled { get; set; } = false;
    public Building currentBase;
    private Resource selectedResource;
    private bool moveToBase = false;
    private bool moveToResource = false;

    /// <summary>Kolik coria už tahle jednotka nasbírala a veze v sobě.</summary>
    public NetworkVariable<int> CoriumCargo = new(writePerm: NetworkVariableWritePermission.Server);

    public override void OnSpawned()
    {
        if (IsServer)
            playerResources = NetworkManager.Singleton.ConnectedClients[OwnerClientId]
                .PlayerObject.GetComponent<PlayerResources>();
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

    public void changeAutoState()
    {
        Debug.Log("change auto state");
        AutoHarvestEnabled = !AutoHarvestEnabled;
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

        StopHarvesting();
        StopDepositing();
    }

    public override void UpdateTroop()
    {
        UpdateConstruction();
        if (IsBuilding.Value || HasPendingBuildOrders) return;
        UpdateHarvesting();
    }

    private void UpdateConstruction()
    {
        if (IsOwner && !IsBuilding.Value)
        {
            if (GoingToBuilding && Vector3.Distance(BuildingPosition, transform.position) <= SightRange)
                BeginBuild();
            foreach (Building building in BuildingsInRange)
            {
                if (building == null || building.IsConstructionComplete.Value) continue;
                RequestBuildServerRpc(building.NetworkObject);
                break;
            }
        }

        if (IsOwner && !IsBuilding.Value && !GoingToBuilding && pendingBuildQueue.Count > 0)
        {
            PendingBuildRequest next = pendingBuildQueue[0];
            pendingBuildQueue.RemoveAt(0);
            RequestMoveToBuild(next.Position, next.BuildingIndex, next.Rotation);
        }

        if (!IsServer) return;
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
                if (playerResources != null && playerResources.TrySpend(BuildCoriumPerSecond, 0))
                    currentBuildTarget.ReceiveCorium(BuildCoriumPerSecond);
            }
        }
    }

    private void StopBuilding()
    {
        currentBuildTarget = null;
        IsBuilding.Value = false;
        IsRepairing.Value = false;
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
        if (CoriumCargo.Value < MaxCargo)
            ScanForResources();

        // Hledání proběhne
        // jen tehdy, když jednotka nemá platný cíl. Pokud currentBase/selectedResource
        // už ukazují na platnou budovu/resource, metoda se rovnou vrátí a nic neprohledává.
        if (IsOwner)
        {
            AutoDetectCore();
            AutoDetectResource();
        }

        if (IsOwner && !IsHarvesting.Value && CoriumCargo.Value < MaxCargo)
        {
            SearchForHarvestingTarget();
        }

        if (IsOwner && !IsDepositing.Value && CoriumCargo.Value >= MaxCargo)
        {
            isNearCore();
        }

        if (Time.time >= nextDebugLog)
        {
            nextDebugLog = Time.time + 1f;
            /*Debug.Log($"[Cargo-DIAG] {name} stav: IsOwner={IsOwner} IsServer={IsServer} AutoHarvestEnabled={AutoHarvestEnabled} " +
                      $"IsHarvesting={IsHarvesting.Value} CoriumCargo={CoriumCargo.Value}/{MaxCargo} " +
                      $"selectedResource={(selectedResource != null ? selectedResource.name : "null")} currentBase={(currentBase != null ? currentBase.name : "null")} " +
                      $"| podmínka(IsOwner&&AutoHarvestEnabled&&!IsHarvesting&&!IsServer)={(IsOwner && AutoHarvestEnabled && !IsHarvesting.Value && !IsServer)}");
        */
        }

        if (IsOwner && AutoHarvestEnabled && !IsHarvesting.Value && !IsDepositing.Value)
        {
            if (CoriumCargo.Value >= MaxCargo)
            {
                if (currentBase != null && !moveToBase){
                    MoveToBase();
                    moveToBase = true;
                    moveToResource = false;
                    }
                    
            }
            else
            {
                if (selectedResource != null && !moveToResource){
                    MoveToTargetResource();
                    moveToResource = true;
                    moveToBase = false;
                    }
                    
            }
        }

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
                }
            }
        }

        if (IsDepositing.Value)
        {
            if (currentCore == null || CoriumCargo.Value <= 0)
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
                    currentCore.CoriumReserve(amount, this);
                    CoriumCargo.Value -= amount;
                }
            }
        }
    }


    private void MoveToTargetResource()
    {
        Debug.Log($"[Cargo-DIAG] {name} MoveToTargetResource voláno, selectedResource={(selectedResource != null ? selectedResource.name : "null")}");
        if(selectedResource != null)
            RequestMove(selectedResource.GameObject().transform.position);
    }

    private void MoveToBase()
    {
        Debug.Log($"[Cargo-DIAG] {name} MoveToBase voláno, currentBase={(currentBase != null ? currentBase.name : "null")}");
        if(currentBase != null && currentBase.Type == Building.BuildingType.Core && currentBase.IsOwner)
            RequestMove(currentBase.GameObject().transform.position);
    }

    private void ScanForResources()
    {
        if (selectedResource != null && !selectedResource.Depleted && selectedResource.CoriumAmount.Value > 0)
            return;
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

    private void SearchForHarvestingTarget()
    {
        foreach (Resource resource in resourcesInRange)
        {
            if (resource == null || resource.Depleted)
                continue;
            if (!IsNearEnough(resource.GameObject().transform.position, this))
                return;

            Debug.Log($"[Cargo] {name} našel resource {resource.name} k sběru.");
            RequestHarvestTargetServerRpc(resource.NetworkObject);
            break;
        }
    }

    private void isNearCore()
    {
        if(Vector3.Distance(currentBase.GameObject().transform.position, this.GameObject().transform.position) < SightRange + 1)
        {
            Debug.Log("Deposituji corium");
            RequestDepositCoriumServerRpc(currentBase.NetworkObject);
        }
    }

    /// <summary>
    /// Automatická obdoba Atlasova SearchForConstructionTarget - hledá kolem sebe
    /// vlastní Core (stejně jako SearchForCore prochází BuildingsInRange), ale
    /// jen pokud currentBase ještě neukazuje na platnou budovu. Používá se pro
    /// auto-harvest chození tam a zpátky (MoveToBase), takže si tu žádnou zprávu
    /// server neposílá - jen si klient lokálně zapamatuje cíl.
    /// </summary>
    private void AutoDetectCore()
    {
        if (currentBase != null && currentBase.Type == Building.BuildingType.Core && currentBase.IsOwner)
            return;

        Building previous = currentBase;
        currentBase = null;

        foreach (Building building in BuildingsInRange)
        {
            if (building == null || building.Type != Building.BuildingType.Core || !building.IsOwner)
                continue;

            currentBase = building;
            break;
        }

        // Pokud se cíl změnil (starý zmizel/byl zničen a našel se nový), je potřeba
        // zrušit příznak moveToBase, aby MoveToBase() poslal příkaz k chůzi znovu.
        if (currentBase != previous)
            moveToBase = false;
    }

    /// <summary>
    /// Automatické hledání resource s alespoň 1 coriem v dosahu (resourcesInRange
    /// plní ScanForResources) - hledá jen tehdy, když selectedResource ještě
    /// neukazuje na platnou, nevyčerpanou surovinu.
    /// </summary>
    private void AutoDetectResource()
    {
        if (selectedResource != null && !selectedResource.Depleted && selectedResource.CoriumAmount.Value > 0)
            return;

        Resource previous = selectedResource;
        selectedResource = null;

        foreach (Resource resource in resourcesInRange)
        {
            if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0)
                continue;

            selectedResource = resource;
            break;
        }

        if (selectedResource != previous)
            moveToResource = false;
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
        IsDepositing.Value = true;
        nextDepositTick = Time.time;
    }

    private void StopHarvesting()
    {
        currentTarget = null;
        IsHarvesting.Value = false;
    }

    private void StopDepositing()
    {
        currentCore = null;
        IsDepositing.Value = false;
    }

    /// <summary>
    /// Server sem připíše vytěžené corium. Volá se výhradně z Resource
    /// (konkrétně z Corium.DepleteResource) poté, co si ověří vlastnictví.
    /// </summary>
    public void AddToCargo(int amount)
    {
        if (!IsServer || amount <= 0)
            return;

        CoriumCargo.Value = Mathf.Min(CoriumCargo.Value + amount, MaxCargo);
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
        nextHarvestTick = Time.time;
    }
}
