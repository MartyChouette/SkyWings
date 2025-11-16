using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// Orchestrates zones, phases, balloon state, and waves.
/// Everything here runs on the server; clients just react via NetworkVariables & RPCs.
/// </summary>
public class NetworkGameManager : NetworkBehaviour
{
    public enum GamePhase : byte
    {
        WaitingForPlayers,
        GatheringCorn,
        BalloonCountdown,
        Flying,
        ZoneComplete,
        GameComplete
    }

    [System.Serializable]
    public class ZoneConfig
    {
        public string name;
        public NetworkBalloonLift balloon;
        public NetworkCropFieldSpawner fieldSpawner;
        public EnemyWaveSpawner waveSpawner;
        public Transform balloonFlightTarget; // where balloon flies to in this zone
        public float flightDuration = 15f;    // seconds to reach target
        public int requiredOrbCount = 10;
    }

    [Header("Zones (3 for now)")]
    public List<ZoneConfig> zones = new List<ZoneConfig>();

    [Header("Phase timing")]
    public float countdownDuration = 5f;     // after threshold met
    public float zoneCompleteDelay = 4f;     // before we advance zones

    public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.WaitingForPlayers);
    public NetworkVariable<int> CurrentZoneIndex = new NetworkVariable<int>(0);
    public NetworkVariable<float> PhaseTimer = new NetworkVariable<float>(0);

    bool _initialized;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer) return;

        if (zones.Count == 0)
        {
            Debug.LogError("NetworkGameManager: no zones configured.");
            return;
        }

        // setup all zones, first one active
        for (int i = 0; i < zones.Count; i++)
            SetupZoneServer(i, i == 0);

        Phase.Value = GamePhase.GatheringCorn;
        CurrentZoneIndex.Value = 0;
        PhaseTimer.Value = 0;
        _initialized = true;
    }

    void SetupZoneServer(int index, bool active)
    {
        var z = zones[index];
        if (z.balloon != null)
        {
            z.balloon.liftThreshold = z.requiredOrbCount;
            z.balloon.State.Value = NetworkBalloonLift.LiftState.Charging;
            z.balloon.OrbCount.Value = 0;
        }

        if (z.fieldSpawner != null)
            z.fieldSpawner.enabled = active;

        if (z.waveSpawner != null)
            z.waveSpawner.enabled = active;
    }

    void Update()
    {
        if (!IsServer || !_initialized) return;

        PhaseTimer.Value += Time.deltaTime;

        switch (Phase.Value)
        {
            case GamePhase.GatheringCorn:
                TickGathering();
                break;

            case GamePhase.BalloonCountdown:
                TickCountdown();
                break;

            case GamePhase.Flying:
                TickFlying();
                break;

            case GamePhase.ZoneComplete:
                TickZoneComplete();
                break;
        }
    }

    ZoneConfig CurrentZone => zones[Mathf.Clamp(CurrentZoneIndex.Value, 0, zones.Count - 1)];

    void TickGathering()
    {
        var zone = CurrentZone;
        if (!zone.balloon) return;

        // when balloon reaches threshold, start countdown
        if (zone.balloon.OrbCount.Value >= zone.requiredOrbCount &&
            zone.balloon.State.Value == NetworkBalloonLift.LiftState.Charging)
        {
            Phase.Value = GamePhase.BalloonCountdown;
            PhaseTimer.Value = 0;
            CountdownStartedClientRpc(CurrentZoneIndex.Value, countdownDuration);
        }
    }

    void TickCountdown()
    {
        if (PhaseTimer.Value >= countdownDuration)
        {
            StartFlyingPhase();
        }
    }

    void StartFlyingPhase()
    {
        var zone = CurrentZone;
        if (zone.balloon)
            zone.balloon.State.Value = NetworkBalloonLift.LiftState.Lifting;

        if (zone.waveSpawner)
            zone.waveSpawner.BeginFlightPhase();

        Phase.Value = GamePhase.Flying;
        PhaseTimer.Value = 0;

        BalloonTakeoffClientRpc(CurrentZoneIndex.Value);
    }

    void TickFlying()
    {
        var zone = CurrentZone;
        if (!zone.balloon || !zone.balloonFlightTarget)
        {
            // if no specific flight path, just time out
            if (PhaseTimer.Value >= zone.flightDuration)
                Phase.Value = GamePhase.ZoneComplete;
            return;
        }

        // simple straight-line interpolation to target over flightDuration
        float t = Mathf.Clamp01(PhaseTimer.Value / zone.flightDuration);
        Vector3 startPos = zone.balloon.transform.position;
        Vector3 targetPos = zone.balloonFlightTarget.position;

        // only set position for server; NetworkTransform will sync
        zone.balloon.transform.position =
            Vector3.Lerp(startPos, targetPos, t);

        if (PhaseTimer.Value >= zone.flightDuration)
        {
            Phase.Value = GamePhase.ZoneComplete;
            PhaseTimer.Value = 0;
            ZoneArrivedClientRpc(CurrentZoneIndex.Value);
        }
    }

    void TickZoneComplete()
    {
        if (PhaseTimer.Value < zoneCompleteDelay) return;

        int next = CurrentZoneIndex.Value + 1;
        if (next >= zones.Count)
        {
            Phase.Value = GamePhase.GameComplete;
            GameCompleteClientRpc();
            return;
        }

        // disable previous zone spawners
        if (CurrentZone.fieldSpawner) CurrentZone.fieldSpawner.enabled = false;
        if (CurrentZone.waveSpawner) CurrentZone.waveSpawner.enabled = false;

        CurrentZoneIndex.Value = next;
        SetupZoneServer(next, true);

        Phase.Value = GamePhase.GatheringCorn;
        PhaseTimer.Value = 0;
        NextZoneStartedClientRpc(next);
    }

    // --- CLIENT EVENTS (for UI, SFX, etc.) ---

    [ClientRpc]
    void CountdownStartedClientRpc(int zoneIndex, float seconds)
    {
        // TODO: show "Balloon lifting in X seconds" UI
        Debug.Log($"[Client] Countdown started in zone {zoneIndex}, {seconds}s.");
    }

    [ClientRpc]
    void BalloonTakeoffClientRpc(int zoneIndex)
    {
        Debug.Log($"[Client] Balloon takeoff in zone {zoneIndex}!");
        // TODO: play big whoosh, camera shake, etc.
    }

    [ClientRpc]
    void ZoneArrivedClientRpc(int zoneIndex)
    {
        Debug.Log($"[Client] Arrived at zone {zoneIndex}.");
    }

    [ClientRpc]
    void NextZoneStartedClientRpc(int zoneIndex)
    {
        Debug.Log($"[Client] Zone {zoneIndex} started (GatheringCorn).");
    }

    [ClientRpc]
    void GameCompleteClientRpc()
    {
        Debug.Log("[Client] Game complete! GG.");
        // TODO: end screen
    }
}
