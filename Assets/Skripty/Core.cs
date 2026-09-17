using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class Core : Building
{
    [SerializeField] private int coriumPerTick = 5;
    [SerializeField] private float tickInterval = 1f;

    public override BuildingType Type => BuildingType.Core;
    private Animator animace;

    private float timer;

    public override void OnBuilt()
    {
        // OnBuilt se volá z OnNetworkSpawn, takže IsOperational už je true
        // a OwnerResources už je nastavené (na serveru)
        animace = this.GameObject().GetComponent<Animator>();
        timer = 0f;
    }

    public void CoriumReserve(int amount, Troop transporter)
    {
        Harvester harvester = transporter as Harvester;
        if(harvester == null)
            return;
        OwnerResources.Add(amount, 0); // pouze server volá tuhle funkci aby hráč nemohl podvádět
        OpenAndCloseClientRpc();
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
        if (!IsOperational || !IsServer || OwnerResources == null) return;

        timer += Time.deltaTime;
        if (timer >= tickInterval)
        {
            timer -= tickInterval;
            OwnerResources.TrySpend(CoriumConsumption, EnergyConsumption); // odečti corium (negativní náklad)
        }
    }
}