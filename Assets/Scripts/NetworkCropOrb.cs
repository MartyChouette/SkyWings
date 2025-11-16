using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
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

    [Header("Collected Visuals")]
    public Renderer orbRenderer;          // optional, will auto-find if null
    public Material collectedMaterial;    // slightly transparent
    public bool disableCollidersWhenCollected = true;

    Rigidbody rb;

    Transform followTarget;
    Vector3 localOffset;

    Transform absorbedAnchor;
    Vector3 absorbedOffset;

    // who vacuumed this corn last (used for ammo credit)
    [HideInInspector] public ulong lastCollectorId;

    NetworkVariable<State> state = new NetworkVariable<State>(
        State.Attached,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

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

        if (!orbRenderer)
            orbRenderer = GetComponentInChildren<Renderer>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!rb)
            rb = GetComponent<Rigidbody>();

        if (IsServer)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            state.Value = State.Attached;
        }
        else
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    // ─────────── called by server from Weapon / Vacuum ───────────

    public void DetachAndFollow(ulong ownerClientId, Vector3 impactPoint, Vector3 impactNormal)
    {
        if (!IsServer) return;

        if (state.Value != State.Attached && state.Value != State.FollowingPlayer)
            return;

        lastCollectorId = ownerClientId;

        if (shotPoofPrefab)
            SpawnShotPoofClientsRpc(impactPoint, impactNormal);

        transform.SetParent(null, true);

        if (!rb)
            rb = GetComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(ownerClientId, out var client))
        {
            followTarget = client.PlayerObject.transform;
        }
        else
        {
            Debug.LogWarning($"[SERVER] DetachAndFollow: owner {ownerClientId} not found.");
            return;
        }

        localOffset = Random.insideUnitSphere * 0.6f;
        state.Value = State.FollowingPlayer;
    }

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

                if (absorbedAnchor)
                    transform.position = absorbedAnchor.position + absorbedOffset;

                if (absorbPoofPrefab)
                    SpawnAbsorbPoofClientsRpc(transform.position);

                // mark visuals as collected on all clients (transparent, no collisions)
                SetCollectedVisualsClientsRpc();

                // tell the balloon it absorbed us (this also handles ammo credit)
                lift.AbsorbOrb(this);

                yield break;
            }

            yield return null;
        }
    }

    // ─────────── RPCs ───────────

    [Rpc(SendTo.ClientsAndHost)]
    void SpawnShotPoofClientsRpc(Vector3 pos, Vector3 normal, RpcParams rpcParams = default)
    {
        if (shotPoofPrefab)
            Instantiate(shotPoofPrefab, pos, Quaternion.LookRotation(normal)).Play();
    }

    [Rpc(SendTo.ClientsAndHost)]
    void SpawnAbsorbPoofClientsRpc(Vector3 pos, RpcParams rpcParams = default)
    {
        if (absorbPoofPrefab)
            Instantiate(absorbPoofPrefab, pos, Quaternion.identity).Play();
    }

    [Rpc(SendTo.ClientsAndHost)]
    void SetCollectedVisualsClientsRpc(RpcParams rpcParams = default)
    {
        if (disableCollidersWhenCollected)
        {
            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;
        }

        if (orbRenderer && collectedMaterial)
            orbRenderer.material = collectedMaterial;
    }

    // ─────────── movement (server-authoritative) ───────────

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
            transform.position = absorbedAnchor.position + absorbedOffset;
        }
    }
}
