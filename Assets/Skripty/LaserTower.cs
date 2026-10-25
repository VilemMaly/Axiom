using UnityEngine;

[RequireComponent(typeof(GenericAttacker))]
public class LaserTower : Building, IGenericAttackerCallbacks
{
    public override BuildingType Type => BuildingType.LaserTower;

    [Header("Laser Tower – cílení")]
    [SerializeField] private Transform turretPivot;
    [SerializeField] private float turretRotationSpeed = 180f;
    [SerializeField] private string shootingAnimationBool = "isShooting";

    private IDamageable currentTarget;

    public float GetAttackCooldown(float defaultCooldown) => defaultCooldown;

    public void OnAttackTargetChanged(Animator attackAnimator, IDamageable target)
    {
        currentTarget = target;

        if (attackAnimator != null && !string.IsNullOrWhiteSpace(shootingAnimationBool))
            attackAnimator.SetBool(shootingAnimationBool, target != null);
    }

    public void OnAttackAnimation(Animator attackAnimator, IDamageable target)
    {
        // Bool is controlled by the current target, not by the delayed shot effect RPC.
    }

    private void LateUpdate()
    {
        if (currentTarget == null || turretPivot == null)
            return;

        Component targetComponent = currentTarget as Component;
        if (targetComponent == null)
            return;

        Vector3 direction = targetComponent.transform.position - turretPivot.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        turretPivot.rotation = Quaternion.RotateTowards(
            turretPivot.rotation,
            targetRotation,
            turretRotationSpeed * Time.deltaTime);
    }
}
