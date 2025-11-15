// ThirdPersonAimController.cs
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("Refs")]
    public Transform cameraRig;      // separate object, NOT under player
    public Transform cameraPivot;    // where camera looks (e.g., head). If null, uses player
    private Camera mainCam;
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

    float yaw, pitch, verticalVel;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        mainCam = Camera.main;
        if (!cameraPivot) cameraPivot = transform;

        if (lockCursor) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        // Make or fix a rig if mis-assigned
        if (!cameraRig || cameraRig == transform || cameraRig.IsChildOf(transform))
        {
            var rigGO = new GameObject("CameraRig");
            cameraRig = rigGO.transform;
            if (mainCam) mainCam.transform.SetParent(cameraRig, true);
        }

        // place rig behind
        Vector3 t = cameraPivot.position + Vector3.up * cameraHeight;
        cameraRig.position = t - transform.forward * cameraDistance;
        cameraRig.rotation = Quaternion.LookRotation(t - cameraRig.position, Vector3.up);
    }

    void Update()
    {
        HandleCamera();
        HandleMove();
    }

    void HandleCamera()
    {
        if (Mouse.current == null) return;

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
        if (mainCam) mainCam.transform.LookAt(target);
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
}
