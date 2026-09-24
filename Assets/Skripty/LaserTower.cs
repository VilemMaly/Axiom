using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class LaserTower : Building
{
    public override BuildingType Type => BuildingType.LaserTower;

    [Header("Laser Tower – cílení")]
    [Tooltip("Transform otočné části věže (turret/hlaveň), která se natáčí na cíl.")]
    [SerializeField] private Transform turretPivot;

    [Tooltip("Bod, odkud vychází laser paprsek (a particle efekt).")]
    [SerializeField] private Transform firePoint;

    [Tooltip("Layer maska pro detekci nepřátelských jednotek a budov.")]
    [SerializeField] private LayerMask enemyLayer;

    [Tooltip("Jak často (v sekundách) se skenuje okolí.")]
    [SerializeField] private float enemyScanInterval = 0.25f;

    [Tooltip("Rychlost rotace turetu (stupně/s). Vyšší = plynulejší zamíření.")]
    [SerializeField] private float turretRotationSpeed = 180f;

    [Header("Laser Tower – VFX/SFX")]
    [Tooltip("LineRenderer pro laser paprsek (child objekt pod firePoint).")]
    [SerializeField] private LineRenderer laserBeam;

    [Tooltip("Jak dlouho je laser paprsek viditelný po výstřelu (v sekundách).")]
    [SerializeField] private float laserDisplayDuration = 0.15f;

    [Tooltip("Particle system výstřelu (volitelné – stejný princip jako u Photonu).")]
    [SerializeField] private ParticleSystem shotParticle;

    [Tooltip("Zvuk výstřelu.")]
    [SerializeField] private AudioClip fireSfx;

    [Tooltip("Animator věže – pokud existuje, přehraje stav 'Fire' při výstřelu.")]
    [SerializeField] private Animator towerAnimator;

    [Tooltip("Jméno Animator triggeru pro výstřel.")]
    [SerializeField] private string fireTriggerName = "Fire";

    // AudioSource – věž ho nemá automaticky přes RequireComponent jako Troop,
    // přiřaď v inspektoru nebo přidej komponentu ručně na prefab.
    [SerializeField] private AudioSource audioSource;

    // Synced pro klienty – aby věděli na koho věž míří (pro rotaci turetu na klientech)
    public NetworkVariable<NetworkObjectReference> CurrentTarget =
        new(writePerm: NetworkVariableWritePermission.Server);

    private readonly Collider[] enemyResults = new Collider[32];
    private NetworkObject lockedTarget;
    private float nextEnemyScan;
    private float attackTimer;

    public override void OnBuilt()
    {
        attackTimer = 0f;

        if (laserBeam != null)
            laserBeam.enabled = false;
    }

    public override void UpdateBuilding()
    {
        // --- SERVER: hledání cíle a střelba ---
        if (IsServer)
        {
            if (Time.time >= nextEnemyScan)
            {
                nextEnemyScan = Time.time + enemyScanInterval;
                ValidateOrAcquireTarget();
            }

            if (lockedTarget != null)
            {
                attackTimer += Time.deltaTime;
                if (attackTimer >= AttackCooldown)
                {
                    attackTimer = 0f;
                    FireAt(lockedTarget);
                }
            }
        }

        // --- SERVER + KLIENTI: rotace turetu na cíl ---
        RotateTurretToTarget();
    }

    // ============================================================
    //  ROTACE TURETU (běží na serveru i na klientech)
    // ============================================================

    private void RotateTurretToTarget()
    {
        if (turretPivot == null) return;

        // Klienti čtou cíl z NetworkVariable
        NetworkObject target = lockedTarget;

        if (target == null && CurrentTarget.Value.TryGet(out NetworkObject netTarget))
            target = netTarget;

        if (target == null) return;

        Vector3 direction = target.transform.position - turretPivot.position;
        direction.y = 0f; // rotujeme jen horizontálně

        if (direction.sqrMagnitude < 0.001f) return;

        Quaternion desiredRotation = Quaternion.LookRotation(direction);
        turretPivot.rotation = Quaternion.RotateTowards(
            turretPivot.rotation,
            desiredRotation,
            turretRotationSpeed * Time.deltaTime);
    }

    // ============================================================
    //  VÝBĚR CÍLE (jen server)
    // ============================================================

    private void ValidateOrAcquireTarget()
    {
        // Ověřit stávající cíl
        if (lockedTarget != null && !IsValidEnemyTarget(lockedTarget))
        {
            lockedTarget = null;
            CurrentTarget.Value = default;
        }

        if (lockedTarget != null)
            return; // stále zamčeno na stejný cíl

        NetworkObject best = FindNearestEnemy();
        if (best == null) return;

        lockedTarget = best;
        CurrentTarget.Value = best;
        attackTimer = 0f; // reset cooldownu při novém cíli
    }

    private NetworkObject FindNearestEnemy()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            AttackRange,
            enemyResults,
            enemyLayer);

        NetworkObject nearest = null;
        float nearestDistSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (enemyResults[i] == null) continue;

            NetworkObject candidate = enemyResults[i].GetComponentInParent<NetworkObject>();
            if (candidate == null || !IsValidEnemyTarget(candidate)) continue;

            float distSqr = (candidate.transform.position - transform.position).sqrMagnitude;
            if (distSqr < nearestDistSqr)
            {
                nearestDistSqr = distSqr;
                nearest = candidate;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Cíl je platný, pokud existuje, je nepřátelský (jiný OwnerClientId)
    /// a je naživu a v dosahu.
    /// </summary>
    private bool IsValidEnemyTarget(NetworkObject netObj)
    {
        if (netObj == null) return false;

        float distSqr = (netObj.transform.position - transform.position).sqrMagnitude;
        if (distSqr > AttackRange * AttackRange) return false;

        if (netObj.TryGetComponent(out Troop troop))
            return troop.OwnerClientId != OwnerClientId && troop.Health.Value > 0;

        if (netObj.TryGetComponent(out Building building))
            return building.OwnerClientId != OwnerClientId && building.Health.Value > 0;

        return false;
    }

    // ============================================================
    //  STŘELBA (server aplikuje damage, klienti přehrají efekt)
    // ============================================================

    private void FireAt(NetworkObject target)
    {
        if (target == null) return;

        // Server-side damage
        if (target.TryGetComponent(out Troop enemyTroop))
            enemyTroop.TakeDamage(Damage);
        else if (target.TryGetComponent(out Building enemyBuilding))
            enemyBuilding.TakeDamage(Damage);

        // Vizuál/zvuk na všech klientech
        PlayFireEffectClientRpc(target.transform.position);
    }

    [ClientRpc]
    private void PlayFireEffectClientRpc(Vector3 targetPosition)
    {
        // --- Laser beam (LineRenderer) ---
        if (laserBeam != null && firePoint != null)
        {
            laserBeam.SetPosition(0, firePoint.position);
            laserBeam.SetPosition(1, targetPosition);
            laserBeam.enabled = true;
            StartCoroutine(HideLaserAfterDelay());
        }

        // --- Particle efekt (stejný princip jako Photon) ---
        if (shotParticle != null)
        {
            Vector3 direction = targetPosition - firePoint.position;
            if (direction.sqrMagnitude > 0.0001f)
                shotParticle.transform.rotation = Quaternion.LookRotation(direction.normalized);

            shotParticle.Play();
        }

        // --- Animator trigger ---
        if (towerAnimator != null && !string.IsNullOrEmpty(fireTriggerName))
            towerAnimator.SetTrigger(fireTriggerName);

        // --- Zvuk výstřelu ---
        if (audioSource != null && fireSfx != null)
            audioSource.PlayOneShot(fireSfx);
    }

    private IEnumerator HideLaserAfterDelay()
    {
        yield return new WaitForSeconds(laserDisplayDuration);

        if (laserBeam != null)
            laserBeam.enabled = false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, AttackRange);
    }
#endif
}