using Unity.Netcode;
using UnityEngine;

public class Corium : Resource
{
    public Material depletedMat;
    public Renderer visual;
    public override void DepleteResource(Harvester harvester, int amount)
    {
        if (!IsServer || Depleted || amount <= 0 || harvester == null)
            return;

        int actualAmount = Mathf.Min(amount, CoriumAmount.Value);
        if (actualAmount <= 0)
            return;

        CoriumAmount.Value -= actualAmount;
        harvester.AddToCargo(actualAmount);

        if (CoriumAmount.Value <= 0)
        {
            DepleteAnimationClientRpc();
        }
    }

    [ClientRpc]
    public override void DepleteAnimationClientRpc()
    {
        // Zde můžete přidat animaci vyčerpání resource, pokud je potřeba
        visual.material = depletedMat;
        Debug.Log("Resource Depleted");
    }
}