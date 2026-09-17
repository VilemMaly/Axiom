using System.Collections.Generic;
using UnityEngine;

namespace STT {

[System.Serializable]
public class TurretParameters {

	[Header("Status")]
	[Tooltip("Activate or deactivate the Turret")]
	public bool active;
	public bool canFire;

	[Header("Shooting")]
	[Tooltip("Burst the force when hit")]
	public float power;
	[Tooltip("Pause between shooting")]
	[Range(0.5f,2)]
	public float ShootingDelay;
	[Tooltip("Radius of the turret view")]
	public float radius;

	[Header("Physics")]
	[Tooltip("Angular damping applied to the rigidbody to prevent rotation overshooting")]
	public float rotationDamping = 5f;

	[Header("Body Collider")]
	public Vector3 bodyColliderSize = new Vector3(2, 2, 2);
	public Vector3 bodyColliderCenter = new Vector3(0, 1, 0);
}

[System.Serializable]
public class TurretFX {

	[Tooltip("Muzzle transform position")]
	public Transform muzzle;
	[Tooltip("Spawn this GameObject when shooting")]
	public GameObject shotFX;
}

[System.Serializable]
public class TurretAudio {

	public AudioClip shotClip;
}

[System.Serializable]
public class TurretTargeting {

	[Tooltip("Speed of aiming at the target")]
	public float aimingSpeed;
	[Tooltip("Pause before aiming starts after acquiring a target")]
	public float aimingDelay;
	[Tooltip("GameObjects with the following tags will be identified as enemies")]
	public string[] tagsToFire;
	public List<Collider> targets = new List<Collider>();
	public Collider target;
}

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(STT_Actor))]
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Animator))]
public class STT_Turret : MonoBehaviour {

	public TurretParameters parameters;
	public TurretTargeting targeting;
	public TurretFX VFX;
	public TurretAudio SFX;

	private Rigidbody rb;
	private AudioSource audioSource;
	private Animator animator;
	private bool isShooting;
	private bool isAiming;

	private void Awake() {
		rb = GetComponent<Rigidbody>();
		audioSource = GetComponent<AudioSource>();
		animator = GetComponent<Animator>();

		rb.angularDamping = parameters.rotationDamping;

		GetComponent<SphereCollider>().isTrigger = true;
		GetComponent<SphereCollider>().radius = parameters.radius;
		GetComponent<BoxCollider>().size = parameters.bodyColliderSize;
		GetComponent<BoxCollider>().center = parameters.bodyColliderCenter;
	}

	private void FixedUpdate() {
		if (!parameters.active) return;

		ClearTargets();

		if (targeting.target != null) {
			if (isAiming) Aiming();
			if (!isShooting) {
				isShooting = true;
				Invoke(nameof(Shooting), parameters.ShootingDelay);
			}
		} else {
			if (isShooting) {
				CancelInvoke(nameof(Shooting));
				isShooting = false;
			}
			if (isAiming) {
				CancelInvoke(nameof(EnableAiming));
				isAiming = false;
			}
		}
	}

	#region Aiming and Shooting

	private void Shot() {
		audioSource.PlayOneShot(SFX.shotClip, Random.Range(0.75f, 1f));
		animator.SetTrigger("Shot");
		GameObject newShotFX = Instantiate(VFX.shotFX, VFX.muzzle);
		Destroy(newShotFX, 2);
	}

	private void Shooting() {
		isShooting = false;

		if (targeting.target == null) return;
		if (!parameters.canFire) return;

		RaycastHit hit;
		if (Physics.Raycast(VFX.muzzle.position, VFX.muzzle.transform.forward, out hit, parameters.radius)) {
			if (CheckTags(hit.collider)) {
				Shot();
				hit.collider.GetComponent<STT_Actor>().ReceiveDamage(parameters.power, hit.point);
				ClearTargets();
			}
		}
	}

	public void Aiming() {
		if (targeting.target == null) return;

		Vector3 delta = targeting.target.transform.position - transform.position;
		float angle = Vector3.Angle(transform.forward, delta);
		Vector3 cross = Vector3.Cross(transform.forward, delta);
		rb.AddTorque(cross * angle * targeting.aimingSpeed);
	}

	#endregion

	#region Targeting

	private void OnTriggerEnter(Collider other) {
		if (!parameters.active) return;

		ClearTargets();

		if (CheckTags(other)) {
			if (targeting.targets.Count == 0) {
				targeting.target = other;
				if (targeting.aimingDelay > 0f)
					Invoke(nameof(EnableAiming), targeting.aimingDelay);
				else
					isAiming = true;
			}
			targeting.targets.Add(other);
		}
	}

	private void OnTriggerExit(Collider other) {
		if (!parameters.active) return;

		ClearTargets();

		if (CheckTags(other)) {
			targeting.targets.Remove(other);
			targeting.target = targeting.targets.Count > 0 ? targeting.targets[0] : null;
		}
	}

	private void EnableAiming() {
		isAiming = true;
	}

	private bool CheckTags(Collider toMatch) {
		for (int i = 0; i < targeting.tagsToFire.Length; i++) {
			if (toMatch.CompareTag(targeting.tagsToFire[i])) return true;
		}
		return false;
	}

	private void ClearTargets() {
		for (int i = targeting.targets.Count - 1; i >= 0; i--) {
			Collider t = targeting.targets[i];
			if (t == null || !t.enabled)
				targeting.targets.RemoveAt(i);
		}
		targeting.target = targeting.targets.Count > 0 ? targeting.targets[0] : null;
	}

	#endregion
}

}
