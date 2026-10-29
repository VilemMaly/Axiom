using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Optional callbacks implemented by the Building or Troop that owns this component.
/// These let each concrete type handle its own aiming and animation.
/// </summary>
public interface IGenericAttackerCallbacks
{
    float GetAttackCooldown(float defaultCooldown);
    void OnAttackTargetChanged(Animator attackAnimator, IDamageable target);
    void OnAttackAnimation(Animator attackAnimator, IDamageable target);
}

/// <summary>
/// Server-authoritative, reusable attack behaviour for a Building or Troop.
/// Add this component beside the owning Building/Troop.
/// </summary>
public sealed class GenericAttacker : NetworkBehaviour
{
    private static readonly int FireSpeedHash = Animator.StringToHash("fireSpeed");
    private static readonly int FireAnimationSpeedHash = Animator.StringToHash("fireAnimationSpeed");
    private static readonly int FireCooldownHash = Animator.StringToHash("fireCooldown");

    [Header("Attack")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private bool alternateFirePoints;
    [SerializeField] private Transform alternateFirePoint;
    [SerializeField] private ParticleSystem shotParticle;
    [SerializeField] private AudioClip fireSfx;
    [Tooltip("AudioSource whose spatial settings determine where the shot is heard in 3D.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Animator used for attack parameters and animation callbacks. If empty, one is searched among children.")]
    [SerializeField] private Animator animator;
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField, Min(0.02f)] private float scanInterval = 0.25f;
    [Tooltip("Optional cooldown override. Zero uses the Building/Troop attack stat.")]
    [SerializeField, Min(0f)] private float cooldownOverride;
    [Tooltip("Duration in seconds of one fire animation loop at multiplier 1. The animation multiplier is calculated so the loop matches the shot cooldown.")]
    [SerializeField, Min(0.02f)] private float fireAnimationLoopDuration = 1f;

    private readonly Collider[] results = new Collider[32];
    private Building sourceBuilding;
    private Troop sourceTroop;
    private IGenericAttackerCallbacks callbacks;
    private IDamageable lockedTarget;
    private Transform particleOriginalParent;
    private float nextScanTime;
    private float nextFireTime;
    private bool useAlternateFirePoint;

    public IDamageable CurrentTarget => lockedTarget;
    public Transform FirePoint => firePoint;
    public Animator AttackAnimator => animator;

    public bool TryAcquireTargetServer()
    {
        if (!IsServer || !SourceIsAlive())
            return false;

        ValidateOrAcquireTarget();
        if (lockedTarget == null)
            return false;

        nextFireTime = Time.time;
        return true;
    }

    public override void OnNetworkSpawn()
    {
        sourceBuilding = GetComponent<Building>();
        sourceTroop = GetComponent<Troop>();
        callbacks = GetComponent(typeof(IGenericAttackerCallbacks)) as IGenericAttackerCallbacks;
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (sourceBuilding == null && sourceTroop == null)
        {
            Debug.LogError($"{nameof(GenericAttacker)} on {name} requires a Building or Troop on the same object.", this);
            enabled = false;
            return;
        }

        if (shotParticle != null)
        {
            shotParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleOriginalParent = shotParticle.transform.parent;
            if (shotParticle.transform != transform && shotParticle.transform.IsChildOf(transform))
                shotParticle.transform.SetParent(null, true);

            ParticleSystem.MainModule particleMain = shotParticle.main;
            particleMain.simulationSpace = ParticleSystemSimulationSpace.World;
        }
        if(alternateFirePoints)
            fireAnimationLoopDuration = fireAnimationLoopDuration / 2;

        callbacks?.OnAttackTargetChanged(animator, null);
    }

    public override void OnNetworkDespawn()
    {
        SetTarget(null);

        if (shotParticle != null)
        {
            shotParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (particleOriginalParent != null)
                shotParticle.transform.SetParent(particleOriginalParent, true);
        }
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (IsServer && !SourceIsAlive())
        {
            SetTarget(null);
            return;
        }

        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
            if (SourceIsAlive())
                ValidateOrAcquireTarget();
            else
                SetTarget(null);
        }

        if (!IsServer)
            return;

        if (lockedTarget == null || Time.time < nextFireTime)
            return;

