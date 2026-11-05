using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(RocketAttacker))]
public class Medium : Troop, IGenericAttackerCallbacks
{
    [Header("Photon – útok")]
    [SerializeField] private string shootingAnimationBool = "isShooting";

    private GenericAttacker attacker;

    public override void OnSpawned()
    {
        attacker = GetComponent<GenericAttacker>();
    }

    public float GetAttackCooldown(float defaultCooldown) => AttackSpeed;

    public void OnAttackTargetChanged(Animator attackAnimator, IDamageable target)
    {
    }

    public void OnAttackAnimation(Animator attackAnimator, IDamageable target)
    {
        // Bool is controlled by the current target, not by the delayed shot effect RPC.
    }

}
