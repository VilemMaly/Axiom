using Unity.Netcode;
using UnityEngine;

public class Scrap : Resource
{   
    public GameObject visual;
    public ResourceManager resourceManager;
    public override void DepleteResource(Troop harvester, int amount)
    {
        if (!IsServer || Depleted || amount <= 0 || harvester is not Cargo cargo)
            return;

        int actualAmount = Mathf.Min(amount, CoriumAmount.Value);
        if (actualAmount <= 0)
            return;

        CoriumAmount.Value -= actualAmount;
        cargo.AddToCargo(actualAmount);

        if (CoriumAmount.Value <= 0)
        {
            DepleteAnimationClientRpc();
            Destroy(gameObject);
        }
    }

    public void setCoriumAmount(int coriumAmount)
    {
        if(!IsServer)
            return;
        CoriumAmount.Value = coriumAmount;
    }
    public override void OnNetworkSpawn()
    {
        Debug.Log($"[Scrap] Scrap spawn s {CoriumAmount.Value} hodnotou");
    }

    [ClientRpc]
    public override void DepleteAnimationClientRpc()
    {
        resourceManager.UnRegisterScrap(this);
        Destroy(gameObject);
    }
}
