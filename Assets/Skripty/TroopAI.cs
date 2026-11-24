
using System;
using System.Collections.Generic;
using Unity.Multiplayer.Tools.NetStats;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// Vyhledává cíle a vykonává společné rozkazy jednotek ve skupině.
/// </summary>
public class TroopAI : MonoBehaviour
{
    #region Proměnné

    [Header("Nastavení AI")]
    [SerializeField] private float thinkInterval = 1f;
    [SerializeField] private float defendRadius = 12f;

    [Header("Reference na skupinu")]
    private TroopGroup group;

    [Header("Stav AI")]
    private float nextThink;
    private Vector3 defendPosition;

    // Uchovává poslední rozkazy k návratu jednotek na pozici.
    private readonly Dictionary<Troop, Vector3> idleReturnOrders =
        new Dictionary<Troop, Vector3>();

    #endregion


    #region Inicializace a veřejné příkazy

    public void Initialize(TroopGroup owner)
    {
        group = owner;
    }

    public void SetDefendPosition(Vector3 position)
    {
        defendPosition = position;
    }

    // Přepne skupinu do režimu sběru surovin.
    public void BeginHarvest()
    {
        if (group != null)
            group.State = TroopGroupState.Harvesting;
    }

    #endregion


    #region Reakce na napadení

    // Reakce na napadení nepřátelskou skupinou.
    public void RespondTo(TroopGroup attacker)
    {
        Debug.Log($"[Troop AI] Responding to attack status: {group.State}");

        if (group == null || attacker == null || group.Units.Count == 0)
        {
            Debug.Log($"[Troop AI] Chyba v respond to útok status: {group.State}");
            return;
        }

        if (group.State == TroopGroupState.Attacking)
        {
            if (group.attackingBuilding)
            {
                group.attackingBuilding = false;
                group.respondingToAttack = true;

                // Zastavení původních příkazů jednotek.
                // Následně mohou zahájit útok na nepřátelskou skupinu.
                foreach (Troop troop in group.Units)
                {
                    troop.StopCurrentTask();
                }
            }

            bool anyAttacking = false;

            foreach (Troop troop in group.Units)
            {
                if (troop == null) continue;

                if (troop.hasTarget())
                {
                    anyAttacking = true;
                    Debug.Log($"[TroopAI] Group {group.GroupId} found attacking {troop.Type}");
                }
            }

            if (anyAttacking == false)
            {
                Debug.Log($"[TroopAI] Group {group.GroupId} is attacking but has no targets, responding to attacker {attacker.name} owned by {attacker.OwnerClientId}");
                group.AttackGroup(attacker);
            }
        }
        else if (group.State == TroopGroupState.Idle || group.State == TroopGroupState.Harvesting)
        {
            Debug.Log($"[TroopAI] Group {group.GroupId} is idle or harvesting, responding to attacker {attacker.name} owned by {attacker.OwnerClientId}");
            group.AttackGroup(attacker);
        }
    }


    // Reakce na napadení nepřátelskou budovou.
    public void RespondTo(Building attacker)
    {
        Debug.Log($"[Troop AI] Responding to attack status: {group.State}");

        if (group == null || attacker == null || group.Units.Count == 0)
        {
            Debug.Log($"[Troop AI] Chyba v respond to útok status: {group.State}");
            return;
        }

        if (group.State == TroopGroupState.Attacking)
        {
            bool anyAttacking = false;

            foreach (Troop troop in group.Units)
            {
                if (troop == null) continue;

                if (troop.hasTarget())
                {
                    anyAttacking = true;
                    Debug.Log($"[TroopAI] Group {group.GroupId} found attacking {troop.Type}");
                }
            }

            if (anyAttacking == false)
            {
                Debug.Log($"[TroopAI] Group {group.GroupId} is attacking but has no targets, responding to attacker {attacker.name} owned by {attacker.OwnerClientId}");
                group.AttackBuilding(attacker);
            }
        }
        else if (group.State == TroopGroupState.Idle || group.State == TroopGroupState.Harvesting)
        {
            Debug.Log($"[TroopAI] Group {group.GroupId} is idle or harvesting, responding to attacker {attacker.name} owned by {attacker.OwnerClientId}");
            group.AttackBuilding(attacker);
        }
    }

