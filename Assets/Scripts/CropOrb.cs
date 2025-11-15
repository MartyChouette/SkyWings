// CropOrb.cs
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class CropOrb : MonoBehaviour
{
    public enum State { Attached, FollowingPlayer, ToBalloon, Absorbed }
    public State state = State.Attached;

    [Header("Follow Tuning")]
    public float followSpeed = 8f;
    public float steering = 6f;
    public float maxSpeed = 10f;
    public float keepDistance = 1.2f;

    [Header("VFX")]
    public ParticleSystem shotPoofPrefab;     // when detached by shot
    public ParticleSystem absorbPoofPrefab;   // when absorbed into balloon

    Rigidbody rb;
    Transform target;
    Vector3 localOffset;

    public void DetachAndFollow(Transform player, Vector3 impactPoint, Vector3 impactNormal)
    {
        if (state != State.Attached && state != State.FollowingPlayer) return;

        // 🔸 SHOT POOF
        if (shotPoofPrefab)
            Instantiate(shotPoofPrefab, impactPoint, Quaternion.LookRotation(impactNormal)).Play();

        transform.SetParent(null, true);
        if (!rb)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.5f; rb.linearDamping = 1f; rb.angularDamping = 0.5f;
        }

        target = player;
        localOffset = Random.insideUnitSphere * 0.6f;
        state = State.FollowingPlayer;
    }

    public void GoToBalloon(BalloonLift lift)
    {
        if (state == State.Absorbed) return;
        target = lift.anchor;
        state = State.ToBalloon;
        StartCoroutine(AbsorbWhenClose(lift));
    }

    System.Collections.IEnumerator AbsorbWhenClose(BalloonLift lift)
    {
        while (state == State.ToBalloon && target)
        {
            if (Vector3.Distance(transform.position, target.position) < 0.35f)
            {
                state = State.Absorbed;
                if (rb) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }
                transform.SetParent(lift.anchor, true);
                transform.localPosition = Random.insideUnitSphere * 0.25f;

                // 🔸 ABSORB POOF
                if (absorbPoofPrefab)
                    Instantiate(absorbPoofPrefab, transform.position, Quaternion.identity).Play();

                lift.AbsorbOrb(this);
                yield break;
            }
            yield return null;
        }
    }

    void Awake()
    {
        var col = GetComponent<SphereCollider>();
        col.isTrigger = false;
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        if (state == State.FollowingPlayer && target)
        {
            Vector3 desiredPos = target.position + localOffset;
            Vector3 to = desiredPos - transform.position;
            Vector3 desiredVel = to.normalized * followSpeed;
            Vector3 steerForce = (desiredVel - rb.linearVelocity) * steering;
            rb.AddForce(steerForce, ForceMode.Acceleration);

            Vector3 flatTo = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
            if (flatTo.magnitude < keepDistance) rb.AddForce(-flatTo.normalized * steering, ForceMode.Acceleration);

            if (rb.linearVelocity.magnitude > maxSpeed) rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
        }
        else if (state == State.ToBalloon && target)
        {
            Vector3 to = (target.position - transform.position);
            Vector3 desiredVel = to.normalized * (followSpeed + 2f);
            Vector3 steer = (desiredVel - rb.linearVelocity) * (steering + 2f);
            rb.AddForce(steer, ForceMode.Acceleration);
            if (rb.linearVelocity.magnitude > maxSpeed + 4f) rb.linearVelocity = rb.linearVelocity.normalized * (maxSpeed + 4f);
        }
    }
}
