using UnityEngine;

[RequireComponent(typeof(Boom))]
public class RocketAttacker : GenericAttacker
{
    private Rocket rocket;
    private Boom boom;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        rocket = GetComponent<Rocket>();
        boom = GetComponent<Boom>();
    }

    protected override void FireAt(IDamageable target, float fireSpeed)
    {
        if (!IsValidTarget(target))
            return;

        if (boom == null || rocket == null)
        {
            Debug.LogError($"{nameof(RocketAttacker)} on {name} requires Rocket and Boom components on the same object.", this);
            return;
        }

        Component targetComponent = target as Component;
        boom.maxDamage = rocket.Damage;
        boom.setExplosionPosition(targetComponent.transform);
        boom.Explode(gameObject, OwnerClientId);

        PlayAttackEffect(target, fireSpeed);
    }
}