    #endregion


    #region Hlavní smyčka AI

    private void Update()
    {
        if (group == null || Time.time < nextThink || !IsServer()) return;

        nextThink = Time.time + Mathf.Max(.1f, thinkInterval);

        group.Prune();

        if (group.Units.Count == 0) return;

        // Průběžné vyhledávání šrotu v okolí jednotek.
        scanForScrap();

        // ================================================
        // OBRANA
        // ================================================

        if (group.Task == TroopGroupTask.Defend)
        {
            ScanForAttackers(false);

            if (group.State == TroopGroupState.Idle)
                ReturnDistantUnitsToPoint(defendPosition);
        }

        // ================================================
        // ÚTOK
        // ================================================

        else if (group.Task == TroopGroupTask.Attack)
        {
            // 1. Nepřátelské jednotky mají přednost.
            ScanForAttackers(true);

            // 2. Skupina bojuje – jednotkám bez cíle se přiřadí nový cíl.
            if (group.State == TroopGroupState.Attacking)
            {
                bool anyCombatTarget = false;

                foreach (Troop troop in group.Units)
                {
                    if (troop != null &&
                        troop.GetComponent<GenericAttacker>() != null &&
                        troop.hasTarget())
                    {
                        anyCombatTarget = true;
                        break;
                    }
                }

                if (anyCombatTarget)
                {
                    foreach (Troop troop in group.Units)
                    {
                        if (troop != null &&
                            troop.GetComponent<GenericAttacker>() != null &&
                            !troop.hasTarget())
                        {
                            Debug.Log($"[TroopAI] Group {group.GroupId} has a unit {troop.name} without a target, returning it to attack objective");

                            if (group.TargetGroup != null && group.TargetGroup.Units.Count > 0)
                            {
                                Troop attackTroop = group.TargetGroup.Units[
                                    UnityEngine.Random.Range(0, group.TargetGroup.Units.Count)
                                ];

                                if (attackTroop == null || attackTroop.Health.Value <= 0)
                                {
                                    Debug.Log("[TroopAI] jednotka náhodně přiřazená je neplatná");
                                    continue;
                                }

                                troop.Attack(attackTroop);
                                troop.MoveTo(attackTroop.gameObject.transform.position);
                            }
                        }
                    }
                }

                Debug.Log($"[TroopAI] Group {group.GroupId} has any combat target: {anyCombatTarget}");

                if (!anyCombatTarget)
                {
                    group.State = TroopGroupState.Idle;
                    group.attackingBuilding = false;
                    group.respondingToAttack = false;
                }
            }

            // 3. Skupina je nečinná – vyhledá nepřátelské budovy v okolí.
            // Nalezená budova změní stav skupiny na Attacking.
            if (group.State == TroopGroupState.Idle)
                ScanForEnemyBuildings();

            // 4. Skupina stále nemá cíl – přesune se k místu útoku.
            if (group.State == TroopGroupState.Idle)
                ReturnDistantUnitsToPoint(GetAttackObjective());
        }

        // ================================================
        // SBĚR SUROVIN
        // ================================================

        else if (group.Task == TroopGroupTask.Harvest)
        {
            UpdateHarvestOrders();
        }
    }

    #endregion


    #region Výběr útočných cílů

    // Určí cílovou pozici podle aktuálního cíle skupiny.
    private Vector3 GetAttackObjective()
    {
        if (group.TargetTroop != null && group.TargetTroop.Health.Value > 0)
            return group.TargetTroop.transform.position;

        if (group.TargetBuilding != null && group.TargetBuilding.Health.Value > 0)
            return group.TargetBuilding.transform.position;

        return group.ObjectivePosition;
    }


