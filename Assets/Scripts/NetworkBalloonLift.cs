using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]   // so motion is visible on clients
public class NetworkBalloonLift : NetworkBehaviour
{
    public Transform anchor;              // where orbs stick (basket)
    public int liftThreshold = 10;        // total orbs required before lift
    public float forcePerOrb = 35f;
    public float baseDamping = 2f;
    public float maxUpForce = 6000f;
    public ParticleSystem gatherBurstPrefab;

    [Header("Visuals")]
    public Renderer balloonRenderer;
    public Material chargingMaterial;
    public Material liftingMaterial;
    public ParticleSystem fullyChargedFx;
    public AudioSource fullyChargedAudio;

    Rigidbody rb;

    public NetworkVariable<int> OrbCount = new NetworkVariable<int>(0);

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

        if (!balloonRenderer)
            balloonRenderer = GetComponentInChildren<Renderer>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            OrbCount.Value = 0;
            State.Value = LiftState.Charging;
        }

        // set initial material based on state
        ApplyStateVisuals(State.Value);
        State.OnValueChanged += OnStateChanged;
    }

    void OnDestroy()
    {
        State.OnValueChanged -= OnStateChanged;
    }

    void OnStateChanged(LiftState oldState, LiftState newState)
    {
        ApplyStateVisuals(newState);
    }

    void ApplyStateVisuals(LiftState state)
    {
        if (!balloonRenderer) return;

        switch (state)
        {
            case LiftState.Charging:
                if (chargingMaterial)
                    balloonRenderer.material = chargingMaterial;
                break;
            case LiftState.Lifting:
                if (liftingMaterial)
                    balloonRenderer.material = liftingMaterial;
                break;
        }
    }

    /// <summary>
    /// Server-only: called by NetworkCropOrb when it reaches the anchor.
    /// Also responsible for giving ammo to the collecting player.
    /// </summary>
    public void AbsorbOrb(NetworkCropOrb orb)
    {
        if (!IsServer) return;
        if (orb == null) return;

        OrbCount.Value++;

        // give +1 ammo to whoever collected this orb
        if (NetworkManager.Singleton != null)
        {
            ulong ownerId = orb.lastCollectorId;
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(ownerId, out var client))
            {
                var inv = client.PlayerObject.GetComponent<PlayerCornInventory>();
                if (inv != null)
                    inv.ServerAddAmmo(1);
            }
        }

        if (gatherBurstPrefab && anchor)
            SpawnGatherBurstClientsRpc(anchor.position);

        if (State.Value == LiftState.Charging && OrbCount.Value >= liftThreshold)
        {
            State.Value = LiftState.Lifting;
            OnFullyChargedClientsRpc();   // visual cue when we start lifting
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    void SpawnGatherBurstClientsRpc(Vector3 pos, RpcParams rpcParams = default)
    {
        if (gatherBurstPrefab)
            Instantiate(gatherBurstPrefab, pos, Quaternion.identity).Play();
    }

    [Rpc(SendTo.ClientsAndHost)]
    void OnFullyChargedClientsRpc(RpcParams rpcParams = default)
    {
        // particle / sound cue for everyone when we cross the threshold
        if (fullyChargedFx)
            fullyChargedFx.Play();

        if (fullyChargedAudio)
            fullyChargedAudio.Play();

        // also update visuals here in case late-joining clients missed OnValueChanged
        ApplyStateVisuals(LiftState.Lifting);
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
