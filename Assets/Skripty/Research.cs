using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class Research : Building
{
    public override BuildingType Type => BuildingType.Research;

    [SerializeField] private List<ResearchData> availableResearch;

    private NetworkVariable<int> currentResearchId = new NetworkVariable<int>(
        -1, writePerm: NetworkVariableWritePermission.Server);
    private NetworkVariable<float> researchProgress = new NetworkVariable<float>(
        0f, writePerm: NetworkVariableWritePermission.Server);

    public override void UpdateBuilding()
    {
        if (!IsOperational || !IsServer) return;
        if (currentResearchId.Value < 0) return;

        // TODO: researchProgress.Value += Time.deltaTime
        // po dosažení data.duration:
        //   - aplikovat efekt (např. přes PlayerUpgrades skript na PlayerObjectu - zatím neexistuje)
        //   - currentResearchId.Value = -1
        //   - ClientRpc notifikace "výzkum dokončen" pro UI
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestStartResearchServerRpc(int researchId, ServerRpcParams rpcParams = default)
    {
        if (currentResearchId.Value >= 0) return; // už se něco zkoumá

        var data = availableResearch.Find(r => r.id == researchId);
        if (data == null) return;

        if (!OwnerResources.TrySpend(data.coriumCost, data.energyCost))
            return;

        currentResearchId.Value = researchId;
        researchProgress.Value = 0f;
    }
}

// Zatím jednoduchý ScriptableObject - TODO: přidat pole pro konkrétní efekt
// (např. enum UpgradeType + float value, které se pak aplikuje na PlayerStats)
[CreateAssetMenu(fileName = "NewResearch", menuName = "RTS/Research Data")]
public class ResearchData : ScriptableObject
{
    public int id;
    public string researchName;
    public int coriumCost;
    public int energyCost;
    public float duration;
}