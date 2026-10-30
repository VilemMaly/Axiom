using Unity.Netcode;
using UnityEngine;

[System.Serializable]
public class TroopDamageEffect
{
    public DamageType damageType;
    public AudioClip hitClip;
    public ParticleSystem particle;
}

/// <summary>
/// Plays the configured hit particle and sound for a damage type on every client.
/// Place this component on the same GameObject as the troop's NetworkObject.
/// </summary>
public class DamageHitRelay : NetworkBehaviour
{
    [Header("Hit efekty")]
    [SerializeField] private TroopDamageEffect[] damageEffects;

    [Header("Zvuk")]
    [SerializeField] private AudioSource audioSource;

    /// <summary>Called by the server when this troop takes damage.</summary>
    public void PlayHit(DamageType damageType)
    {
        if (!IsSpawned || !IsServer)
            return;

        PlayHitLocal(damageType);
        PlayHitClientRpc(damageType);
    }

    private void PlayHitLocal(DamageType damageType)
    {
        if (damageEffects == null)
            return;

        foreach (TroopDamageEffect effect in damageEffects)
        {
            if (effect == null || effect.damageType != damageType)
                continue;

            if (effect.particle != null)
                effect.particle.Play();

            if (audioSource != null && effect.hitClip != null)
                audioSource.PlayOneShot(effect.hitClip);

            return;
        }
    }

    [ClientRpc]
    private void PlayHitClientRpc(DamageType damageType)
    {
        // Host already played the effect in PlayHitLocal.
        if (!IsServer)
            PlayHitLocal(damageType);
    }
}
