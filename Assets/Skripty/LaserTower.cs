using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class LaserTower : Building
{
    // Určuje typ této budovy.
    public override BuildingType Type => BuildingType.LaserTower;


    // ============================================================
    //  LASER TOWER – CÍLENÍ
    // ============================================================

    [Header("Laser Tower – cílení")]

    [Tooltip("Transform otočné části věže (turret/hlaveň), která se natáčí na cíl.")]
    [SerializeField] private Transform turretPivot;


    [Tooltip("Bod, odkud vychází laser paprsek (a particle efekt).")]
    [SerializeField] private Transform firePoint;


    [Tooltip("Layer maska pro detekci nepřátelských jednotek a budov.")]
    [SerializeField] private LayerMask enemyLayer;


    [Tooltip("Jak často (v sekundách) se skenuje okolí.")]
    [SerializeField] private float enemyScanInterval = 0.25f;


    [Tooltip("Rychlost rotace turetu (stupně/s).")]
    [SerializeField] private float turretRotationSpeed = 180f;


    [Tooltip("Jak často server posílá klientům nový úhel věže.")]
    [SerializeField] private float rotationSyncInterval = 0.05f;


    // ============================================================
    //  LASER TOWER – VFX / SFX
    // ============================================================

    [Header("Laser Tower – VFX/SFX")]

    [Tooltip("LineRenderer pro laser paprsek (child objekt pod firePoint).")]
    [SerializeField] private LineRenderer laserBeam;


    [Tooltip("Jak dlouho je laser paprsek viditelný po výstřelu.")]
    [SerializeField] private float laserDisplayDuration = 0.15f;


    [Tooltip("Particle system výstřelu.")]
    [SerializeField] private ParticleSystem shotParticle;


    [Tooltip("Zvuk výstřelu.")]
    [SerializeField] private AudioClip fireSfx;


    [Tooltip("Animator věže.")]
    [SerializeField] private Animator towerAnimator;


    [Tooltip("Jméno Animator triggeru pro výstřel.")]
    [SerializeField] private string fireTriggerName = "Fire";


    [SerializeField] private AudioSource audioSource;


    // ============================================================
    //  SERVEROVÝ TARGET
    // ============================================================

    /*
     * Skutečný target existuje pouze na serveru.
     *
     * Klientům ho vůbec neposíláme.
     */
    private NetworkObject lockedTarget;


    // ============================================================
    //  SYNCHRONIZOVANÝ ÚHEL
    // ============================================================

    /*
     * Poslední úhel, který server poslal klientům.
     *
     * Jde pouze o Y rotaci.
     */
    private float lastSyncedYaw;


    /*
     * Poslední přijatý úhel na klientovi.
     */
    private float targetYaw;


    /*
     * Určuje, jestli klient aktuálně dostává platný úhel
     * a má tedy věž otáčet.
     */
    private bool hasRotationTarget;


    /*
     * Čas, kdy se může znovu synchronizovat rotace.
     */
    private float nextRotationSync;


    // ============================================================
    //  INTERNÍ PROMĚNNÉ
    // ============================================================

    private readonly Collider[] enemyResults = new Collider[32];


    private float nextEnemyScan;


    private float attackTimer;


    // ============================================================
    //  POSTAVENÍ VĚŽE
    // ============================================================

    public override void OnBuilt()
    {
        /*
         * Při postavení začneme s nulovým časem útoku.
         */
        attackTimer = 0f;


        /*
         * Resetujeme synchronizaci rotace.
         */
        nextRotationSync = 0f;
        lastSyncedYaw = 0f;
        targetYaw = 0f;
        hasRotationTarget = false;


        /*
         * Laser je na začátku vypnutý.
         */
        if (laserBeam != null)
            laserBeam.enabled = false;


        /*
         * Server začíná bez targetu.
         */
        if (IsServer)
            lockedTarget = null;
    }


    // ============================================================
    //  HLAVNÍ LOGIKA VĚŽE
    // ============================================================

    public override void UpdateBuilding()
    {
        // --------------------------------------------------------
        // SERVER
        // --------------------------------------------------------

        if (IsServer)
        {
            /*
             * Je čas znovu prohledat okolí?
             */
            if (Time.time >= nextEnemyScan)
            {
                nextEnemyScan = Time.time + enemyScanInterval;

                ValidateOrAcquireTarget();
            }


            /*
             * Pokud máme target,
             * server počítá jeho aktuální úhel.
             */
            if (lockedTarget != null)
            {
                attackTimer += Time.deltaTime;


                /*
                 * Server si věž otáčí lokálně.
                 */
                float desiredYaw = CalculateTargetYaw(lockedTarget);

                ApplyTurretRotation(desiredYaw);


                /*
                 * Pravidelně pošleme stejný úhel i klientům.
                 */

                SyncTurretYaw(desiredYaw);
                


                /*
                 * Výstřel.
                 */
                if (attackTimer >= AttackCooldown)
                {
                    attackTimer = 0f;

                    FireAt(lockedTarget);
                }
            }
        }

        // --------------------------------------------------------
        // KLIENT
        // --------------------------------------------------------

        else
        {
            /*
             * Klient nemá target.
             *
             * Má pouze poslední úhel, který mu poslal server.
             */
            if (hasRotationTarget)
            {
                ApplyTurretRotation(targetYaw);
            }
        }
    }


    // ============================================================
    //  VÝPOČET ÚHLU NA TARGET
    // ============================================================

    private float CalculateTargetYaw(NetworkObject target)
    {
        /*
         * Bez targetu nebo turretPivotu nemáme co počítat.
         */
        if (target == null || turretPivot == null)
            return turretPivot != null
                ? turretPivot.eulerAngles.y
                : 0f;


        /*
         * Směr od věže k targetu.
         */
        Vector3 direction =
            target.transform.position - turretPivot.position;


        /*
         * Zabráníme naklánění nahoru/dolů.
         */
        direction.y = 0f;


        /*
         * Když je target prakticky přesně uprostřed,
         * necháme současný úhel.
         */
        if (direction.sqrMagnitude < 0.001f)
            return turretPivot.eulerAngles.y;


        /*
         * Spočítáme požadovanou rotaci.
         */
        Quaternion desiredRotation =
            Quaternion.LookRotation(direction);


        /*
         * Vrátíme pouze Y úhel.
         */
        return desiredRotation.eulerAngles.y;
    }


    // ============================================================
    //  APLIKACE ÚHLU
    // ============================================================

    private void ApplyTurretRotation(float desiredYaw)
    {
        if (turretPivot == null)
            return;


        /*
         * Použijeme pouze Y rotaci.
         *
         * X a Z zůstávají nulové stejně jako u původního systému.
         */
        Quaternion desiredRotation =
            Quaternion.Euler(0f, desiredYaw, 0f);


        /*
         * Plynulé otočení věže.
         */
        turretPivot.rotation = Quaternion.RotateTowards(
            turretPivot.rotation,
            desiredRotation,
            turretRotationSpeed * Time.deltaTime
        );
    }


    // ============================================================
    //  SYNCHRONIZACE ÚHLU
    // ============================================================

    private void SyncTurretYaw(float yaw)
    {
        /*
         * Server si úhel nastaví okamžitě lokálně.
         *
         * To je důležité hlavně pro případ,
         * kdy server běží jako host.
         */
        lastSyncedYaw = yaw;

        targetYaw = yaw;
        hasRotationTarget = true;


        /*
         * Pošleme pouze číslo představující Y úhel.
         *
         * Žádný target, NetworkObjectReference ani Transform.
         */
        SetTurretRotationClientRpc(turretPivot.rotation);
    }


    // ============================================================
    //  RPC – NASTAVENÍ ÚHLU
    // ============================================================

    [ClientRpc]
    private void SetTurretRotationClientRpc(Quaternion rotation)
    {
        /*
         * Klient dostal od serveru pouze úhel.
         */


        /*
         * Od této chvíle má klient věž otáčet.
         */
        hasRotationTarget = true;
        Debug.Log($"Dostal jsem rotaci {NetworkManager.Singleton.LocalClientId}");
        turretPivot.rotation = rotation;

    }


    // ============================================================
    //  RPC – CLEAR ROTACE
    // ============================================================

    [ClientRpc]
    private void ClearRotationClientRpc()
    {
        /*
         * Server říká:
         *
         * "Věž už nemá target."
         *
         * Klient tedy přestane věž aktivně natáčet.
         */
        hasRotationTarget = false;
    }


    // ============================================================
    //  HLEDÁNÍ / OVĚŘOVÁNÍ CÍLE
    // ============================================================

    private void ValidateOrAcquireTarget()
    {
        /*
         * Pokud máme target, nejdříve ověříme jeho platnost.
         */
        if (lockedTarget != null &&
            !IsValidEnemyTarget(lockedTarget))
        {
            /*
             * Target už není platný.
             */
            lockedTarget = null;


            /*
             * Zastavíme útok.
             */
            attackTimer = 0f;


            /*
             * Server přestane lokálně řídit rotaci.
             */
            hasRotationTarget = false;


            /*
             * Řekneme všem klientům:
             *
             * "Clear."
             */
            ClearRotationClientRpc();


            /*
             * Nemáme target, takže v tomto průchodu
             * už nic dalšího neděláme.
             */
            return;
        }


        /*
         * Pokud už target máme,
         * nový nehledáme.
         */
        if (lockedTarget != null)
            return;


        /*
         * Najdeme nejbližšího nepřítele.
         */
        NetworkObject best = FindNearestEnemy();


        /*
         * Nic jsme nenašli.
         */
        if (best == null)
            return;


        /*
         * Nastavíme target pouze na serveru.
         */
        lockedTarget = best;


        /*
         * Resetujeme attack cooldown.
         */
        attackTimer = 0f;


        /*
         * Hned při získání targetu
         * vypočítáme jeho aktuální úhel.
         */
        float desiredYaw = CalculateTargetYaw(lockedTarget);


        /*
         * Hned ho pošleme klientům.
         */
        SyncTurretYaw(desiredYaw);
    }


    // ============================================================
    //  HLEDÁNÍ NEJBLIŽŠÍHO NEPŘÍTELE
    // ============================================================

    private NetworkObject FindNearestEnemy()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            AttackRange,
            enemyResults,
            enemyLayer
        );


        NetworkObject nearest = null;


        float nearestDistSqr = float.MaxValue;


        for (int i = 0; i < count; i++)
        {
            if (enemyResults[i] == null)
                continue;


            NetworkObject candidate =
                enemyResults[i].GetComponentInParent<NetworkObject>();


            if (candidate == null)
                continue;


            if (!IsValidEnemyTarget(candidate))
                continue;


            float distSqr =
                (candidate.transform.position - transform.position)
                .sqrMagnitude;


            if (distSqr < nearestDistSqr)
            {
                nearestDistSqr = distSqr;
                nearest = candidate;
            }
        }


        return nearest;
    }


    // ============================================================
    //  KONTROLA, JESTLI JE OBJEKT PLATNÝ NEPŘÍTEL
    // ============================================================

    private bool IsValidEnemyTarget(NetworkObject netObj)
    {
        if (netObj == null)
            return false;


        float distSqr =
            (netObj.transform.position - transform.position)
            .sqrMagnitude;


        if (distSqr > AttackRange * AttackRange)
            return false;


        // --------------------------------------------------------
        // TROOP
        // --------------------------------------------------------

        if (netObj.TryGetComponent(out Troop troop))
        {
            return troop.OwnerClientId != OwnerClientId
                   && troop.Health.Value > 0;
        }


        // --------------------------------------------------------
        // BUILDING
        // --------------------------------------------------------

        if (netObj.TryGetComponent(out Building building))
        {
            return building.OwnerClientId != OwnerClientId
                   && building.Health.Value > 0;
        }


        return false;
    }


    // ============================================================
    //  VÝSTŘEL
    // ============================================================

    private void FireAt(NetworkObject target)
    {
        if (target == null)
            return;


        // --------------------------------------------------------
        // SERVEROVÝ DAMAGE
        // --------------------------------------------------------

        if (target.TryGetComponent(out Troop enemyTroop))
        {
            enemyTroop.TakeDamage(Damage);
        }
        else if (target.TryGetComponent(out Building enemyBuilding))
        {
            enemyBuilding.TakeDamage(Damage);
        }


        // --------------------------------------------------------
        // VIZUÁLNÍ EFEKT
        // --------------------------------------------------------

        PlayFireEffectClientRpc(target.transform.position);
    }


    // ============================================================
    //  VIZUÁLNÍ EFEKT VÝSTŘELU
    // ============================================================

    [ClientRpc]
    private void PlayFireEffectClientRpc(Vector3 targetPosition)
    {
        
    }


    // ============================================================
    //  SKRYTÍ LASERU PO VÝSTŘELU
    // ============================================================

    private IEnumerator HideLaserAfterDelay()
    {
        yield return new WaitForSeconds(laserDisplayDuration);


        if (laserBeam != null)
            laserBeam.enabled = false;
    }


    // ============================================================
    //  GIZMO PRO UNITY EDITOR
    // ============================================================

#if UNITY_EDITOR

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;

        Gizmos.DrawWireSphere(
            transform.position,
            AttackRange
        );
    }

#endif
}