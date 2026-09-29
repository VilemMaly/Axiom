using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Umístit na hlavní Player NetworkObject (stejné místo jako Selector, TroopInteractor,
/// TroopSpawner). Zpracovává žádost o postavení NOVÉ budovy na dané pozici.
///
/// Kdo smí zavolat žádost:
/// - Core: TroopInteractor.Build() volá RequestBuildServerRpc rovnou, bez Atlasu
///   (bootstrap na začátku hry, kdy hráč ještě žádného Atlase nemá).
/// - Cokoliv jiného: žádost posílá až samotný Atlas (viz Atlas.RequestMoveToBuild),
///   jakmile fyzicky dorazí do WorkRange od cílové pozice. Server si to znovu ověří.
///
/// Po úspěšném spawnu budova začíná nedostavěná (StoredCorium = 0,
/// IsConstructionComplete = false - viz Building.OnNetworkSpawn/ReceiveCorium(0)).
/// Samotné dostavění pak už řeší existující flow: Atlas.SearchForConstructionTarget
/// najde budovu v BuildingsInRange a začne do ní posílat corium.
/// </summary>
public class Build : NetworkBehaviour
{
    [System.Serializable]
    public class BuildingEntry
    {
        public string buildingName;
        public Building.BuildingType type;
        public GameObject prefab;

        [Tooltip("Poloměr koule, kterou server zkouší 'nasadit' na dané místo, aby ověřil, že tam prefab nekoliduje.")]
        public float checkRadius = 1f;
    }

    [Header("Raycast na zem (stejný princip jako u TroopSpawneru)")]
    [SerializeField] private LayerMask groundLayerMask;
    [SerializeField] private float raycastStartHeight = 50f;
    [SerializeField] private float raycastMaxDistance = 200f;

    [Header("Kontrola kolize před stavbou")]
    [SerializeField] private LayerMask obstacleLayerMask;

    [Header("Seznam budov (pořadí = index, odpovídá UI tlačítkům)")]
    [SerializeField] private List<BuildingEntry> buildingPrefabs = new List<BuildingEntry>();

    // ============================================================
    // RPC - žádost o postavení nové budovy
    // ============================================================

    /// <summary>
    /// atlasRef se předává jen u budov jiných než Core - je to reference na Atlase,
    /// který o stavbu žádá, aby server ověřil, že je opravdu vlastní a opravdu blízko.
    /// Pro Core se předává default a validace Atlasu se přeskočí.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    public void RequestBuildServerRpc(
        int buildingIndex,
        Vector3 position,
        int rotation,
        NetworkObjectReference atlasRef,
        ServerRpcParams rpcParams = default)
    {
        ulong requestingClientId = rpcParams.Receive.SenderClientId;

        Debug.Log(
            $"[Build] RequestBuildServerRpc přijat | " +
            $"Player={requestingClientId} | " +
            $"BuildingIndex={buildingIndex} | " +
            $"Position={position} | " +
            $"Rotation={rotation}"
        );

        if (buildingIndex < 0 || buildingIndex >= buildingPrefabs.Count)
        {
            Debug.LogWarning(
                $"[Build] STAVBA NEDOKONČENA | " +
                $"Player={requestingClientId} | " +
                $"Důvod: neplatný index budovy {buildingIndex} | " +
                $"Počet položek={buildingPrefabs.Count}"
            );

            return;
        }

        BuildingEntry entry = buildingPrefabs[buildingIndex];

        Debug.Log(
            $"[Build] Vybraná budova | " +
            $"Player={requestingClientId} | " +
            $"Name='{entry.buildingName}' | " +
            $"Type={entry.type} | " +
            $"Index={buildingIndex}"
        );

        if (entry.prefab == null)
        {
            Debug.LogWarning(
                $"[Build] STAVBA NEDOKONČENA | " +
                $"Player={requestingClientId} | " +
                $"Budova='{entry.buildingName}' | " +
                $"Důvod: prefab není přiřazen."
            );

            return;
        }

        Debug.Log(
            $"[Build] Prefab nalezen | " +
            $"Budova='{entry.buildingName}' | " +
            $"Prefab='{entry.prefab.name}'"
        );

        // Core lze postavit bez Atlasu (bootstrap) - u všeho ostatního musí žádost
        // reálně přijít od vlastního Atlasu, který je fyzicky u místa stavby.
        if (entry.type != Building.BuildingType.Core)
        {
            Debug.Log(
                $"[Build] Kontrola Atlasu | " +
                $"Budova='{entry.buildingName}' | " +
                $"Player={requestingClientId}"
            );

            if (!atlasRef.TryGet(out NetworkObject atlasNetObj))
            {
                Debug.LogWarning(
                    $"[Build] STAVBA NEDOKONČENA | " +
                    $"Player={requestingClientId} | " +
                    $"Budova='{entry.buildingName}' | " +
                    $"Důvod: Atlas reference je neplatná."
                );

                return;
            }

            Atlas atlas = atlasNetObj.GetComponent<Atlas>();

            if (atlas == null || atlas.OwnerClientId != requestingClientId)
            {
                Debug.LogWarning(
                    $"[Build] STAVBA NEDOKONČENA | " +
                    $"Player={requestingClientId} | " +
                    $"Budova='{entry.buildingName}' | " +
                    $"Důvod: Atlas je null nebo patří jinému hráči. " +
                    $"AtlasOwner={(atlas != null ? atlas.OwnerClientId.ToString() : "NULL")}"
                );

                return;
            }

            float distance = Vector3.Distance(atlas.transform.position, position);

            Debug.Log(
                $"[Build] Kontrola vzdálenosti Atlase | " +
                $"Budova='{entry.buildingName}' | " +
                $"AtlasPosition={atlas.transform.position} | " +
                $"BuildPosition={position} | " +
                $"Distance={distance:F2} | " +
                $"SightRange={atlas.SightRange:F2}"
            );

            if (distance > atlas.SightRange)
            {
                Debug.LogWarning(
                    $"[Build] STAVBA NEDOKONČENA | " +
                    $"Player={requestingClientId} | " +
                    $"Budova='{entry.buildingName}' | " +
                    $"Důvod: Atlas je příliš daleko. " +
                    $"Distance={distance:F2} > SightRange={atlas.SightRange:F2}"
                );

                return;
            }

            Debug.Log(
                $"[Build] Atlas je v povolené vzdálenosti | " +
                $"Budova='{entry.buildingName}'"
            );
        }
        else
        {
            Debug.Log(
                $"[Build] Budova '{entry.buildingName}' je Core -> Atlas se nekontroluje."
            );
        }

        // Validace pozice + spawn teď žije centrálně v Building.cs (sdílené se
        // stejnou logikou, kterou dřív měl BuildingPlacementController), aby se
        // nekopírovala na dvou místech.
        Debug.Log(
            $"[Build] Začíná validace pozice | " +
            $"Budova='{entry.buildingName}' | " +
            $"Position={position} | " +
            $"CheckRadius={entry.checkRadius}"
        );

        if (!Building.TryValidatePosition(
                position,
                entry.checkRadius,
                groundLayerMask,
                obstacleLayerMask,
                raycastStartHeight,
                raycastMaxDistance,
                out Vector3 groundedPosition,
                out string failReason))
        {
            Debug.LogWarning(
                $"[Build] STAVBA NEDOKONČENA | " +
                $"Player={requestingClientId} | " +
                $"Budova='{entry.buildingName}' | " +
                $"Důvod validace: {failReason}"
            );

            return;
        }

        Debug.Log(
            $"[Build] Pozice VALIDNÍ | " +
            $"Budova='{entry.buildingName}' | " +
            $"GroundedPosition={groundedPosition}"
        );

        Quaternion spawnRotation = Quaternion.Euler(0f, rotation, 0f);

        Debug.Log(
            $"[Build] Pokus o spawn budovy | " +
            $"Player={requestingClientId} | " +
            $"Budova='{entry.buildingName}' | " +
            $"Type={entry.type} | " +
            $"Position={groundedPosition} | " +
            $"Rotation={rotation}°"
        );

        Building.SpawnAndAssignOwnership(
            entry.prefab,
            groundedPosition,
            spawnRotation,
            requestingClientId
        );

        Debug.Log(
            $"[Build] SPAWN BUDOVY DOKONČEN | " +
            $"Player={requestingClientId} | " +
            $"Budova='{entry.buildingName}' | " +
            $"Type={entry.type}"
        );
    }
}