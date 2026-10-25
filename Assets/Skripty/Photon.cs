using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(GenericAttacker))]
public class Photon : Troop, IGenericAttackerCallbacks
{
    [Header("Photon – útok")]
    [SerializeField] private string shootingAnimationBool = "isShooting";
    [SerializeField, Min(0.02f)] private float fireInterval = 1f;

    private GenericAttacker attacker;

    public override void OnSpawned()
    {
        attacker = GetComponent<GenericAttacker>();
    }

    public float GetAttackCooldown(float defaultCooldown) => fireInterval;

    public void OnAttackTargetChanged(Animator attackAnimator, IDamageable target)
    {
        if (attackAnimator != null && !string.IsNullOrWhiteSpace(shootingAnimationBool))
            attackAnimator.SetBool(shootingAnimationBool, target != null);
    }

    public void OnAttackAnimation(Animator attackAnimator, IDamageable target)
    {
        // Bool is controlled by the current target, not by the delayed shot effect RPC.
    }

    [ServerRpc]
    public void RequestForceAttackServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || attacker == null)
            return;

        attacker.TryAcquireTargetServer();
    }
}
