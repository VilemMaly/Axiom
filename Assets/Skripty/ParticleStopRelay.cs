using UnityEngine;

public class ParticleStopRelay : MonoBehaviour
{
    [SerializeField] private Boom boom;

    private void OnParticleSystemStopped()
    {
        boom.OnParticleSystemStopped();
    }
}
