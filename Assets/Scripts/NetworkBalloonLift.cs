using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkBalloonLift : NetworkBehaviour
{
    public Transform anchor;              // where orbs stick (basket)
    public int liftThreshold = 10;        // total orbs required before lift
    public float forcePerOrb = 35f;
    public float baseDamping = 2f;
    public float maxUpForce = 6000f;
    public ParticleSystem gatherBurstPrefab;

    Rigidbody rb;

    // replicated orb count (for UI)
    public NetworkVariable<int> OrbCount = new NetworkVariable<int>(0);

    // balloon state: grounded vs lifting/flying
    public enum LiftState : byte { Grounded, Charging, Lifting }
    public NetworkVariable<LiftState> State = new NetworkVariable<LiftState>(LiftState.Charging);

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (!anchor)
        {
            var a = new GameObject("BalloonAnchor");
            a.transform.SetParent(transform, false);
            a.transform.localPosition = Vector3.zero;
            anchor = a.transform;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            OrbCount.Value = 0;
            State.Value = LiftState.Charging;
        }
    }

    // server-only
    public void AbsorbOrb(NetworkCropOrb orb)
    {
        if (!IsServer) return;

        OrbCount.Value++;

        if (gatherBurstPrefab && anchor)
            SpawnGatherBurstClientRpc(anchor.position);

        if (State.Value == LiftState.Charging && OrbCount.Value >= liftThreshold)
        {
            // you could start a countdown here instead of instant lift
            State.Value = LiftState.Lifting;
        }
    }

    [ClientRpc]
    void SpawnGatherBurstClientRpc(Vector3 pos)
    {
        if (gatherBurstPrefab)
            Instantiate(gatherBurstPrefab, pos, Quaternion.identity).Play();
    }

    void FixedUpdate()
    {
        if (!IsServer || rb == null) return;

        if (State.Value != LiftState.Lifting) return;

        int extraOrbs = Mathf.Max(0, OrbCount.Value - (liftThreshold - 1));
        float upForce = Mathf.Min(maxUpForce, extraOrbs * forcePerOrb);

        if (extraOrbs > 0)
        {
            rb.AddForce(Vector3.up * upForce, ForceMode.Force);
        }

        float damping = baseDamping * rb.mass * Mathf.Max(0f, rb.linearVelocity.y);
        rb.AddForce(Vector3.down * damping, ForceMode.Force);
    }
}