    // Vyhledá nejbližší nepřátelskou budovu v dosahu jednotek.
    private bool ScanForEnemyBuildings()
    {
        Building nearest = null;
        float best = float.MaxValue;
        Vector3 center = Center();

        foreach (Troop troop in group.Units)
        {
            if (troop == null || troop.GetComponent<GenericAttacker>() == null)
                continue;

            foreach (Building building in troop.Scan<Building>(TroopScanType.Building))
            {
                if (building == null ||
                    building.OwnerClientId == group.OwnerClientId ||
                    building.Health.Value <= 0)
                    continue;

                float distance = (building.transform.position - center).sqrMagnitude;

                if (distance < best)
                {
                    best = distance;
                    nearest = building;
                }
            }
        }

        if (nearest == null)
        {
            Debug.Log("[TroopAI] žádná budova nebyla nalezena");
            return false;
        }

        Debug.Log($"[TroopAI] nalezena nejbližší budova {nearest.Type}");
        group.AttackBuilding(nearest);

        return true;
    }


    // Vyhledá nepřátelské skupiny v okolí jednotek.
    private bool ScanForAttackers(bool attackAnyEnemy)
    {
        foreach (Troop unit in group.Units)
        {
            if (unit == null) continue;

            foreach (TroopGroup enemy in unit.ScanTroopGroups())
            {
                if (enemy == null || enemy.OwnerClientId == group.OwnerClientId)
                    continue;

                if (!attackAnyEnemy && enemy.Task != TroopGroupTask.Attack)
                    continue;

                group.HandleEnemyGroupDetected(enemy);
                return true;
            }
        }

        Debug.Log($"[TroopAI] Group {group.GroupId} found no attackers.");
        return false;
    }


    // Vybere nejslabší jednotku podle zdraví a útočné síly.
    private static Troop SelectWeakest(List<Troop> troops)
    {
        Troop weakest = null;
        float score = float.MaxValue;

        foreach (Troop troop in troops)
        {
            if (troop == null) continue;

            float value = troop.Health.Value +
                          troop.Damage * Mathf.Max(1f, troop.AttackRange);

            if (value < score)
            {
                score = value;
                weakest = troop;
            }
        }

        return weakest;
    }


    // Vypočítá celkovou bojovou sílu skupiny.
    private static float EstimateStrength(IReadOnlyList<Troop> troops)
    {
        float value = 0f;

        foreach (Troop troop in troops)
        {
            if (troop != null)
                value += troop.Health.Value +
                         troop.Damage * Mathf.Max(1f, troop.AttackRange);
        }

        return value;
    }

    #endregion


    #region Pohyb a rozmístění jednotek

    // Vrátí příliš vzdálené jednotky na určenou pozici.
    private void ReturnDistantUnitsToPoint(Vector3 destination)
    {
        if (group == null || group.State != TroopGroupState.Idle || !IsServer())
            return;

        int unitCount = group.Units.Count;

        for (int i = 0; i < unitCount; i++)
        {
            Troop troop = group.Units[i];

            if (troop == null) continue;

            // Jednotka je dostatečně blízko cílové pozice.
            if (Vector3.Distance(troop.transform.position, destination) <= defendRadius)
            {
                idleReturnOrders.Remove(troop);
                continue;
            }

            // Kontrola, zda už jednotka nemá stejný pohybový rozkaz.
            bool sameOrder =
                idleReturnOrders.TryGetValue(troop, out Vector3 previousDestination) &&
                (previousDestination - destination).sqrMagnitude <= 0.25f;

            if (sameOrder && troop.IsMoving) continue;

            troop.RequestMove(destination, i, unitCount);
            idleReturnOrders[troop] = destination;
        }
    }


    // Vypočítá střední pozici všech jednotek ve skupině.
    private Vector3 Center()
    {
        Vector3 center = Vector3.zero;
        int count = 0;

        foreach (Troop troop in group.Units)
        {
            if (troop != null)
            {
                center += troop.transform.position;
                count++;
            }
        }

        return count > 0 ? center / count : transform.position;
    }

    #endregion


    #region Vyhledávání surovin

