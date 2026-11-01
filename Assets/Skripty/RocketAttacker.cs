using UnityEngine;

[RequireComponent(typeof(Boom))]
public class RocketAttacker : GenericAttacker
{
    private Troop rocketTroop;
    private Boom boom;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        rocketTroop = GetComponent<Troop>();
        boom = GetComponent<Boom>();
    }

    protected override void FireAt(IDamageable target, float fireSpeed)
    {
        if (!IsValidTarget(target))
            return;

        if (boom == null)
        {
            Debug.LogError($"{nameof(RocketAttacker)} on {name} requires Boom component on the same object.", this);
            return;
        }

        Component targetComponent = target as Component;
        boom.maxDamage = rocketTroop.Damage;
        boom.setExplosionPosition(targetComponent.transform);
        boom.Explode(gameObject, OwnerClientId);

        PlayAttackEffect(target, fireSpeed);
    }
}
