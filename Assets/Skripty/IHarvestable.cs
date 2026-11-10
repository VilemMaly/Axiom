using UnityEngine;

public interface IHarvestable
{
    Transform HarvestTransform { get; }
    void DepleteResource(Troop harvester, int amount);
}
