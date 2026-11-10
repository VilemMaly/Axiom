using System;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class Core : Building
{

    public override BuildingType Type => BuildingType.Core;
    private Animator animace;
    public event Action<ulong> coreNotFunctional;

    public override void OnBuilt()
    {
        // OnBuilt se volá z OnNetworkSpawn, takže IsOperational už je true
        // a OwnerResources už je nastavené (na serveru)
        animace = this.GameObject().GetComponent<Animator>();
    }

    public void CoriumReserve(int amount, Troop transporter)
    {
        if (transporter is not Cargo)
            return;
        OwnerResources.Add(amount, 0); // pouze server volá tuhle funkci aby hráč nemohl podvádět
        OpenAndCloseClientRpc();
    }

    public override void onDestroy()
    {
        coreNotFunctional?.Invoke(OwnerClientId);
    }

    [ClientRpc]
    public void OpenAndCloseClientRpc()
    {
        animace.SetTrigger("OpenAndClose");
    }

    public override void UpdateBuilding()
    {
        // Trojitá pojistka:
        // 1) IsOperational - nastaví se JEN v OnNetworkSpawn, tedy JEN po Spawnu
        // 2) IsServer - těžba se počítá jen na serveru
        // 3) OwnerResources != null - pro jistotu, i kdyby se OnBuilt nějak minul
        if (!IsOperational)
            coreNotFunctional?.Invoke(OwnerClientId);
        if (!IsOperational || !IsServer || OwnerResources == null) return;

    }
    // budoucí animace při spawnu, jak padá z nebe a udělá v zemi díru
}
