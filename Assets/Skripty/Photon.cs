using Unity.Netcode;
using UnityEngine;

public class Photon : Troop
{
    [Header("Photon - laserový útok")]
    [SerializeField] private LayerMask enemyLayerMask;
    [SerializeField] private float enemyScanInterval = 0.25f;
    [SerializeField] private float fireInterval = 1f; // sekundy mezi zásahy

    [Header("Photon - VFX/SFX výstřelu")]
    [Tooltip("Particle system výstřelu (child prefab pod troopem) - natočí se směrem na cíl a přehraje.")]
    [SerializeField] private ParticleSystem shotParticle;
    [Tooltip("Zvuk výstřelu - přehraje se přes sdílený audioSource z Troop.")]
    [SerializeField] private AudioClip fireSfx;

    private readonly Collider[] enemyResults = new Collider[32];
    private float nextEnemyScan;
    private float nextFireTime;
    public int Damage;
    public float AttackRange;
    public float AttackSpeed;

    // Synced pro klienty - aby si mohli přehrát laser VFX na správný cíl
    public NetworkVariable<NetworkObjectReference> CurrentTarget =
        new(writePerm: NetworkVariableWritePermission.Server);

    private NetworkObject lockedTarget;

    public override void OnSpawned()
    {
        // zatím nic navíc, ale zachováváme override kvůli konzistenci s Atlasem
    }

    public override void UpdateTroop()
    {
        if (!IsServer)
            return;

        if (Time.time >= nextEnemyScan)
        {
            nextEnemyScan = Time.time + enemyScanInterval;
            ValidateOrAcquireTarget();
        }

        if (lockedTarget == null)
            return;

        if (Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireInterval;
            FireAt(lockedTarget);
        }
    }

    // --- Výběr cíle: drží se stávajícího, dokud je platný ---
    private void ValidateOrAcquireTarget()
    {
        if (lockedTarget != null && !IsValidEnemyTarget(lockedTarget))
        {
            lockedTarget = null;
            CurrentTarget.Value = default;
        }

        if (lockedTarget != null)
            return; // stále zamčeno na stejný cíl, nic dalšího neřešíme

        NetworkObject best = FindNearestEnemy();

        if (best == null)
            return;

        lockedTarget = best;
        CurrentTarget.Value = best; // implicitní konverze NetworkObject -> NetworkObjectReference
        Debug.Log($"[Apollo] {name} se zamkl na cíl {best.name}.");
    }

    private NetworkObject FindNearestEnemy()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            AttackRange,
            enemyResults,
            enemyLayerMask);

        NetworkObject nearest = null;
        float nearestDistSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (enemyResults[i] == null)
                continue;

            NetworkObject candidate = enemyResults[i].GetComponentInParent<NetworkObject>();

            if (candidate == null || !IsValidEnemyTarget(candidate))
                continue;

            float distSqr = (candidate.transform.position - transform.position).sqrMagnitude;

            if (distSqr < nearestDistSqr)
            {
                nearestDistSqr = distSqr;
                nearest = candidate;
            }
        }

        return nearest;
    }

    // Cíl je platný, pokud existuje, je nepřátelský (jiný OwnerClientId) a je naživu
    private bool IsValidEnemyTarget(NetworkObject netObj)
    {
        if (netObj == null)
            return false;

        float distSqr = (netObj.transform.position - transform.position).sqrMagnitude;
        if (distSqr > AttackRange * AttackRange)
            return false;

        if (netObj.TryGetComponent(out Troop troop))
        {
            return troop.OwnerClientId != OwnerClientId && troop.Health.Value > 0;
        }

        if (netObj.TryGetComponent(out Building building))
        {
            return building.OwnerClientId != OwnerClientId && building.Health.Value > 0;
        }

        return false;
    }

    // --- Samotný zásah - VŽDY jen jeden cíl, žádný splash/AoE ---
    private void FireAt(NetworkObject target)
    {
        if (target == null)
            return;

        if (target.TryGetComponent(out Troop enemyTroop))
        {
            enemyTroop.TakeDamage(Damage);
        }
        else if (target.TryGetComponent(out Building enemyBuilding))
        {
            enemyBuilding.TakeDamage(Damage);
        }

        // Klientům pošleme jen pozici cíle v okamžiku výstřelu (ne referenci) - k přehrání
        // particle/sfx efektu stačí, a nevadí, že cíl se mezitím může pohnout nebo zemřít.
        PlayFireEffectClientRpc(target.transform.position);
    }

    [ClientRpc]
    private void PlayFireEffectClientRpc(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;

        if (shotParticle != null)
        {
            if (direction.sqrMagnitude > 0.0001f)
                shotParticle.transform.rotation = Quaternion.LookRotation(direction.normalized);

            shotParticle.Play();
        }

        if (audioSource != null && fireSfx != null)
            audioSource.PlayOneShot(fireSfx);
    }

    // Volitelný manuální příkaz hráče (attack-move / force target) - stejný bezpečnostní vzor jako u Atlase
    [ServerRpc]
    public void RequestForceAttackServerRpc(
        NetworkObjectReference targetReference,
        ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!targetReference.TryGet(out NetworkObject netObj))
            return;

        if (!IsValidEnemyTarget(netObj))
            return;

        lockedTarget = netObj;
        CurrentTarget.Value = netObj;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, AttackRange);
    }
#endif
}