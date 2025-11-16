using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Third-person networked player: owner handles input & camera;
/// server owns gameplay (shoot raycast → detach corn, damage, etc.)
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayer : NetworkBehaviour
{
    [Header("Refs")]
    public Transform cameraRig;      // separate object, NOT under player
    public Transform cameraPivot;    // where camera looks (head); if null uses transform
    public Camera playerCamera;      // per-player camera
    private CharacterController cc;

    [Header("Move")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 8.5f;
    public float rotationLerp = 12f;
    public float jumpHeight = 2f;
    public float gravity = -20f;

    [Header("Camera Orbit")]
    public float mouseSensitivity = 120f; // deg/sec
    public float minPitch = -35f;
    public float maxPitch = 70f;
    public float cameraDistance = 4f;
    public float cameraHeight = 1.5f;
    public bool lockCursor = true;
    public bool invertY = false;

    [Header("Combat")]
    public float shootRange = 50f;
    public float shootCooldown = 0.1f;
    public LayerMask shootMask = ~0;
    public Transform muzzle;
    public ParticleSystem muzzleFlashPrefab;
    public ParticleSystem hitSparkPrefab;
    public AudioSource audioSource;
    public AudioClip shootClip;
    public ReticleHUD reticle;

    float yaw, pitch, verticalVel;
    float shootCd;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!cameraPivot) cameraPivot = transform;

        if (!playerCamera)
        {
            // assume there is ONE camera child under rig or player
            playerCamera = GetComponentInChildren<Camera>();
        }

        if (lockCursor && Application.isFocused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Only local owner gets input + camera
        if (!IsOwner)
        {
            if (playerCamera)
                playerCamera.enabled = false;

            if (audioSource)
                audioSource.spatialBlend = 1f; // 3D for remote players
            return;
        }

        // Owner camera rig setup
        if (!cameraRig || cameraRig == transform || cameraRig.IsChildOf(transform))
        {
            var rigGO = new GameObject($"CameraRig_{OwnerClientId}");
            cameraRig = rigGO.transform;
        }

        if (playerCamera)
            playerCamera.transform.SetParent(cameraRig, true);

        Vector3 t = cameraPivot.position + Vector3.up * cameraHeight;
        cameraRig.position = t - transform.forward * cameraDistance;
        cameraRig.rotation = Quaternion.LookRotation(t - cameraRig.position, Vector3.up);

        if (!audioSource)
            audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        if (!IsOwner) return;

        HandleCamera();
        HandleMove();
        HandleShootInput();
    }

    void HandleCamera()
    {
        if (Mouse.current == null || playerCamera == null || cameraRig == null)
            return;

        Vector2 md = Mouse.current.delta.ReadValue();
        float dt = Time.deltaTime;
        yaw += md.x * mouseSensitivity * dt;
        pitch += (invertY ? md.y : -md.y) * mouseSensitivity * dt;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Vector3 target = cameraPivot.position + Vector3.up * cameraHeight;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pos = target - (rot * Vector3.forward * cameraDistance);

        cameraRig.position = pos;
        cameraRig.rotation = rot;
        playerCamera.transform.LookAt(target);
    }

    void HandleMove()
    {
        if (Keyboard.current == null) return;

        float x = 0, z = 0;
        if (Keyboard.current.wKey.isPressed) z += 1;
        if (Keyboard.current.sKey.isPressed) z -= 1;
        if (Keyboard.current.aKey.isPressed) x -= 1;
        if (Keyboard.current.dKey.isPressed) x += 1;

        Vector3 input = Vector3.ClampMagnitude(new Vector3(x, 0, z), 1f);
        if (cameraRig == null) return;

        Vector3 camF = Vector3.Scale(cameraRig.forward, new Vector3(1, 0, 1)).normalized;
        Vector3 camR = cameraRig.right;
        Vector3 moveDir = (camF * input.z + camR * input.x).normalized;

        if (moveDir.sqrMagnitude > 0.0001f)
        {
            Quaternion tRot = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.Lerp(transform.rotation, tRot, rotationLerp * Time.deltaTime);
        }

        float speed = Keyboard.current.leftShiftKey.isPressed ? sprintSpeed : moveSpeed;
        Vector3 horizontal = moveDir * speed;

        if (cc.isGrounded)
        {
            verticalVel = -2f;
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else verticalVel += gravity * Time.deltaTime;

        cc.Move(new Vector3(horizontal.x, verticalVel, horizontal.z) * Time.deltaTime);
    }

    void HandleShootInput()
    {
        shootCd -= Time.deltaTime;

        if (playerCamera != null)
        {
            // reticle hover tint
            Ray aimRay = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            bool onCrop = Physics.Raycast(aimRay, out RaycastHit hoverHit, shootRange, shootMask, QueryTriggerInteraction.Ignore)
                          && hoverHit.collider.GetComponentInParent<NetworkCropOrb>() != null;
            if (reticle) reticle.SetTargeting(onCrop);
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && shootCd <= 0f)
        {
            shootCd = shootCooldown;
            LocalShootFX(); // immediate feedback
            if (playerCamera)
            {
                Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                ShootRpc(ray.origin, ray.direction);

            }
        }
    }

    void LocalShootFX()
    {
        if (muzzleFlashPrefab && muzzle)
            Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation).Play();

        if (audioSource && shootClip)
            audioSource.PlayOneShot(shootClip);

        if (reticle) reticle.Pop();
    }

 

    [ClientRpc]
    void SpawnHitSparkClientRpc(Vector3 pos, Vector3 normal)
    {
        if (hitSparkPrefab)
            Instantiate(hitSparkPrefab, pos, Quaternion.LookRotation(normal)).Play();
    }

    public Transform GetPlayerRoot()
    {
        return transform;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ShootRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
    {
        Ray ray = new Ray(origin, direction);
        if (Physics.Raycast(ray, out RaycastHit hit, shootRange, shootMask, QueryTriggerInteraction.Ignore))
        {
            if (hitSparkPrefab)
                SpawnHitSparkClientRpc(hit.point, hit.normal);

            // 1) Corn orbs
            NetworkCropOrb orb = hit.collider.GetComponentInParent<NetworkCropOrb>();
            if (orb != null)
            {
                orb.DetachAndFollow(OwnerClientId, hit.point, hit.normal);
                return;
            }

            // 2) Enemies
            NetworkEnemyBase enemy = hit.collider.GetComponentInParent<NetworkEnemyBase>();
            if (enemy != null)
            {
                enemy.ServerApplyDamage(10f);
                return;
            }

            // 3) Later: props etc.
        }
    }

}


