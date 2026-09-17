using Unity.Netcode;
using UnityEngine;

public class LaserTower : Building
{
    public override BuildingType Type => BuildingType.LaserTower;

    [Header("Laser Tower specific")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private LineRenderer laserBeam;
    [SerializeField] private LayerMask enemyLayer;

    private NetworkVariable<ulong> currentTargetNetId = new NetworkVariable<ulong>(
        ulong.MaxValue, writePerm: NetworkVariableWritePermission.Server);

    private float attackTimer;

    public override void OnBuilt()
    {
        attackTimer = 0f;
    }

    public override void UpdateBuilding()
    {
        if (!IsOperational || !IsServer) return;

        // TODO: hledání cíle
        // 1) Physics.OverlapSphere(transform.position, AttackRange, enemyLayer)
        // 2) vyfiltrovat jen jednotky/budovy s jiným OwnerClientId než tahle věž
        // 3) vybrat nejbližší (nebo dle priority - jednotky před budovami)
        // 4) uložit NetworkObjectId cíle do currentTargetNetId.Value

        // TODO: pokud má platný cíl v dosahu:
        //   attackTimer += Time.deltaTime;
        //   if (attackTimer >= AttackSpeed) { attackTimer = 0f; Fire(target); }

        // TODO: pokud cíl zemřel/utekl z dosahu -> currentTargetNetId.Value = ulong.MaxValue, hledej znovu
    }

    // TODO: implementovat Fire(NetworkObject target)
    //   - server aplikuje damage: target.GetComponent<Building>().TakeDamage(Damage) nebo Troop obdoba
    //   - zavolat FireLaserClientRpc pro vizuál, NIKDY neaplikovat damage v ClientRpc

    [ClientRpc]
    private void FireLaserClientRpc(Vector3 targetPosition)
    {
        // TODO: krátce ukázat laserBeam mezi firePoint.position a targetPosition
        // (coroutine, co po X sekundách LineRenderer.enabled = false)
    }
}