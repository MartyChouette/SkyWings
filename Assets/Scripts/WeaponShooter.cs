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

    [Header("Shoot")]
    public float shootRange = 50f;
    public LayerMask shootMask = ~0;

    [Header("VFX/SFX")]
    public Transform muzzle;
    public ParticleSystem vacuumLoopPrefab;
    public ParticleSystem shootMuzzlePrefab;
    public ParticleSystem hitSparkPrefab;
    public AudioSource audioSource;
    public AudioClip vacuumLoopClip;
    public AudioClip shootClip;

    [Header("UI")]
    public ReticleHUD reticle;

    bool isVacuuming;
    float vacuumTickCD;
    ParticleSystem _vacuumInstance;

    PlayerCornInventory _inventory;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!muzzle) muzzle = cam ? cam.transform : transform;
        if (!audioSource) audioSource = GetComponent<AudioSource>();
        _inventory = GetComponent<PlayerCornInventory>();
    }

    void Update()
    {
        if (!IsOwner || cam == null) return;

        Ray aimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        bool onCrop = Physics.Raycast(aimRay, out RaycastHit hoverHit, vacuumRange, hitMask, QueryTriggerInteraction.Ignore)
                      && hoverHit.collider.GetComponentInParent<NetworkCropOrb>() != null;
        if (reticle) reticle.SetTargeting(onCrop);

        bool lmbHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool rmbPressed = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

        // ───────── Vacuum (LMB hold) ─────────
        HandleVacuum(lmbHeld, aimRay);

        // ───────── Shoot (RMB press) ─────────
        if (rmbPressed)
        {
            Vector3 origin = aimRay.origin;
            Vector3 direction = aimRay.direction;

            // optional client-side gate to avoid spam when ammo is zero
            if (_inventory == null || _inventory.Ammo <= 0)
            {
                // could play a "click" sound or UI feedback here
            }
            else
            {
                ShootServerRpc(origin, direction);
            }
        }
    }

    void HandleVacuum(bool held, Ray aimRay)
    {
        if (held && !isVacuuming)
        {
            isVacuuming = true;
            vacuumTickCD = 0f;
            StartVacuumLocal();
            StartVacuumClientsRpc();    // show FX on other clients
        }
        else if (!held && isVacuuming)
        {
            isVacuuming = false;
            StopVacuumLocal();
            StopVacuumClientsRpc();
        }

        if (!isVacuuming) return;

        vacuumTickCD -= Time.deltaTime;
        if (vacuumTickCD <= 0f)
        {
            vacuumTickCD = 1f / Mathf.Max(1f, vacuumTicksPerSecond);

            Vector3 origin = aimRay.origin;
            Vector3 direction = aimRay.direction;
            VacuumServerRpc(origin, direction);
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

    // ───────── Vacuum RPCs ─────────

    [Rpc(SendTo.ClientsAndHost)]
    void StartVacuumClientsRpc(RpcParams rpcParams = default)
    {
        if (IsOwner) return; // owner already started local FX

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

    [Rpc(SendTo.ClientsAndHost)]
    void StopVacuumClientsRpc(RpcParams rpcParams = default)
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
    void VacuumServerRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
    {
        Ray ray = new Ray(origin, direction.normalized);

        RaycastHit[] hits = Physics.SphereCastAll(
            ray,
            vacuumRadius,
            vacuumRange,
            hitMask,
            QueryTriggerInteraction.Ignore
        );

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

    // ───────── Shooting ─────────

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ShootServerRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
    {
        var player = GetComponent<PlayerCornInventory>();
        if (player == null) return;

        // ammo gate (server-authoritative)
        if (!player.ServerConsumeAmmo(1))
            return;

        // Do actual hit detection server-side
        Ray ray = new Ray(origin, direction.normalized);
        if (Physics.Raycast(ray, out RaycastHit hit, shootRange, shootMask, QueryTriggerInteraction.Ignore))
        {
            // damage enemies
            var enemy = hit.collider.GetComponentInParent<NetworkEnemyBase>();
            if (enemy != null)
            {
                enemy.ServerApplyDamage(1); // or pass damage amount
            }

            // spawn hit FX for everyone
            ShootHitClientsRpc(muzzle ? muzzle.position : origin, hit.point, hit.normal);
        }
        else
        {
            // no hit, just play muzzle FX
            ShootHitClientsRpc(muzzle ? muzzle.position : origin, origin + direction.normalized * shootRange, -direction.normalized);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ShootHitClientsRpc(Vector3 muzzlePos, Vector3 hitPos, Vector3 hitNormal, RpcParams rpcParams = default)
    {
        // muzzle flash
        if (shootMuzzlePrefab && muzzle)
            Instantiate(shootMuzzlePrefab, muzzle.position, muzzle.rotation, muzzle).Play();

        // hit spark
        if (hitSparkPrefab)
            Instantiate(hitSparkPrefab, hitPos, Quaternion.LookRotation(hitNormal)).Play();

        // sound
        if (audioSource && shootClip)
            audioSource.PlayOneShot(shootClip);

        if (reticle) reticle.Pop();
    }
}
