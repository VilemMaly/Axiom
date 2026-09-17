using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class Atlas : Troop
{
    /// <summary>
    /// Jedna položka fronty rozestavěných budov, na které Atlas ještě čeká, než k nim dorazí.
    /// Nahrazuje dřívější dvě paralelní pole (BuildingPositions / BuildingIndexes) - obyčejný
    /// enum by tu nešel použít, protože potřebujeme s sebou nést i konkrétní Vector3 pozici
    /// a rotaci, ne jen pojmenovanou hodnotu, takže jde o jednoduchý struct.
    /// </summary>
    [System.Serializable]
    public struct PendingBuildRequest
    {
        public int BuildingIndex;
        public Vector3 Position;
        public int Rotation; // stupně kolem Y (0/90/180/270), viz BuildingPlacementController

        public PendingBuildRequest(int buildingIndex, Vector3 position, int rotation)
        {
            BuildingIndex = buildingIndex;
            Position = position;
            Rotation = rotation;
        }
    }

    [Header("Atlas - stavba a oprava")]
    public float RepairPower = 10f;
    public float WorkRange = 2.5f;

    [Header("Stavba")]
    public int BuildCoriumPerSecond = 15;

    private PlayerResources resources;
    public event Action<Atlas, int, Vector3, int, int> NearBuilding; // atlas, index, pozice, rotace, počet zbývajících ve frontě

    private Building currentTarget;
    private float nextBuildSendTime;

    private bool GoingToBuilding = false;
    private Vector3 BuildingPosition;
    private int BuildingIndex;
    private int BuildingRotation;
    private List<PendingBuildRequest> pendingBuildQueue = new List<PendingBuildRequest>();
    
    public NetworkVariable<bool> IsBuilding = new(writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsRepairing = new(writePerm: NetworkVariableWritePermission.Server);


    public override void OnSpawned()
    {
        if (IsServer)
        {
            resources = NetworkManager.Singleton
                .ConnectedClients[OwnerClientId]
                .PlayerObject
                .GetComponent<PlayerResources>();
        }
    }


    private void SearchForConstructionTarget()
    {
        foreach (Building building in BuildingsInRange)
        {
            if (building == null)
                continue;

            if (!building.IsConstructionComplete.Value)
            {
                Debug.Log($"[Atlas] {name} našel budovu {building.name} k dostavbě.");
                RequestBuildServerRpc(
                    building.NetworkObject
                );

                break;
            }
        }
    }


    public override void BuildBuilding(Building building)
    {
        if (!IsServer)
            return;

        if (building == null)
            return;

        currentTarget = building;
        nextBuildSendTime = Time.time + 1f;

        IsBuilding.Value = true;
        IsRepairing.Value = false;
    }



    public override void RepairBuilding(Building building)
    {
        if (!IsServer)
            return;

        if (building == null)
            return;


        currentTarget = building;

        IsRepairing.Value = true;
        IsBuilding.Value = false;
    }



    public override void UpdateTroop()
    {
        if (IsOwner && !IsBuilding.Value)
        {
            if(GoingToBuilding && Vector3.Distance(BuildingPosition, this.GameObject().transform.position) <= SightRange)
                BeginBuild();
            SearchForConstructionTarget();
        }
        if(IsOwner && !IsBuilding.Value && !GoingToBuilding && pendingBuildQueue.Count > 0)
        {
            PendingBuildRequest next = pendingBuildQueue[0];
            pendingBuildQueue.RemoveAt(0);
            RequestMoveToBuild(next.Position, next.BuildingIndex, next.Rotation);
        }
        if (!IsServer)
            return;


        if (IsBuilding.Value)
        {
            if (currentTarget == null ||
                currentTarget.IsConstructionComplete.Value)
            {
                Debug.Log($"[Atlas] {name} staví budovu {currentTarget.name}. Zbývá: {currentTarget.RequiredCorium - currentTarget.StoredCorium.Value} coria.");
                StopBuilding();
                return;
            }

            if (Time.time >= nextBuildSendTime)
            {
                Debug.Log($"[Atlas] {name} staví budovu {currentTarget.name}. Zbývá: {currentTarget.RequiredCorium - currentTarget.StoredCorium.Value} coria.");
                nextBuildSendTime = Time.time + 1f;
                int amount = BuildCoriumPerSecond;

                if (resources != null &&
                    resources.TrySpend(amount, 0))
                {
                    currentTarget.ReceiveCorium(amount);
                }
            }
        }


        if (IsRepairing.Value)
        {
            // později doplníš opravu
        }
    }



    private void StopBuilding()
    {
        currentTarget = null;

        IsBuilding.Value = false;
        IsRepairing.Value = false;
    }



    [ServerRpc]
    private void RequestBuildServerRpc(
        NetworkObjectReference buildingReference,
        ServerRpcParams rpcParams = default)
    {
        Debug.Log($"[Atlas] RequestBuildServerRpc called by {rpcParams.Receive.SenderClientId} for building {buildingReference}");
        // bezpečnost - pouze vlastník jednotky může dávat příkazy
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;


        if (!buildingReference.TryGet(out NetworkObject netObj))
            return;


        if (!netObj.TryGetComponent(out Building building))
            return;


        Debug.Log($"[Atlas] {name} žádá o stavbu budovy {building.name}.");
        // serverová kontrola:
        // budova musí být skutečně v dosahu Atlasu
        if (!BuildingsInRange.Contains(building))
            return;



        // už hotová budova
        if (building.IsConstructionComplete.Value)
            return;


        BuildBuilding(building);
    }



    [ServerRpc]
    public void RequestRepairServerRpc(
        NetworkObjectReference buildingReference,
        ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;


        if (!buildingReference.TryGet(out NetworkObject netObj))
            return;


        if (!netObj.TryGetComponent(out Building building))
            return;


        if (!BuildingsInRange.Contains(building))
            return;


        RepairBuilding(building);
    }

    public void RequestMoveToBuild(Vector3 position, int buildingIndex, int rotation)
    {
        if(GoingToBuilding || IsBuilding.Value)
        {
            pendingBuildQueue.Add(new PendingBuildRequest(buildingIndex, position, rotation));
            return;
        }
        GoingToBuilding = true;
        RequestMoveServerRpc(position);
        BuildingPosition = position;
        BuildingIndex = buildingIndex;
        BuildingRotation = rotation;
        if(BuildingPosition == Vector3.zero)
        {
            Debug.Log("Pozice co dostal atlas je neplatná");
            GoingToBuilding = false;
        }

    }

    private void BeginBuild()
    {
        GoingToBuilding = false;
        NearBuilding?.Invoke(this, BuildingIndex, BuildingPosition, BuildingRotation, pendingBuildQueue.Count);
        BuildingPosition = Vector3.zero;
    }
}