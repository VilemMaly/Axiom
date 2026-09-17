using UnityEngine;

namespace STT {

[System.Serializable]
public class ActorParameters {

	[Tooltip("Actor's Health")]
	public float toughness;
	[Tooltip("Threshold of the applied force")]
	public float armor;
	public float damageFactor;
}

[System.Serializable]
public class ActorFX {

	[Tooltip("Spawn this GameObject when the turret is hitting")]
	public GameObject damageFX;
	[Tooltip("Spawn this GameObject when the actor is destroyed")]
	public GameObject deactivateFX;
}

[System.Serializable]
public class ActorAudio {

	public AudioClip destroyClip;
}

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class STT_Actor : MonoBehaviour {

	public ActorParameters parameters;
	public ActorFX VFX;
	public ActorAudio SFX;

	private Rigidbody actor;
	private AudioSource audioSource;
	private Collider actorCollider;
	private Renderer actorRenderer;
	private bool isDead;

	void Awake() {
		actor = GetComponent<Rigidbody>();
		audioSource = GetComponent<AudioSource>();
		actorCollider = GetComponent<Collider>();
		actorRenderer = GetComponent<Renderer>();
	}

	public void ReceiveDamage(float damage, Vector3 position) {
		if (isDead) return;

		if (parameters.toughness - damage > 0) {
			parameters.toughness -= damage;
			actor.AddExplosionForce(damage * parameters.damageFactor, position, 0.25f);

			if (VFX.damageFX != null) {
				GameObject newDamageFX = Instantiate(VFX.damageFX, position, Quaternion.identity);
				Destroy(newDamageFX, 3);
			}
		} else {
			if (VFX.deactivateFX != null) {
				GameObject newDeactivateFX = Instantiate(VFX.deactivateFX, transform.position, Quaternion.identity);
				Destroy(newDeactivateFX, 3);
			}

			Die();
		}
	}

	private void OnCollisionEnter(Collision collision) {
		if (collision.relativeVelocity.magnitude > parameters.armor) {
			ReceiveDamage(collision.relativeVelocity.magnitude, transform.position);
		}
	}

	public void Die() {
		if (isDead) return;
		isDead = true;
		actorCollider.enabled = false;
		actorRenderer.enabled = false;
		if (SFX.destroyClip != null) audioSource.PlayOneShot(SFX.destroyClip);
		Destroy(gameObject, 2);
	}
}

}
