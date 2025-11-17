using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Networked balloon lift that charges from absorbed corn orbs and then
/// moves along a path of stops (islands / final area).
/// Only moves while ALL connected players are riding it.
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

    [Header("Scene refs")]
    [Tooltip("Where orbs home to when flying toward the balloon.")]
    public Transform anchor;

    [Header("Charging")]
    [Tooltip("How many orbs are required to move to the next stop.")]
    public int liftThreshold = 10;

    [Tooltip("Optional burst VFX each time an orb is absorbed.")]
    public ParticleSystem gatherBurstPrefab;

    [Header("Path movement")]
    [Tooltip("Ordered stops the balloon will travel to. Index 0 is the starting island.")]
    public Transform[] stops;

    [Tooltip("Units per second when flying between stops.")]
    public float moveSpeed = 4f;

    [Tooltip("How close to the stop before snapping and finishing.")]
    public float arriveDistance = 0.1f;

    [Header("Visuals")]
    [Tooltip("Renderers whose material color will change when fully charged.")]
    public Renderer[] balloonRenderers;
    public Color chargingColor = Color.white;
    public Color chargedColor = Color.yellow;

    // ───────── Network state ─────────

    public NetworkVariable<int> OrbCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<LiftState> State = new NetworkVariable<LiftState>(
        LiftState.Charging, // first zone starts in Charging
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // How many riders are currently on the lift (server authority)
    public NetworkVariable<int> RiderCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    Rigidbody _rb;
    int _currentStopIndex;

    // Track which clientIds are riding (server only)
    readonly HashSet<ulong> _riderClients = new HashSet<ulong>();

    // ─────────────────────────────── lifecycle ───────────────────────────────

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

            // Snap to first stop if set
            if (stops != null && stops.Length > 0 && stops[0] != null)
            {
                _currentStopIndex = 0;
                transform.position = stops[0].position;
            }
        }

        OrbCount.OnValueChanged += OnOrbCountChanged;
        State.OnValueChanged += OnStateChanged;

        // Initialize visuals
        OnOrbCountChanged(0, OrbCount.Value);
        OnStateChanged(State.Value, State.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        OrbCount.OnValueChanged -= OnOrbCountChanged;
        State.OnValueChanged -= OnStateChanged;
        _riderClients.Clear();
    }

    // ─────────────────────────────── rider API (called by BalloonLiftRiderZone) ───────────────────────────────

    public void RegisterRider(ulong clientId)
    {
        if (!IsServer) return;

        if (_riderClients.Add(clientId))
        {
            RiderCount.Value = _riderClients.Count;
            // Debug.Log($"[BalloonLift] Rider joined. Count={RiderCount.Value}");
        }
    }

    public void UnregisterRider(ulong clientId)
    {
        if (!IsServer) return;

        if (_riderClients.Remove(clientId))
        {
            RiderCount.Value = _riderClients.Count;
            // Debug.Log($"[BalloonLift] Rider left. Count={RiderCount.Value}");
        }
    }

    // ─────────────────────────────── charging API ───────────────────────────────

    /// <summary>
    /// Called by NetworkCropOrb when an orb reaches the balloon.
    /// </summary>
    public void AbsorbOrb(NetworkCropOrb orb)
    {
        if (!IsServer) return;

        if (gatherBurstPrefab && orb != null)
            SpawnGatherBurstClientRpc(orb.transform.position);

        OrbCount.Value++;

        if (OrbCount.Value >= liftThreshold && State.Value != LiftState.Lifting)
        {
            BeginMoveToNextStop();
        }
    }

    void BeginMoveToNextStop()
    {
        if (stops == null || stops.Length == 0)
        {
            Debug.LogWarning("[BalloonLift] No stops assigned, cannot move.");
            return;
        }

        int next = Mathf.Min(_currentStopIndex + 1, stops.Length - 1);
        if (next == _currentStopIndex)
        {
            Debug.Log("[BalloonLift] Already at final stop; ignoring charge.");
            return;
        }

        _currentStopIndex = next;
        State.Value = LiftState.Lifting;
        Debug.Log($"[BalloonLift] Fully charged → moving to stop #{_currentStopIndex} ({stops[_currentStopIndex].name})");
    }

    // ─────────────────────────────── movement ───────────────────────────────

    void FixedUpdate()
    {
        if (!IsServer) return;
        if (State.Value != LiftState.Lifting) return;
        if (stops == null || stops.Length == 0) return;

        // Require all connected players to be on the lift
        int totalPlayers = GetConnectedPlayerCount();

        // For debugging, uncomment:
        // Debug.Log($"[BalloonLift] Riders={RiderCount.Value}, Players={totalPlayers}");

        if (totalPlayers == 0)
            return; // nothing to do

        if (RiderCount.Value < totalPlayers)
            return; // not everyone is on the lift yet

        Transform target = stops[_currentStopIndex];
        if (!target) return;

        Vector3 currentPos = _rb.position;
        Vector3 targetPos = target.position;

        Vector3 nextPos = Vector3.MoveTowards(
            currentPos,
            targetPos,
            moveSpeed * Time.fixedDeltaTime);

        _rb.MovePosition(nextPos);

        if (Vector3.Distance(nextPos, targetPos) <= arriveDistance)
        {
            _rb.MovePosition(targetPos);
            OnReachedStop();
        }
    }

    void OnReachedStop()
    {
        Debug.Log($"[BalloonLift] Reached stop #{_currentStopIndex}");

        OrbCount.Value = 0;
        State.Value = LiftState.Charging;

        // If you want special logic on the final island:
        // if (_currentStopIndex == stops.Length - 1) { ... win condition ... }
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    /// <summary>
    /// Counts how many real player objects are currently connected.
    /// Adjust the component type if your player script is named differently.
    /// </summary>
    int GetConnectedPlayerCount()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            return 0;

        int count = 0;

        foreach (var kvp in nm.ConnectedClients)
        {
            var client = kvp.Value;
            if (client.PlayerObject != null &&
                client.PlayerObject.GetComponent<NetworkPlayer>() != null)
            {
                count++;
            }
        }

        return count;
    }

    // ─────────────────────────────── visuals ───────────────────────────────

    void OnOrbCountChanged(int oldValue, int newValue)
    {
        bool charged = newValue >= liftThreshold;
        SetChargedVisual(charged);
    }

    void OnStateChanged(LiftState oldState, LiftState newState)
    {
        // extra per-state visuals could go here
    }

    void SetChargedVisual(bool charged)
    {
        if (balloonRenderers == null) return;

        Color targetColor = charged ? chargedColor : chargingColor;

        foreach (var r in balloonRenderers)
        {
            if (!r) continue;
            var mat = r.material;
            mat.color = targetColor;
        }
    }

    // ─────────────────────────────── VFX RPC ───────────────────────────────

    [Rpc(SendTo.ClientsAndHost)]
    void SpawnGatherBurstClientRpc(Vector3 pos, RpcParams rpcParams = default)
    {
        if (gatherBurstPrefab)
        {
            Instantiate(gatherBurstPrefab, pos, Quaternion.identity).Play();
        }
    }
}
