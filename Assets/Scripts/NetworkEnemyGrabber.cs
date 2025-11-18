using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class NetworkEnemyGrabber : NetworkEnemyBase
{
    [Header("Grab Settings")]
    public float detectRadius = 18f;
    public float grabRange = 2.2f;

    [Tooltip("Where the grabbed player is held (child of this enemy).")]
    public Transform holdPoint;

    [Tooltip("Where the player is dropped (kill zone / pit / void).")]
    public Transform killZone;

    [Header("Damage")]
    [Tooltip("Damage applied when the player is dropped into the kill zone.")]
    public float dropDamage = 999f; // tune as desired

    [Header("Animation")]
    public Animator animator;

    private enum GrabState
    {
        Idle,
        Approaching,
        CarryingToKillZone,
        Returning
    }

    private GrabState _state = GrabState.Idle;
    private NetworkObject _grabbedPlayer;
    private Vector3 _homePos;

    void Reset()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        _homePos = transform.position;
    }

    void Update()
    {
        if (!IsServer) return;

        switch (_state)
        {
            case GrabState.Idle: TickIdle(); break;
            case GrabState.Approaching: TickApproach(); break;
            case GrabState.CarryingToKillZone: TickCarry(); break;
            case GrabState.Returning: TickReturn(); break;
        }

        UpdateAnim();
    }

    // ───────────────────── States ─────────────────────

    void TickIdle()
    {
        NetworkObject target = FindNearestPlayer(detectRadius);
        if (target != null)
        {
            _grabbedPlayer = target;
            _state = GrabState.Approaching;
        }
    }

    void TickApproach()
    {
        if (_grabbedPlayer == null)
        {
            _state = GrabState.Idle;
            return;
        }

        Transform pt = _grabbedPlayer.transform;
        Vector3 flatTarget = new Vector3(pt.position.x, transform.position.y, pt.position.z);
        Vector3 dir = flatTarget - transform.position;
        float dist = dir.magnitude;

        if (dist > detectRadius * 1.5f)
        {
            // lost them
            _grabbedPlayer = null;
            _state = GrabState.Idle;
            return;
        }

        dir.Normalize();
        transform.position += dir * moveSpeed * Time.deltaTime;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);

        if (dist <= grabRange)
        {
            DoGrabServer();
        }
    }

    void DoGrabServer()
    {
        if (_grabbedPlayer == null || holdPoint == null)
        {
            _state = GrabState.Idle;
            return;
        }

        AttachPlayerServer(_grabbedPlayer);
        AttachPlayerClientRpc(_grabbedPlayer.NetworkObjectId);

        _state = GrabState.CarryingToKillZone;
    }

    void TickCarry()
    {
        if (_grabbedPlayer == null || killZone == null)
        {
            _state = GrabState.Returning;
            return;
        }

        Vector3 flatTarget = new Vector3(killZone.position.x, transform.position.y, killZone.position.z);
        Vector3 dir = flatTarget - transform.position;
        float dist = dir.magnitude;

        dir.Normalize();
        transform.position += dir * moveSpeed * Time.deltaTime;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);

        if (dist <= 1.5f)
        {
            DropAndDamageServer();
        }
    }

    void TickReturn()
    {
        Vector3 flatTarget = new Vector3(_homePos.x, transform.position.y, _homePos.z);
        Vector3 dir = flatTarget - transform.position;
        float dist = dir.magnitude;

        if (dist < 0.2f)
        {
            _state = GrabState.Idle;
            return;
        }

        dir.Normalize();
        transform.position += dir * moveSpeed * Time.deltaTime;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }

    // ───────────────────── Helpers ─────────────────────

    NetworkObject FindNearestPlayer(float radius)
    {
        NetworkObject best = null;
        float bestDist = radius;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (!client.PlayerObject) continue;
            float d = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = client.PlayerObject;
            }
        }

        return best;
    }

    void UpdateAnim()
    {
        if (!animator) return;

        bool moving =
            _state == GrabState.Approaching ||
            _state == GrabState.CarryingToKillZone ||
            _state == GrabState.Returning;

        animator.SetBool("IsMoving", moving);
        animator.SetBool("IsCarrying", _state == GrabState.CarryingToKillZone);
    }

    // ───────────────────── Server-side attach / drop ─────────────────────

    void AttachPlayerServer(NetworkObject player)
    {
        Transform t = player.transform;

        t.SetParent(holdPoint);
        t.position = holdPoint.position;
        t.rotation = holdPoint.rotation;

        var rb = t.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true; // physics won’t fight the parenting
        }

        // OPTIONAL: disable their movement controller component here
        // var controller = t.GetComponent<YourPlayerController>();
        // if (controller) controller.enabled = false;
    }

    void DropAndDamageServer()
    {
        if (_grabbedPlayer)
        {
            Transform t = _grabbedPlayer.transform;

            // Unparent and move to kill zone
            t.SetParent(null);
            t.position = killZone.position;

            var rb = t.GetComponent<Rigidbody>();
            if (rb)
            {
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Damage
            if (_grabbedPlayer.TryGetComponent(out NetworkPlayerHealth hp))
            {
                hp.ServerTakeDamage(dropDamage, Vector3.down, 0f);
            }

            // OPTIONAL: re-enable controller
            // var controller = t.GetComponent<YourPlayerController>();
            // if (controller) controller.enabled = true;
        }

        if (_grabbedPlayer)
        {
            DropPlayerClientRpc(_grabbedPlayer.NetworkObjectId, killZone.position);
        }

        _grabbedPlayer = null;
        _state = GrabState.Returning;
    }

    // ───────────────────── RPCs ─────────────────────

    [ClientRpc]
    void AttachPlayerClientRpc(ulong playerNetId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetId, out var obj))
            return;

        Transform t = obj.transform;
        t.SetParent(holdPoint, true);
        t.position = holdPoint.position;
        t.rotation = holdPoint.rotation;

        var rb = t.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    [ClientRpc]
    void DropPlayerClientRpc(ulong playerNetId, Vector3 dropPos)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetId, out var obj))
            return;

        Transform t = obj.transform;
        t.SetParent(null, true);
        t.position = dropPos;

        var rb = t.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}
