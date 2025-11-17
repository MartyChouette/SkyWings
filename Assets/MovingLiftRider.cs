using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(CharacterController))]
public class MovingLiftRider : NetworkBehaviour
{
    [Header("Ground check")]
    public float groundRayDistance = 1.0f;
    public LayerMask groundMask = ~0;  // you can restrict this to your level layers

    CharacterController _cc;
    Transform _currentPlatform;
    Vector3 _lastPlatformPos;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    void LateUpdate()
    {
        // Only move the player on the controlling side
        if (!IsOwner) return;

        UpdatePlatformLock();
        ApplyPlatformDelta();
    }

    void UpdatePlatformLock()
    {
        if (_cc.isGrounded)
        {
            // Cast down to see what we're standing on
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            if (Physics.Raycast(origin, Vector3.down, out var hit,
                    groundRayDistance, groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.CompareTag("Lift"))
                {
                    if (_currentPlatform != hit.collider.transform)
                    {
                        _currentPlatform = hit.collider.transform;
                        _lastPlatformPos = _currentPlatform.position;
                    }
                    return;
                }
            }
        }

        // Not on a lift anymore
        _currentPlatform = null;
    }

    void ApplyPlatformDelta()
    {
        if (_currentPlatform == null) return;

        Vector3 currentPos = _currentPlatform.position;
        Vector3 delta = currentPos - _lastPlatformPos;

        if (delta.sqrMagnitude > 0f)
        {
            // Move player by the same amount the platform moved
            _cc.Move(delta);
        }

        _lastPlatformPos = currentPos;
    }
}