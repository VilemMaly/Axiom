using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Základní třída pro sběratelné suroviny na mapě (Corium a případně další
/// typy v budoucnu). Konkrétní chování těžby (kolik se ubere, co se stane
/// po vyčerpání, komu se surovina připíše) řeší potomci - viz Corium.
/// </summary>
public abstract class Resource : NetworkBehaviour
{
    public NetworkVariable<int> CoriumAmount = new(writePerm: NetworkVariableWritePermission.Server);
    public int SpawnAmount = 100; // výchozí množství coria při spawnování resource
    public bool Depleted => CoriumAmount.Value <= 0;

    /// <summary>
    /// Server-side metoda (NENÍ to Rpc) - volá ji server sám za sebe
    /// z Harvester.UpdateTroop, poté co si už dřív přes
    /// RequestHarvestTargetServerRpc ověřil, že daná jednotka smí tento
    /// resource těžit. Nejde o síťové volání, které by překračovalo hranici
    /// klient/server, takže žádnou RpcParams/ownership kontrolu tu řešit
    /// nemusíme - tu už udělal Harvester při přijetí cíle.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            CoriumAmount.Value = SpawnAmount; // Příklad: výchozí množství coria
        }
    }
    public virtual void DepleteResource(Harvester harvester, int amount)
    {
    }

    [ClientRpc]
    public virtual void DepleteAnimationClientRpc()
    {
        // Zde můžete přidat animaci vyčerpání resource, pokud je potřeba
    }
}