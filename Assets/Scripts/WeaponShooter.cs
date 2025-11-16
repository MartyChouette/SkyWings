// WeaponShooter.cs  (Luigi vacuum, new Rpc API, fixed)
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;



[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(NetworkTransform))]    // 👈 add this
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
        // Only the local player reads input and sends RPCs.
        if (!IsOwner || cam == null) return;

        // ───────── Reticle hover ─────────
        Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        bool onCrop = Physics.Raycast(
                          aimRay,
                          out RaycastHit hoverHit,
                          vacuumRange,
                          hitMask,
                          QueryTriggerInteraction.Ignore
                      )
                      && hoverHit.collider.GetComponentInParent<NetworkCropOrb>() != null;

        if (reticle) reticle.SetTargeting(onCrop);

        // ───────── Input ─────────
        bool held = Mouse.current != null && Mouse.current.leftButton.isPressed;

        if (held && !isVacuuming)
        {
            isVacuuming = true;
            vacuumTickCD = 0f;

            // Owner plays FX immediately
            StartVacuumLocal();

            // Tell the server we started, so it can fan out to other clients
            StartVacuumServerRpc();
        }
        else if (!held && isVacuuming)
        {
            isVacuuming = false;

            StopVacuumLocal();
            StopVacuumServerRpc();
        }

        if (!isVacuuming) return;

        // ───────── Tick to send suction ray to server ─────────
        vacuumTickCD -= Time.deltaTime;
        if (vacuumTickCD <= 0f)
        {
            vacuumTickCD = 1f / Mathf.Max(1f, vacuumTicksPerSecond);

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            VacuumServerRpc(ray.origin, ray.direction);
        }
    }

    // ───────── Local FX helpers (no networking here) ─────────

    void StartVacuumLocal()
    {
        if (vacuumLoopPrefab && muzzle && _vacuumInstance == null)
        {
            _vacuumInstance = Instantiate(
                vacuumLoopPrefab,
                muzzle.position,
                muzzle.rotation,
                muzzle
            );
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

    // ───────── RPCs for starting / stopping vacuum FX ─────────
    // Owner -> Server
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void StartVacuumServerRpc(RpcParams rpcParams = default)
    {
        // On server, tell all clients (including host's client) to start FX
        StartVacuumClientRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void StopVacuumServerRpc(RpcParams rpcParams = default)
    {
        StopVacuumClientRpc();
    }

    // Server -> Clients
    // Runs on all *clients* (including the host's client);
    // we skip the owner because they already did StartVacuumLocal/StopVacuumLocal.
    [Rpc(SendTo.ClientsAndHost)]
    void StartVacuumClientRpc(RpcParams rpcParams = default)
    {
        if (IsOwner) return; // already started locally
        StartVacuumLocal();
    }

    [Rpc(SendTo.ClientsAndHost)]
    void StopVacuumClientRpc(RpcParams rpcParams = default)
    {
        if (IsOwner) return; // already stopped locally
        StopVacuumLocal();
    }

    // ───────── Vacuum hits (gameplay) ─────────
    // Owner -> Server
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void VacuumServerRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
    {
        // Only runs on server
        Ray ray = new Ray(origin, direction.normalized);

        RaycastHit[] hits = Physics.SphereCastAll(
            ray,
            vacuumRadius,
            vacuumRange,
            hitMask,
            QueryTriggerInteraction.Ignore
        );

        // Optional debug
        // Debug.Log($"[SERVER] Vacuum from client {OwnerClientId}, hits: {hits.Length}");

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
