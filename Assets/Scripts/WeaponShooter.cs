// WeaponShooter.cs  (Luigi vacuum, new Rpc API)
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class WeaponShooter : NetworkBehaviour
{
    [Header("Core")]
    public Camera cam;
    public float vacuumRange = 25f;
    public float vacuumRadius = 1.2f;
    public LayerMask hitMask = ~0;
    public float vacuumTicksPerSecond = 10f;

    [Header("VFX/SFX")]
    public Transform muzzle;
    public ParticleSystem vacuumLoopPrefab;
    public AudioSource audioSource;
    public AudioClip vacuumLoopClip;

    [Header("UI")]
    public ReticleHUD reticle;

    bool isVacuuming;
    float vacuumTickCD;
    ParticleSystem _vacuumInstance;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!muzzle) muzzle = cam ? cam.transform : transform;
        if (!audioSource) audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        if (!IsOwner || cam == null) return;

        // Reticle hover

        Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        bool onCrop = Physics.Raycast(aimRay, out RaycastHit hoverHit, vacuumRange, hitMask, QueryTriggerInteraction.Ignore)
                      && hoverHit.collider.GetComponentInParent<NetworkCropOrb>() != null;
        if (reticle) reticle.SetTargeting(onCrop);

        // Hold LMB to vacuum
        bool held = Mouse.current != null && Mouse.current.leftButton.isPressed;

        if (held && !isVacuuming)
        {
            isVacuuming = true;
            vacuumTickCD = 0f;
            StartVacuumLocal();
            StartVacuumClientRpc();
        }
        else if (!held && isVacuuming)
        {
            isVacuuming = false;
            StopVacuumLocal();
            StopVacuumClientRpc();
        }

        if (!isVacuuming) return;

        vacuumTickCD -= Time.deltaTime;
        if (vacuumTickCD <= 0f)
        {
            vacuumTickCD = 1f / Mathf.Max(1f, vacuumTicksPerSecond);

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            VacuumRpc(ray.origin, ray.direction);

        }
    }

    // ───────── local FX ─────────

    void StartVacuumLocal()
    {
        if (vacuumLoopPrefab && muzzle && _vacuumInstance == null)
        {
            _vacuumInstance = Instantiate(vacuumLoopPrefab, muzzle.position, muzzle.rotation, muzzle);
            _vacuumInstance.Play();
        }

        if (audioSource && vacuumLoopClip)
        {
            audioSource.clip = vacuumLoopClip;
            audioSource.loop = true;
            if (!audioSource.isPlaying)
                audioSource.Play();
        }
    }

    void StopVacuumLocal()
    {
        if (_vacuumInstance)
        {
            _vacuumInstance.Stop();
            Destroy(_vacuumInstance.gameObject, 1f);
            _vacuumInstance = null;
        }

        if (audioSource && audioSource.clip == vacuumLoopClip)
        {
            audioSource.Stop();
            audioSource.loop = false;
        }
    }

    [ClientRpc]
    void StartVacuumClientRpc()
    {
        if (IsOwner) return;

        if (vacuumLoopPrefab && muzzle && _vacuumInstance == null)
        {
            _vacuumInstance = Instantiate(vacuumLoopPrefab, muzzle.position, muzzle.rotation, muzzle);
            _vacuumInstance.Play();
        }

        if (audioSource && vacuumLoopClip)
        {
            audioSource.clip = vacuumLoopClip;
            audioSource.loop = true;
            if (!audioSource.isPlaying)
                audioSource.Play();
        }
    }

    [ClientRpc]
    void StopVacuumClientRpc()
    {
        if (IsOwner) return;

        if (_vacuumInstance)
        {
            _vacuumInstance.Stop();
            Destroy(_vacuumInstance.gameObject, 1f);
            _vacuumInstance = null;
        }

        if (audioSource && audioSource.clip == vacuumLoopClip)
        {
            audioSource.Stop();
            audioSource.loop = false;
        }
    }


    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void VacuumRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
    {
        Ray ray = new Ray(origin, direction.normalized);

        RaycastHit[] hits = Physics.SphereCastAll(
            ray,
            vacuumRadius,
            vacuumRange,
            hitMask,
            QueryTriggerInteraction.Ignore
        );

        Debug.Log($"[SERVER] Vacuum from client {OwnerClientId}, hits: {hits.Length}");

        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];

            NetworkCropOrb orb = hit.collider.GetComponentInParent<NetworkCropOrb>();
            if (orb != null)
            {
                Vector3 normal = -direction.normalized;
                orb.DetachAndFollow(OwnerClientId, hit.point, normal);
            }
        }
    }
}
