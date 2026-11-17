using System.Collections.Generic;
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
public class GenericAttacker : NetworkBehaviour
{
    private static readonly int FireSpeedHash = Animator.StringToHash("fireSpeed");
    private static readonly int FireAnimationSpeedHash = Animator.StringToHash("fireAnimationSpeed");
    private static readonly int FireCooldownHash = Animator.StringToHash("fireCooldown");

    [Header("Attack")]
    [SerializeField] private DamageType damageType = DamageType.BigAutomaticGun;
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
    private IDamageable commandedTarget;

    public IDamageable CurrentTarget => lockedTarget;
    public Transform FirePoint => firePoint;
    public Animator AttackAnimator => animator;

    public bool TryAcquireTargetServer()
    {
        if (!IsServer || sourceTroop != null || !SourceIsAlive())
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
            if (sourceTroop != null && commandedTarget == null)
                SetTarget(null);
            else if (SourceIsAlive())
                ValidateOrAcquireTarget();
            else
                SetTarget(null);
        }

        if (!IsServer)
            return;

        if (lockedTarget == null || Time.time < nextFireTime)
            return;

        // troop ještě není u cíle, jen jde dál, cíl nerušit
        if (!IsValidTarget(lockedTarget))
            return;

        float fireSpeed = GetCooldown();
        nextFireTime = Time.time + fireSpeed;
        if(sourceTroop != null)
        {
            Debug.Log($"[Generic Attacker] fireAt {lockedTarget} from {sourceTroop.Type}");
        }
        else if (sourceBuilding != null)
        {
            Debug.Log($"[Generic Attacker] fireAt {lockedTarget} from {sourceBuilding.Type}");
        }
        
