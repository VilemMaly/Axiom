using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class Harvester : Troop
{
    [Header("Harvester - sběr surovin")]
    public float WorkRange = 2.5f;
    public int HarvestCoriumPerSecond = 15;

    [Tooltip("Kolik coria může jednotka najednou převážet.")]
    public int MaxCargo = 100;

    [Header("Harvester - vykládka do core")]
    public int DepositCoriumPerSecond = 20;

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

    public override void OnSelectResource(Resource resource)
    {
        base.OnSelectResource(resource);
        selectedResource = resource;
        Debug.Log($"[Harvester-DIAG] {name} OnSelectResource -> selectedResource={(resource != null ? resource.name : "null")} | IsOwner={IsOwner} IsServer={IsServer}");
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
    public override void UpdateTroop()
    {
        // Detekce resources kolem sebe běží na všech instancích stejně jako
        // detekce budov v Troop.ScanBuildings - server tak má vlastní
        // autoritativní seznam, klient (owner) ho používá k výběru cíle.
        if (CoriumCargo.Value < MaxCargo)
            ScanForResources();

        // Stejně jako Atlas u SearchForConstructionTarget - hledání proběhne
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
            /*Debug.Log($"[Harvester-DIAG] {name} stav: IsOwner={IsOwner} IsServer={IsServer} AutoHarvestEnabled={AutoHarvestEnabled} " +
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
        Debug.Log($"[Harvester-DIAG] {name} MoveToTargetResource voláno, selectedResource={(selectedResource != null ? selectedResource.name : "null")}");
        if(selectedResource != null)
            RequestMove(selectedResource.GameObject().transform.position);
    }

    private void MoveToBase()
    {
        Debug.Log($"[Harvester-DIAG] {name} MoveToBase voláno, currentBase={(currentBase != null ? currentBase.name : "null")}");
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
                Debug.Log($"[Harvester] {name} detekuje resource {resource.name} v dosahu.");
            }
        }

        for (int i = resourcesInRange.Count - 1; i >= 0; i--)
        {
            Resource resource = resourcesInRange[i];

            if (resource == null || !currentResources.Contains(resource))
            {
                if (resource != null)
                    Debug.Log($"[Harvester] {name} ztrácí resource {resource.name} z dosahu.");

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

            Debug.Log($"[Harvester] {name} našel resource {resource.name} k sběru.");
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
            return;

        if (!coreReference.TryGet(out NetworkObject netObj))
            return;

        if (!netObj.TryGetComponent(out Core core))
            return;

        // serverová kontrola: core musí být skutečně v dosahu Harvesteru
        // (budovy v dosahu sleduje Troop.ScanBuildings, stejný list jako SearchForCore)
        if (!BuildingsInRange.Contains(core))
            return;
        if(Vector3.Distance(core.GameObject().transform.position, this.GameObject().transform.position) > SightRange + 1)
            return;

        // core musí patřit stejnému hráči jako harvester
        if (core.OwnerClientId != OwnerClientId)
            return;

        if (CoriumCargo.Value <= 0)
            return;

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
            return;

        if (!resourceReference.TryGet(out NetworkObject netObj))
            return;

        if (!netObj.TryGetComponent(out Resource resource))
            return;

        // serverová kontrola: resource musí být skutečně v dosahu Harvesteru
        if (!resourcesInRange.Contains(resource))
            return;

        if (!IsNearEnough(resource.GameObject().transform.position, this))
            return;

        if (resource.Depleted)
            return;

        currentTarget = resource;
        IsHarvesting.Value = true;
        nextHarvestTick = Time.time;
    }
}