// WeaponShooter.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponShooter : MonoBehaviour
{
    [Header("Core")]
    public Camera cam;
    public float range = 50f;
    public LayerMask hitMask = ~0;
    public float shootCooldown = 0.1f;

    [Header("VFX/SFX")]
    public Transform muzzle;                    // where muzzle flash spawns (can use camera)
    public ParticleSystem muzzleFlashPrefab;    // short burst
    public ParticleSystem hitSparkPrefab;       // small spark/poof on crop hit
    public AudioSource audioSource;             // optional
    public AudioClip shootClip;

    [Header("UI")]
    public ReticleHUD reticle;

    float cd;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!muzzle) muzzle = cam ? cam.transform : transform;
        if (!audioSource) audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        cd -= Time.deltaTime;

        // Continuous center ray to tint reticle when hovering a crop
        Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        bool onCrop = Physics.Raycast(aimRay, out RaycastHit hoverHit, range, hitMask, QueryTriggerInteraction.Ignore)
                      && hoverHit.collider.GetComponentInParent<CropOrb>() != null;
        if (reticle) reticle.SetTargeting(onCrop);

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && cd <= 0f)
        {
            cd = shootCooldown;
            Shoot();
        }
    }

    void Shoot()
    {
        // 🔸 MUZZLE PARTICLES (spawn here)
        if (muzzleFlashPrefab && muzzle)
            Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation).Play();

        if (audioSource && shootClip)
            audioSource.PlayOneShot(shootClip);

        if (reticle) reticle.Pop(); // 🔸 RETICLE POP on shoot

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            // 🔸 HIT PARTICLES (spawn at impact)
            if (hitSparkPrefab)
                Instantiate(hitSparkPrefab, hit.point, Quaternion.LookRotation(hit.normal)).Play();

            var orb = hit.collider.GetComponentInParent<CropOrb>();
            if (orb) orb.DetachAndFollow(PlayerRoot(), hit.point, hit.normal); // pass impact for orb VFX
        }
    }

    Transform PlayerRoot()
    {
        // assume weapon is under the player hierarchy
        Transform t = transform;
        while (t.parent != null) t = t.parent;
        return t;
    }
}
