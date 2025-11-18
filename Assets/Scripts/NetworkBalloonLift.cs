using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// Balloon lift that charges by orbs, only moves when all *alive* players
/// are physically riding it, and drops if orbs hit zero while flying.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class NetworkBalloonLift : NetworkBehaviour
{
    public enum LiftState : byte
    {
        Idle,
        Charging,
        Lifting
    }

    [Header("Scene Refs")]
    public Transform anchor;

    [Header("Charging")]
    [Min(1)]
    public int liftThreshold = 10;
    public ParticleSystem gatherBurstPrefab;

    [Header("Path Movement")]
    public Transform[] stops;
    public float moveSpeed = 4f;
    public float arriveDistance = 0.1f;

    [Header("Visuals")]
    public Renderer[] balloonRenderers;
    public Color chargingColor = Color.white;
    public Color chargedColor = Color.yellow;

    [Header("Drop Behaviour")]
    public float dropGravityMultiplier = 1f;

    // ------------------- Network -------------------

    public NetworkVariable<int> OrbCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<LiftState> State = new NetworkVariable<LiftState>(
        LiftState.Charging,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> RiderCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ------------------- Internals -------------------

    private Rigidbody _rb;
    private bool _isDropping;
    private int _currentStopIndex = 0;

    // Server only: track which players are physically riding
    private readonly HashSet<ulong> _riders = new HashSet<ulong>();

    // ------------------- Init -------------------

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;

            if (stops != null && stops.Length > 0 && stops[0] != null)
            {
                transform.position = stops[0].position;
            }
        }

        OrbCount.OnValueChanged += OnOrbCountChanged;
        State.OnValueChanged += OnStateChanged;

        SetChargedVisual(OrbCount.Value >= liftThreshold);
    }

    void OnDestroy()
    {
        OrbCount.OnValueChanged -= OnOrbCountChanged;
        State.OnValueChanged -= OnStateChanged;
    }

    // ------------------- Rider API -------------------

    // These are what BalloonLiftRiderZone is calling
    public void RegisterRider(ulong clientId)
    {
        if (!IsServer) return;

        if (_riders.Add(clientId))
            RiderCount.Value = _riders.Count;
    }

    public void UnregisterRider(ulong clientId)
    {
        if (!IsServer) return;

        if (_riders.Remove(clientId))
            RiderCount.Value = _riders.Count;
    }

    // Optional RPC wrappers if you ever need to call from a client-side trigger
    [ServerRpc(RequireOwnership = false)]
    public void RegisterRiderServerRpc(ulong clientId)
    {
        RegisterRider(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void UnregisterRiderServerRpc(ulong clientId)
    {
        UnregisterRider(clientId);
    }

    // ------------------- Orb & Thief Logic -------------------

    public void AbsorbOrb(NetworkCropOrb orb)
    {
        if (!IsServer) return;

        if (gatherBurstPrefab && orb != null)
            SpawnGatherBurstClientRpc(orb.transform.position);

        OrbCount.Value = Mathf.Clamp(OrbCount.Value + 1, 0, 9999);
    }

    public void ServerStealOrbs(int amount)
    {
        if (!IsServer) return;

        int newVal = Mathf.Max(0, OrbCount.Value - amount);
        OrbCount.Value = newVal;
    }

    // Move to next stop – call this from your game manager when charged
    public void ServerBeginMoveToNextStop()
    {
        if (!IsServer) return;

        if (stops == null || stops.Length == 0)
        {
            Debug.LogWarning("[Lift] No stops assigned, cannot move.");
            return;
        }

        if (_currentStopIndex >= stops.Length)
            _currentStopIndex = stops.Length - 1;

        State.Value = LiftState.Lifting;
        _isDropping = false;

        _rb.isKinematic = true;
        _rb.useGravity = false;
    }

    // ------------------- Movement -------------------

    void FixedUpdate()
    {
        if (!IsServer) return;
        if (_isDropping) return;
        if (State.Value != LiftState.Lifting) return;

        // Require enough orbs
        if (OrbCount.Value < liftThreshold)
            return;

        // Require all alive players to be on the lift
        int alive = GetAlivePlayerCount();
        if (alive > 0 && RiderCount.Value < alive)
            return;

        MoveTowardStop();
    }

    void MoveTowardStop()
    {
        if (stops == null || stops.Length == 0)
            return;

        if (_currentStopIndex < 0 || _currentStopIndex >= stops.Length)
            return;

        Transform target = stops[_currentStopIndex];
        if (target == null) return;

        Vector3 next = Vector3.MoveTowards(
            transform.position,
            target.position,
            moveSpeed * Time.fixedDeltaTime
        );

        _rb.MovePosition(next);

        if (Vector3.Distance(next, target.position) <= arriveDistance)
        {
            ReachStop();
        }
    }

    void ReachStop()
    {
        Debug.Log($"[Lift] Reached stop {_currentStopIndex}");

        _rb.MovePosition(stops[_currentStopIndex].position);

        State.Value = LiftState.Charging;
        OrbCount.Value = 0;
        _isDropping = false;

        _rb.isKinematic = true;
        _rb.useGravity = false;

        if (_currentStopIndex < stops.Length - 1)
            _currentStopIndex++;
    }

    // ------------------- Alive Player Counting -------------------

    int GetAlivePlayerCount()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return 0;

        int aliveCount = 0;

        foreach (var kvp in nm.ConnectedClients)
        {
            var obj = kvp.Value.PlayerObject;
            if (obj == null) continue;

            var health = obj.GetComponent<NetworkPlayerHealth>();
            if (health != null && health.currentHealth.Value > 0f)
                aliveCount++;
        }

        return aliveCount;
    }

    // ------------------- Drop Logic -------------------

    void StartDrop()
    {
        if (!IsServer) return;
        _isDropping = true;

        State.Value = LiftState.Idle;
        _rb.isKinematic = false;
        _rb.useGravity = true;

        if (dropGravityMultiplier > 1f)
        {
            _rb.AddForce(Vector3.down * 9.81f * (dropGravityMultiplier - 1f),
                         ForceMode.Acceleration);
        }

        Debug.Log("[Lift] DROPPING — Out of orbs during flight!");
    }

    // ------------------- Visuals -------------------

    void OnOrbCountChanged(int oldValue, int newValue)
    {
        bool charged = newValue >= liftThreshold;
        SetChargedVisual(charged);

        if (IsServer && State.Value == LiftState.Lifting && newValue <= 0)
            StartDrop();
    }

    void OnStateChanged(LiftState oldState, LiftState newState)
    {
        // hook for future state-based effects
    }

    void SetChargedVisual(bool charged)
    {
        if (balloonRenderers == null) return;

        Color c = charged ? chargedColor : chargingColor;

        foreach (var r in balloonRenderers)
        {
            if (r != null)
                r.material.color = c;
        }
    }

    // ------------------- VFX -------------------

    [Rpc(SendTo.ClientsAndHost)]
    void SpawnGatherBurstClientRpc(Vector3 pos)
    {
        if (gatherBurstPrefab)
            Instantiate(gatherBurstPrefab, pos, Quaternion.identity).Play();
    }
}