        FireAt(lockedTarget, fireSpeed);
    }

        public virtual List<Troop> BuildingScanTroops()
        {
        var found = new List<Troop>();
        if (!IsServer || sourceBuilding.SightRange <= 0f) return found;
        Collider[] hits = Physics.OverlapSphere(transform.position, sourceBuilding.SightRange, targetLayers, QueryTriggerInteraction.Collide);
        foreach (Collider hit in hits)
        {
            if (hit == null) continue;
            Troop foundTroop = hit.GetComponentInParent<Troop>();

            if (foundTroop != null && foundTroop.OwnerClientId != OwnerClientId)
            {
                if(foundTroop.IsAirUnit)
                {
                    if(sourceBuilding.CanAttackAir == true)
                    {
                        found.Add(foundTroop);
                        continue;
                    }
                }
                found.Add(foundTroop);
                
            }
        }
        return found;
    }

    /// <summary>Sets a troop's combat target. Troops never acquire targets autonomously.</summary>
    public bool SetCommandedTargetServer(IDamageable target)
    {
        if (!IsServer || sourceTroop == null || !SourceIsAlive() || !IsValidTarget(target, false))
            return false;
        Debug.Log($"[GenericAttacker] nastavuju cíl na {target}");
        commandedTarget = target;
        SetTarget(target);
        nextFireTime = Time.time;
        return true;
    }

    public void ClearCommandedTargetServer()
    {
        if (!IsServer || sourceTroop == null) return;
        commandedTarget = null;
        SetTarget(null);
    }

    private void ValidateOrAcquireTarget()
    {
        if (sourceTroop != null)
        {
            if (commandedTarget == null || !IsValidTarget(commandedTarget, false))
            {
                commandedTarget = null;
                SetTarget(null);
                return;
            }

            SetTarget(commandedTarget);
            UpdateTroopTargetMovement(commandedTarget);
            return;
        }

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

    protected bool IsValidTarget(IDamageable candidate, bool requireInRange = true)
    {
        Component candidateComponent = candidate as Component;
        if (candidateComponent == null ||
            ReferenceEquals(candidate, sourceBuilding) ||
            ReferenceEquals(candidate, sourceTroop))
            {
                Debug.Log("[Generic Attacker] candidate není validní protože je null nebo je tato budova nebo troop");
                return false;
                
            }
            

        NetworkBehaviour candidateNetworkBehaviour = candidateComponent as NetworkBehaviour;
        if (candidateNetworkBehaviour != null &&
            (!candidateNetworkBehaviour.IsSpawned || candidateNetworkBehaviour.OwnerClientId == OwnerClientId))
            {
                Debug.Log("[Generic Attacker] candidate není validní protože není spawned nebo se snaží střelit na vlastní jednotku hráče");
                return false;
                }

        if (requireInRange && (candidateComponent.transform.position - transform.position).sqrMagnitude > GetRange() * GetRange())
        {
            Debug.Log("[Generic Attacker] candidate není validní protože je moc daleko");
            return false;
        }
            

        if (candidateComponent is Troop troop)
            return troop.OwnerClientId != OwnerClientId &&
                   troop.Health.Value > 0 &&
                   (!troop.IsAirUnit || CanAttackAir());

        if (candidateComponent is Building building)
        {
            return building.OwnerClientId != OwnerClientId &&
                   building.Health.Value > 0 &&
                   building.IsConstructionComplete.Value;
        }

        Debug.Log("[Generic Attacker] candidate není validní protože nesplnil podmínky pro validaci");
        return false;
    }

    private bool CanAttackAir()
    {
        return sourceTroop != null ? sourceTroop.CanAttackAir : sourceBuilding != null && sourceBuilding.CanAttackAir;
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

    protected int GetDamage()
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

    protected void SetTarget(IDamageable target)
    {
        if (lockedTarget == target)
            return;
        Debug.Log($"[GenericAttacker] {name} setting target to {target?.ToString() ?? "null"}");
        lockedTarget = target;
        if(target is Troop targetTroop)
        {
            if (targetTroop.Group == null)
            {
                Debug.Log($"[GenericAttacker] {targetTroop.Type} nemá validní nebo žádnou skupinu");
                return;
            }
            if(sourceTroop != null)
            {
                Debug.Log($"[GenericAttacker] Troop Mark attacked");
                targetTroop.Group.MarkAttacked(sourceTroop.Group);
            }
            else if(sourceBuilding != null)
            {
                Debug.Log($"[GenericAttacker] Building Mark attacked");
                targetTroop.Group.MarkAttacked(sourceBuilding);
            }
            
        }
        if(sourceTroop != null)
        {
            UpdateTroopTargetMovement(target);
        }
            callbacks?.OnAttackTargetChanged(animator, target);
        
        if (IsServer)
            SetAttackTargetClientRpc(target != null);
    }

    private void UpdateTroopTargetMovement(IDamageable target)
    {
        if (sourceTroop == null)
            {
                Debug.Log("[Generic Attacker] Source troop neexistuje");
                return;
            }
            
        Debug.Log($"[GenericAttacker] Updating troop {sourceTroop.name} movement for target {target?.ToString() ?? "null"}");
        if(target == null)
            return;
        if (target is Troop targetTroop)
        {
            
            float range = GetRange();
            if (Vector3.Distance(sourceTroop.AgentDestination, targetTroop.transform.position) > 1)
                return;   // jednotka se nepohnula, není potřeba měnit cílovou pozici pohybu
            Debug.Log("[Generic Attacker] Target je troop, pohyb k němu");
            sourceTroop.MoveTo(targetTroop.transform.position);
        }
        if (target is Building targetBuilding)
        {
            
            float range = GetRange();
            if (Vector3.Distance(sourceTroop.AgentDestination, targetBuilding.transform.position) > 1)
                return;   // jednotka se nepohnula, není potřeba měnit cílovou pozici pohybu
            Debug.Log("[Generic Attacker] Target je budova, pohyb k ní");
            sourceTroop.MoveTo(targetBuilding.transform.position);
        }
        /*
        Component targetComponent = target as Component;
        bool hasValidTarget = targetComponent != null && IsValidTarget(target, false);
        bool inRange = hasValidTarget &&
                       (targetComponent.transform.position - transform.position).sqrMagnitude <= GetRange() * GetRange();

        if(!hasValidTarget)
        {
            Debug.Log($"[Generic Attacker] Jednotka {sourceTroop.Type} nemá valid target na pohyb k němu");
        }
        SetTroopMovementPaused(inRange);
        if (hasValidTarget && !inRange && IsServer)
            sourceTroop.MoveTo(targetComponent.transform.position);*/
    }

    [ClientRpc]
    private void SetAttackTargetClientRpc(bool hasTarget)
    {
        if (IsServer)
            return;

        // The server's locked target is not replicated as an object reference. The
        // current callbacks only need to know whether a target exists to drive the
        // shooting bool, so pass the attacker itself as a non-null sentinel.
        IDamageable targetState = hasTarget
            ? (IDamageable)sourceTroop ?? sourceBuilding
            : null;
        callbacks?.OnAttackTargetChanged(animator, targetState);
    }

    private void SetTroopMovementPaused(bool paused)
    {
        if (sourceTroop == null)
            return;

        sourceTroop.SetCombatMovementPaused(paused);
        if (IsServer)
            SetTroopMovementPausedClientRpc(paused);
    }

    [ClientRpc]
    private void SetTroopMovementPausedClientRpc(bool paused)
    {
        if (IsServer || sourceTroop == null)
            return;

        sourceTroop.SetCombatMovementPaused(paused);
    }

    protected virtual void FireAt(IDamageable target, float fireSpeed)
    {
        if (!IsValidTarget(target))
        {
            SetTarget(null);
            return;
        }

        if (target is Troop troopTarget)
            troopTarget.TakeDamage(GetDamage(), damageType);
        else
            target.TakeDamage(GetDamage());

        PlayAttackEffect(target, fireSpeed);
    }

    protected void PlayAttackEffect(IDamageable target, float fireSpeed)
    {

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