        float fireSpeed = GetCooldown();
        nextFireTime = Time.time + fireSpeed;
        FireAt(lockedTarget, fireSpeed);
    }

    private void ValidateOrAcquireTarget()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            GetRange(),
            results,
            targetLayers,
            QueryTriggerInteraction.Collide);

        IDamageable nearest = null;
        float nearestDistance = float.PositiveInfinity;
        bool currentTargetDetected = false;

        for (int i = 0; i < count; i++)
        {
            Collider hit = results[i];
            if (hit == null)
                continue;

            IDamageable damageable = hit.GetComponentInParent(typeof(IDamageable)) as IDamageable;
            if (damageable == null)
                continue;

            Component damageableComponent = damageable as Component;
            if (damageableComponent == null || !IsValidTarget(damageable))
                continue;

            if (damageable == lockedTarget)
                currentTargetDetected = true;

            float distance = (damageableComponent.transform.position - transform.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearest = damageable;
                nearestDistance = distance;
            }
        }

        if (nearest == null)
        {
            nextFireTime = 0f;
            SetTarget(null);
            return;
        }

        if (nearest != lockedTarget && !currentTargetDetected)
            nextFireTime = 0f;

        SetTarget(nearest);
    }

    private bool IsValidTarget(IDamageable candidate)
    {
        Component candidateComponent = candidate as Component;
        if (candidateComponent == null ||
            ReferenceEquals(candidate, sourceBuilding) ||
            ReferenceEquals(candidate, sourceTroop))
            return false;

        NetworkBehaviour candidateNetworkBehaviour = candidateComponent as NetworkBehaviour;
        if (candidateNetworkBehaviour != null &&
            (!candidateNetworkBehaviour.IsSpawned || candidateNetworkBehaviour.OwnerClientId == OwnerClientId))
            return false;

        if ((candidateComponent.transform.position - transform.position).sqrMagnitude > GetRange() * GetRange())
            return false;

        if (candidateComponent is Troop troop)
            return troop.OwnerClientId != OwnerClientId && troop.Health.Value > 0;

        if (candidateComponent is Building building)
        {
            return building.OwnerClientId != OwnerClientId &&
                   building.Health.Value > 0 &&
                   building.IsConstructionComplete.Value;
        }

        return false;
    }

    private bool SourceIsAlive()
    {
        if (sourceTroop != null)
            return sourceTroop.Health.Value > 0;

        return sourceBuilding != null &&
               sourceBuilding.IsOperational &&
               sourceBuilding.Health.Value > 0 &&
               sourceBuilding.IsConstructionComplete.Value;
    }

    private float GetRange()
    {
        return sourceBuilding != null ? sourceBuilding.AttackRange : sourceTroop.AttackRange;
    }

    private int GetDamage()
    {
        return sourceBuilding != null ? sourceBuilding.Damage : sourceTroop.Damage;
    }

    private float GetCooldown()
    {
        if (cooldownOverride > 0f)
            return cooldownOverride;

        float cooldown;
        if (sourceBuilding != null)
            cooldown = sourceBuilding.AttackCooldown > 0f ? 1/sourceBuilding.AttackCooldown : 1f;
        else
            cooldown = sourceTroop.AttackSpeed > 0f ? 1/sourceTroop.AttackSpeed : 1f;

        if (callbacks != null)
            cooldown = 1 / callbacks.GetAttackCooldown(cooldown);

        return Mathf.Max(0.02f, cooldown);
    }

    private void SetTarget(IDamageable target)
    {
        if (lockedTarget == target)
            return;

        lockedTarget = target;
        callbacks?.OnAttackTargetChanged(animator, target);
    }

    private void FireAt(IDamageable target, float fireSpeed)
    {
        if (!IsValidTarget(target))
        {
            SetTarget(null);
            return;
        }

        target.TakeDamage(GetDamage());

        Transform selectedFirePoint = GetNextFirePoint();
        Vector3 origin = selectedFirePoint != null ? selectedFirePoint.position : transform.position;
        PlayShotClientRpc(origin, fireSpeed);
    }

    private Transform GetNextFirePoint()
    {
        if (!alternateFirePoints || alternateFirePoint == null)
            return firePoint;

        Transform selected = useAlternateFirePoint ? alternateFirePoint : firePoint;
        useAlternateFirePoint = !useAlternateFirePoint;
        return selected;
    }

    [ClientRpc]
    private void PlayShotClientRpc(Vector3 origin, float fireSpeed)
    {
        SetFireAnimatorSpeed(fireSpeed);

        if (shotParticle != null)
        {
            shotParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Transform particleTransform = shotParticle.transform;
            Component targetComponent = lockedTarget as Component;
            Vector3 destination = targetComponent != null
                ? targetComponent.transform.position
                : origin + particleTransform.forward;
            Vector3 direction = destination - origin;
            Quaternion rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                : particleTransform.rotation;

            particleTransform.SetPositionAndRotation(origin, rotation);
            shotParticle.Play();
        }

        if (audioSource != null && fireSfx != null)
        {
            audioSource.PlayOneShot(fireSfx);
        }

        callbacks?.OnAttackAnimation(animator, lockedTarget);
    }

    private void SetFireAnimatorSpeed(float cooldown)
    {
        if (animator == null)
            return;

        // Match one animation loop (at its authored duration) to the time between shots.
        float safeCooldown = Mathf.Max(0.02f, cooldown);
        animator.SetFloat(FireCooldownHash, safeCooldown);
        animator.SetFloat(FireSpeedHash, safeCooldown);
        float safeAnimationDuration = Mathf.Max(0.02f, fireAnimationLoopDuration);
        animator.SetFloat(FireAnimationSpeedHash, safeAnimationDuration / safeCooldown);
    }

    private void OnDrawGizmosSelected()
    {
        float range = sourceBuilding != null
            ? sourceBuilding.AttackRange
            : sourceTroop != null ? sourceTroop.AttackRange : 0f;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
