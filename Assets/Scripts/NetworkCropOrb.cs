using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkCropOrb : NetworkBehaviour
{
    public enum State : byte { Attached, FollowingPlayer, ToBalloon, Absorbed }

    [Header("Follow Tuning")]
    public float followSpeed = 8f;
    public float steering = 6f;
    public float maxSpeed = 10f;
    public float keepDistance = 1.2f;

    [Header("VFX")]
    public ParticleSystem shotPoofPrefab;
    public ParticleSystem absorbPoofPrefab;

    Rigidbody rb;
    Transform followTarget;
    Vector3 localOffset;

    NetworkVariable<State> state = new NetworkVariable<State>(State.Attached);

    void Awake()
    {
        var col = GetComponent<SphereCollider>();
        col.isTrigger = false;

        rb = GetComponent<Rigidbody>();
        if (!rb)
            rb = gameObject.AddComponent<Rigidbody>();

#if UNITY_6000_0_OR_NEWER
        rb.linearDamping = 1f;
        rb.angularDamping = 0.5f;
#else
        rb.drag = 1f;
        rb.angularDrag = 0.5f;
#endif
        rb.mass = 0.5f;
        rb.isKinematic = false;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            state.Value = State.Attached;
        }
    }

    // server only
    public void DetachAndFollow(ulong ownerClientId, Vector3 impactPoint, Vector3 impactNormal)
    {
        if (!IsServer) return;
        if (state.Value != State.Attached && state.Value != State.FollowingPlayer) return;

        if (shotPoofPrefab)
            SpawnShotPoofClientRpc(impactPoint, impactNormal);

        transform.SetParent(null, true);

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
        rb.isKinematic = false;

        var playerObj = NetworkManager.Singleton.ConnectedClients[ownerClientId].PlayerObject;
        if (!playerObj) return;

        var player = playerObj.GetComponent<NetworkPlayer>();
        if (!player) return;

        followTarget = player.transform;
        localOffset = Random.insideUnitSphere * 0.6f;
        state.Value = State.FollowingPlayer;
    }

    [ClientRpc]
    void SpawnShotPoofClientRpc(Vector3 pos, Vector3 normal)
    {
        if (shotPoofPrefab)
            Instantiate(shotPoofPrefab, pos, Quaternion.LookRotation(normal)).Play();
    }

    // balloon side:
    public void GoToBalloon(NetworkBalloonLift lift)
    {
        if (!IsServer) return;
        if (state.Value == State.Absorbed) return;

        followTarget = lift.anchor;
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
                transform.SetParent(lift.anchor, true);
                transform.localPosition = Random.insideUnitSphere * 0.25f;

                if (absorbPoofPrefab)
                    SpawnAbsorbPoofClientRpc(transform.position);

                lift.AbsorbOrb(this);
                yield break;
            }
            yield return null;
        }
    }

    [ClientRpc]
    void SpawnAbsorbPoofClientRpc(Vector3 pos)
    {
        if (absorbPoofPrefab)
            Instantiate(absorbPoofPrefab, pos, Quaternion.identity).Play();
    }

    void FixedUpdate()
    {
        if (!IsServer || rb == null) return;

        if (state.Value == State.FollowingPlayer && followTarget)
        {
            Vector3 desiredPos = followTarget.position + localOffset;
            Vector3 to = desiredPos - transform.position;
            Vector3 desiredVel = to.normalized * followSpeed;

#if UNITY_6000_0_OR_NEWER
            Vector3 steer = (desiredVel - rb.linearVelocity) * steering;
            rb.AddForce(steer, ForceMode.Acceleration);

            Vector3 flatTo = Vector3.ProjectOnPlane(followTarget.position - transform.position, Vector3.up);
            if (flatTo.magnitude < keepDistance)
                rb.AddForce(-flatTo.normalized * steering, ForceMode.Acceleration);

            float speed = rb.linearVelocity.magnitude;
            if (speed > maxSpeed)
                rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
#else
            Vector3 steer = (desiredVel - rb.velocity) * steering;
            rb.AddForce(steer, ForceMode.Acceleration);

            Vector3 flatTo = Vector3.ProjectOnPlane(followTarget.position - transform.position, Vector3.up);
            if (flatTo.magnitude < keepDistance)
                rb.AddForce(-flatTo.normalized * steering, ForceMode.Acceleration);

            float speed = rb.velocity.magnitude;
            if (speed > maxSpeed)
                rb.velocity = rb.velocity.normalized * maxSpeed;
#endif
        }
        else if (state.Value == State.ToBalloon && followTarget)
        {
            Vector3 to = (followTarget.position - transform.position);
            Vector3 desiredVel = to.normalized * (followSpeed + 2f);

#if UNITY_6000_0_OR_NEWER
            Vector3 steer = (desiredVel - rb.linearVelocity) * (steering + 2f);
            rb.AddForce(steer, ForceMode.Acceleration);

            float max = maxSpeed + 4f;
            if (rb.linearVelocity.magnitude > max)
                rb.linearVelocity = rb.linearVelocity.normalized * max;
#else
            Vector3 steer = (desiredVel - rb.velocity) * (steering + 2f);
            rb.AddForce(steer, ForceMode.Acceleration);

            float max = maxSpeed + 4f;
            if (rb.velocity.magnitude > max)
                rb.velocity = rb.velocity.normalized * max;
#endif
        }
    }
}