    // Vyhledá šrot v okolí jednotek a zaregistruje ho do správce surovin.
    private void scanForScrap()
    {
        foreach (Troop troop in group.Units)
        {
            foreach (Resource resource in troop.Scan<Resource>(TroopScanType.Scrap))
            {
                if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0)
                    continue;

                if (resource is Scrap)
                {
                    group.Owner.ResourceManager.RegisterScrap(resource as Scrap);
                }
            }
        }
    }

    #endregion


    #region Sběr a ukládání surovin

    private void UpdateHarvestOrders()
    {
        foreach (Troop troop in group.Units)
        {
            if (!(troop is Cargo cargo)) continue;

            if (group.Owner == null || group.Owner.BuildingInteractor == null)
                return;

            IReadOnlyList<Building> buildings = group.Owner.BuildingInteractor.MyBuildings;

            // ================================================
            // SBĚR SUROVIN
            // ================================================

            if (!cargo.IsCargoFull && !(cargo.IsDepositing.Value && cargo.HasCargo))
            {
                cargo.IsDepositing.Value = false;
                cargo.IsHarvesting.Value = false;

                // Vyhledání nejbližšího vlastního Core.
                Core nearestCore = null;
                float coreDistance = float.MaxValue;

                foreach (Building building in buildings)
                {
                    if (building == null ||
                        building.OwnerClientId != group.OwnerClientId ||
                        building.Type != Building.BuildingType.Core ||
                        !(building is Core core))
                        continue;

                    float distance = (core.transform.position - cargo.transform.position).sqrMagnitude;

                    if (distance < coreDistance)
                    {
                        nearestCore = core;
                        coreDistance = distance;
                    }
                }

                // Vyhledání nejbližší suroviny vzhledem ke Core.
                Resource nearestResource = null;
                float resourceDistance = float.MaxValue;

                ResourceManager resourceManager = group.Owner.ResourceManager;

                if (nearestCore != null && resourceManager != null)
                {
                    foreach (Resource resource in resourceManager.Resources)
                    {
                        if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0)
                            continue;

                        if (resource is Corium)
                            Debug.Log("[TroopAI] nejbližší scan ke core je korium");

                        if (resource is Scrap)
                            Debug.Log("[TroopAI] nejbližší scan ke core je scrap");

                        float distance = (resource.transform.position - nearestCore.transform.position).sqrMagnitude;

                        if (distance < resourceDistance)
                        {
                            nearestResource = resource;
                            resourceDistance = distance;
                        }
                    }
                }

                // Vyhledání surovin v okolí samotné jednotky.
                Resource scannedResource = null;
                float scannedResourceDistance = float.MaxValue;

                foreach (Resource resource in cargo.Scan<Resource>(TroopScanType.Scrap))
                {
                    Debug.Log("[TroopAI] Scan scrapu kolem sebe");

                    if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0)
                        continue;

                    if (resource is Scrap)
                    {
                        resourceManager.RegisterScrap(resource as Scrap);
                    }

                    float distance = (resource.transform.position - cargo.transform.position).sqrMagnitude;

                    if (distance < scannedResourceDistance)
                    {
                        scannedResource = resource;
                        scannedResourceDistance = distance;
                    }
                }

                // Pokud není dostupná žádná surovina, jednotka ukončí aktuální úkol.
                if (nearestResource == null)
                {
                    Debug.Log("[TroopAI] Žádný resource poblíž");
                    cargo.StopCurrentTask();
                    cargo.status.Value = Troop.State.idle;
                    continue;
                }

                // Vyhledání koria v okolí jednotky.
                foreach (Resource resource in cargo.Scan<Resource>(TroopScanType.Coria))
                {
                    Debug.Log("[TroopAI] Scan coria kolem sebe");

                    if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0)
                        continue;

                    float distance = (resource.transform.position - cargo.transform.position).sqrMagnitude;

                    if (distance < scannedResourceDistance)
                    {
                        Debug.Log("[TroopAI] Našlo se korium kolem sebe");
                        scannedResource = resource;
                        scannedResourceDistance = distance;
                    }
                }

                // Zahájení sběru nebo přesun k nejbližší surovině.
                if (scannedResource != null)
                {
                    Debug.Log("[TroopAI] cargo harvestuje");
                    cargo.IsHarvesting.Value = true;
                    cargo.Harvest(scannedResource);
                }
                else if (cargo.status.Value != Troop.State.moving || !cargo.IsMoving)
                {
                    Debug.Log("[TroopAI] pohyb k nearest resource");
                    cargo.MoveTo(nearestResource.transform.position);
                }

                continue;
            }

            // ================================================
            // UKLÁDÁNÍ SUROVIN
            // ================================================

            cargo.IsHarvesting.Value = false;

            // Vyhledání nejbližší budovy, která potřebuje Corium.
            Building depositTarget = null;
            Building.BuildingType selectedBuildingType = Building.BuildingType.Core;
            float depositDistance = float.MaxValue;

            foreach (Building building in buildings)
            {
                if (building == null ||
                    building.OwnerClientId != group.OwnerClientId ||
                    building.MissingCorium <= 0)
                    continue;

                float distance = (building.transform.position - cargo.transform.position).sqrMagnitude;

                if (distance < depositDistance)
                {
                    depositTarget = building;
                    selectedBuildingType = building.Type;
                    depositDistance = distance;
                }
            }

            // Pokud žádná budova nepotřebuje Corium, vyhledá nejbližší Core.
            if (depositTarget == null)
            {
                float nearestCoreDistance = float.MaxValue;

                foreach (Building building in buildings)
                {
                    if (building == null ||
                        building.OwnerClientId != group.OwnerClientId ||
                        building.Type != Building.BuildingType.Core)
                        continue;

                    float distance = (building.transform.position - cargo.transform.position).sqrMagnitude;

                    if (distance < nearestCoreDistance)
                    {
                        depositTarget = building;
                        selectedBuildingType = building.Type;
                        nearestCoreDistance = distance;
                    }
                }
            }

            // Pokud není dostupná žádná cílová budova, jednotka ukončí úkol.
            if (depositTarget == null)
            {
                cargo.IsDepositing.Value = false;
                cargo.StopCurrentTask();
                cargo.status.Value = Troop.State.idle;
                continue;
            }

            // Vyhledání vhodné budovy v okolí jednotky.
            Building scannedBuilding = null;
            float scannedBuildingDistance = float.MaxValue;

            foreach (Building building in cargo.Scan<Building>(TroopScanType.Building))
            {
                if (building == null ||
                    building.OwnerClientId != group.OwnerClientId ||
                    building.Type != selectedBuildingType ||
                    (selectedBuildingType != Building.BuildingType.Core && building.MissingCorium <= 0))
                    continue;

                float distance = (building.transform.position - cargo.transform.position).sqrMagnitude;

                if (distance < scannedBuildingDistance)
                {
                    scannedBuilding = building;
                    scannedBuildingDistance = distance;
                }
            }

            // Zahájení ukládání nebo přesun k cílové budově.
            if (scannedBuilding != null)
            {
                cargo.IsDepositing.Value = true;
                cargo.Deposit(scannedBuilding);
            }
            else if (cargo.status.Value != Troop.State.moving || !cargo.IsMoving)
            {
                cargo.MoveTo(depositTarget.transform.position);
            }
        }
    }


    // Vyhledá nejbližší dostupnou surovinu podle zadané pozice.
    private Resource FindNearestResource(Vector3 from)
    {
        Resource best = null;
        float bestDistance = float.MaxValue;

        ResourceManager resourceManager =
            group != null && group.Owner != null ? group.Owner.ResourceManager : null;

        if (resourceManager == null) return null;

        foreach (Resource resource in resourceManager.Resources)
        {
            if (resource == null || resource.Depleted || resource.CoriumAmount.Value <= 0)
                continue;

            float distance = (resource.transform.position - from).sqrMagnitude;

            if (distance < bestDistance)
            {
                best = resource;
                bestDistance = distance;
            }
        }

        return best;
    }

    #endregion


    #region Síťová kontrola

    // Ověří, zda aktuální instance běží na serveru.
    private bool IsServer()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }

    #endregion
}
