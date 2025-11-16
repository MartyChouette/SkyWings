using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkCropOrb : NetworkBehaviour
{
    public enum State : byte
    {
        Attached,
        FollowingPlayer,
        ToBalloon,
        Absorbed
    }

    [Header("Follow Tuning")]
    public float followSpeed = 8f;
    public float steering = 6f;
    public float maxSpeed = 10f;
    public float keepDistance = 1.2f;

    [Header("VFX")]
    public ParticleSystem shotPoofPrefab;
    public ParticleSystem absorbPoofPrefab;

    // ─────────────────────────────── private state ───────────────────────────────

    Rigidbody rb;

    // While attached / following / to-balloon we use this as the “chase” target
    Transform followTarget;
    Vector3 localOffset;

    // When absorbed, we no longer parent but we keep an anchor + offset
    Transform absorbedAnchor;
    Vector3 absorbedOffset;

    NetworkVariable<State> state = new NetworkVariable<State>(
        State.Attached,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ─────────────────────────────── lifecycle ───────────────────────────────

    void Awake()
    {
        var col = GetComponent<SphereCollider>();
        col.isTrigger = false;

        rb = GetComponent<Rigidbody>();
        if (!rb)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = 1f;
            rb.angularDamping = 0.5f;
#else
            rb.drag        = 1f;
            rb.angularDrag = 0.5f;
#endif
        }
        // DO NOT decide kinematic / gravity here; we’ll do it in OnNetworkSpawn
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!rb)
            rb = GetComponent<Rigidbody>();

        if (IsServer)
        {
            // Server owns physics
            rb.isKinematic = false;
            rb.useGravity = true;
        }
        else
        {
            // Clients: let NetworkTransform drive; no local physics
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (IsServer)
        {
            state.Value = State.Attached;
        }
    }

    // ─────────────────────────────── public API ───────────────────────────────

    /// <summary>
    /// Shot by a player; detach from stalk and start following that player.
    /// Server only.
    /// </summary>
    public void DetachAndFollow(ulong ownerClientId, Vector3 impactPoint, Vector3 impactNormal)
    {
        if (!IsServer) return;

        Debug.Log($"[SERVER] DetachAndFollow on {name}, state={state.Value}, owner={ownerClientId}");

        if (state.Value != State.Attached && state.Value != State.FollowingPlayer)
            return;

        if (shotPoofPrefab)
            SpawnShotPoofClientRpc(impactPoint, impactNormal);

        // Detach from stalk (null parent is valid for a NetworkObject)
        transform.SetParent(null, true);

        if (!rb)
        {
            rb = GetComponent<Rigidbody>();
            if (!rb)
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rb.mass = 0.5f;
            }
        }

        // Server does physics
        rb.isKinematic = false;
        rb.useGravity = true;

        // Find the owning player object
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(ownerClientId, out var client))
        {
            followTarget = client.PlayerObject.transform;
        }
        else
        {
            Debug.LogWarning("[SERVER] DetachAndFollow: could not find player for ownerClientId=" + ownerClientId);
            return;
        }

        localOffset = Random.insideUnitSphere * 0.6f;
        state.Value = State.FollowingPlayer;
    }

    /// <summary>
    /// Called when entering the balloon area: fly to the balloon and get absorbed.
    /// Server only.
    /// </summary>
    public void GoToBalloon(NetworkBalloonLift lift)
    {
        if (!IsServer) return;
        if (state.Value == State.Absorbed) return;
        if (!lift || !lift.anchor) return;

        followTarget = lift.anchor;
        absorbedAnchor = lift.anchor;
        absorbedOffset = Random.insideUnitSphere * 0.25f;

        state.Value = State.ToBalloon;
        StartCoroutine(AbsorbWhenClose(lift));
    }

    // ─────────────────────────────── absorb coroutine ───────────────────────────────

    System.Collections.IEnumerator AbsorbWhenClose(NetworkBalloonLift lift)
    {
        while (IsServer && state.Value == State.ToBalloon && followTarget)
        {
            if (Vector3.Distance(transform.position, followTarget.position) < 0.35f)
            {
                state.Value = State.Absorbed;

#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
                rb.isKinematic = true;
                rb.useGravity = false;

                // Instead of parenting (which NGO forbids to non-NetworkObject parents),
                // just snap to our anchor + offset once:
                if (absorbedAnchor)
                    transform.position = absorbedAnchor.position + absorbedOffset;

                if (absorbPoofPrefab)
                    SpawnAbsorbPoofClientRpc(transform.position);

                lift.AbsorbOrb(this);
                yield break;
            }

            yield return null;
        }
    }

    // ─────────────────────────────── RPCs ───────────────────────────────

    [ClientRpc]
    void SpawnShotPoofClientRpc(Vector3 pos, Vector3 normal)
    {
        if (shotPoofPrefab)
            Instantiate(shotPoofPrefab, pos, Quaternion.LookRotation(normal)).Play();
    }

    [ClientRpc]
    void SpawnAbsorbPoofClientRpc(Vector3 pos)
    {
        if (absorbPoofPrefab)
            Instantiate(absorbPoofPrefab, pos, Quaternion.identity).Play();
    }

    // ─────────────────────────────── movement ───────────────────────────────

    void FixedUpdate()
    {
        if (!IsServer || rb == null) return;

        if (state.Value == State.FollowingPlayer && followTarget)
        {
            Vector3 desiredPos = followTarget.position + localOffset;
            Vector3 to = desiredPos - transform.position;

#if UNITY_6000_0_OR_NEWER
            Vector3 desiredVel = to.normalized * followSpeed;
            Vector3 steerForce = (desiredVel - rb.linearVelocity) * steering;
            rb.AddForce(steerForce, ForceMode.Acceleration);

            // Keep a little distance from the player
            Vector3 flatTo = Vector3.ProjectOnPlane(followTarget.position - transform.position, Vector3.up);
            if (flatTo.magnitude < keepDistance)
                rb.AddForce(-flatTo.normalized * steering, ForceMode.Acceleration);

            if (rb.linearVelocity.magnitude > maxSpeed)
                rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
#else
            Vector3 desiredVel = to.normalized * followSpeed;
            Vector3 steerForce = (desiredVel - rb.velocity) * steering;
            rb.AddForce(steerForce, ForceMode.Acceleration);

            Vector3 flatTo = Vector3.ProjectOnPlane(followTarget.position - transform.position, Vector3.up);
            if (flatTo.magnitude < keepDistance)
                rb.AddForce(-flatTo.normalized * steering, ForceMode.Acceleration);

            if (rb.velocity.magnitude > maxSpeed)
                rb.velocity = rb.velocity.normalized * maxSpeed;
#endif
        }
        else if (state.Value == State.ToBalloon && followTarget)
        {
            Vector3 to = followTarget.position - transform.position;

#if UNITY_6000_0_OR_NEWER
            Vector3 desiredVel = to.normalized * (followSpeed + 2f);
            Vector3 steer = (desiredVel - rb.linearVelocity) * (steering + 2f);
            rb.AddForce(steer, ForceMode.Acceleration);

            float max = maxSpeed + 4f;
            if (rb.linearVelocity.magnitude > max)
                rb.linearVelocity = rb.linearVelocity.normalized * max;
#else
            Vector3 desiredVel = to.normalized * (followSpeed + 2f);
            Vector3 steer      = (desiredVel - rb.velocity) * (steering + 2f);
            rb.AddForce(steer, ForceMode.Acceleration);

            float max = maxSpeed + 4f;
            if (rb.velocity.magnitude > max)
                rb.velocity = rb.velocity.normalized * max;
#endif
        }
        else if (state.Value == State.Absorbed && absorbedAnchor)
        {
            // Keep orb “stuck” to the balloon without changing its parent
            transform.position = absorbedAnchor.position + absorbedOffset;
        }
    }
}
