using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(GenericAttacker))]
public class RadarTroop : Troop
{
    public override void OnSpawned()
    {
    }
    // Asi bude odhalovat miny a skenovat okolí pravidelnýnma pingama
}
