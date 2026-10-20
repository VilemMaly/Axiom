using System;
using UnityEngine;

public class Wall : Building
{
    public override BuildingType Type => BuildingType.Wall;

    // Zeď nemá aktivní logiku - jen vysoké HP a blokuje pohyb (přes collider).
    // TODO: pokud budeš chtít "napojování" zdí do sebe (jako palisády v AoE),
    // řeš to v OnBuilt() kontrolou sousedních Wall objektů a přepnutím meshe.

    [SerializeField] public Transform leftLock;
    [SerializeField] public Transform rightLock;

    public override void OnBuilt()
    {
    }
}