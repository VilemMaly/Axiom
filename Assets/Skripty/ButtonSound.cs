using UnityEngine;

public class ButtonSound : MonoBehaviour
{
    [SerializeField] private AudioClip audioClip;
    [SerializeField] private AudioSource audioSource;

    // Assign this method to the Button's On Click() event in the Inspector.
    public void PlaySound()
    {
        if (audioSource == null || audioClip == null)
        {
            Debug.LogWarning("ButtonSound needs an AudioSource and an AudioClip.", this);
            return;
        }

        audioSource.PlayOneShot(audioClip);
    }
}