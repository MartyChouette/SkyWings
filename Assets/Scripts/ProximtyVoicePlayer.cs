using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;

using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;

/// <summary>
/// On the local owner:
/// - Initializes Unity Services + Vivox
/// - Logs in anonymously
/// - Joins one positional channel
/// - Periodically sends 3D position into Vivox
/// </summary>
public class ProximityVoicePlayer : NetworkBehaviour
{
    [Header("Vivox Channel")]
    [Tooltip("Vivox positional channel name")]
    public string channelName = "cornfield";

    [Header("3D Voice Settings")]
    public float audibleDistance = 32f;
    public float conversationalDistance = 2f;
    public float audioFadeIntensity = 1f;
    public float positionUpdateInterval = 0.3f;

    [Header("Listener Override (optional)")]
    [Tooltip("If set, this transform is used as the listener (usually your camera). If null, uses this GameObject.")]
    public Transform listenerOverride;

    [Header("Debug")]
    [Tooltip("Print detailed Vivox/debug logs to the Console.")]
    public bool debugLogging = true;

    GameObject _listener;
    bool _vivoxReady;
    bool _joinedChannel;
    bool _hasSentPosition;
    float _nextUpdateTime;

    // ───────────────────────────────── helpers ─────────────────────────────────

    void Log(string msg)
    {
        if (debugLogging)
            Debug.Log($"[ProximityVoicePlayer] {msg}");
    }

    void LogWarning(string msg)
    {
        if (debugLogging)
            Debug.LogWarning($"[ProximityVoicePlayer] {msg}");
    }

    void LogError(string msg, Exception ex = null)
    {
        if (debugLogging)
        {
            if (ex != null)
                Debug.LogError($"[ProximityVoicePlayer] {msg}\n{ex}");
            else
                Debug.LogError($"[ProximityVoicePlayer] {msg}");
        }
    }

    // ───────────────────────────────── lifecycle ─────────────────────────────────

    public override async void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
            return;

        _listener = listenerOverride != null ? listenerOverride.gameObject : gameObject;

        Log("Owner spawned, starting Vivox init…");

        await InitVivoxAndJoinAsync();
    }

    public override async void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (!IsOwner)
            return;

        if (!_joinedChannel)
            return;

        var svc = VivoxService.Instance;
        if (svc == null)
            return;

        try
        {
            var channels = svc.ActiveChannels;
            if (channels != null && channels.ContainsKey(channelName))
            {
                Log($"Leaving channel '{channelName}' on despawn…");
                await svc.LeaveChannelAsync(channelName);
            }
        }
        catch (Exception ex)
        {
            LogWarning($"Error leaving channel on despawn: {ex.Message}");
        }
        finally
        {
            _joinedChannel = false;
        }
    }

    // ───────────────────────────────── Init & join ─────────────────────────────────

    async Task InitVivoxAndJoinAsync()
    {
        try
        {
            // 1. Unity Services
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                Log("Initializing Unity Services…");
                await UnityServices.InitializeAsync();
                Log("Unity Services initialized.");
            }
            else
            {
                Log("Unity Services already initialized.");
            }

            // 2. Authentication
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Log("Signing in anonymously…");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Log($"Signed in. PlayerId = {AuthenticationService.Instance.PlayerId}");
            }
            else
            {
                Log($"Already signed in. PlayerId = {AuthenticationService.Instance.PlayerId}");
            }

            // 3. Vivox init
            Log("Initializing VivoxService…");
            await VivoxService.Instance.InitializeAsync();
            Log("VivoxService initialized.");

            // 4. Login
            if (!VivoxService.Instance.IsLoggedIn)
            {
                var options = new LoginOptions
                {
                    DisplayName = "Player-" + AuthenticationService.Instance.PlayerId
                };
                Log($"Logging into Vivox as '{options.DisplayName}'…");
                await VivoxService.Instance.LoginAsync(options);
                Log("Vivox login successful.");
            }
            else
            {
                Log("Vivox already logged in.");
            }

            _vivoxReady = true;

            // 5. Join positional channel
            int audible = Mathf.Max(1, Mathf.RoundToInt(audibleDistance));
            int conversational = Mathf.Clamp(Mathf.RoundToInt(conversationalDistance), 0, audible);

            var props = new Channel3DProperties(
                audible,
                conversational,
                audioFadeIntensity,
                AudioFadeModel.InverseByDistance
            );

            Log($"Joining positional channel '{channelName}' (audible={audible}, conversational={conversational})…");

            await VivoxService.Instance.JoinPositionalChannelAsync(
                channelName,
                ChatCapability.AudioOnly,
                props,
                null
            );

            _joinedChannel = true;
            Log($"*** VIVOX READY *** Joined positional channel '{channelName}'.");
        }
        catch (Exception ex)
        {
            _vivoxReady = false;
            _joinedChannel = false;
            LogError("Vivox init/join FAILED.", ex);
        }
    }

    // ───────────────────────────────── per-frame position ─────────────────────────────────

    void Update()
    {
        if (!IsOwner)
            return;

        if (_listener == null)
            return;

        if (!_vivoxReady || !_joinedChannel)
            return; // we never successfully joined, so don't call Set3DPosition

        if (Time.time < _nextUpdateTime)
            return;

        var svc = VivoxService.Instance;
        if (svc == null || !svc.IsLoggedIn)
            return;

        var channels = svc.ActiveChannels;
        if (channels == null || !channels.ContainsKey(channelName))
        {
            // Channel dropped or we were kicked; stop sending positions
            if (_joinedChannel)
                LogWarning($"Channel '{channelName}' no longer in ActiveChannels – stopping position updates.");
            _joinedChannel = false;
            return;
        }

        _nextUpdateTime = Time.time + positionUpdateInterval;

        try
        {
            svc.Set3DPosition(_listener, channelName, true);

            if (!_hasSentPosition)
            {
                _hasSentPosition = true;
                Log($"First Set3DPosition sent successfully for channel '{channelName}'. Positional audio live.");
            }
        }
        catch (InvalidOperationException ex)
        {
            // This is the "not currently in any channels" error we were seeing before.
            LogWarning("Set3DPosition threw InvalidOperationException (no channel). Stopping updates.\n" + ex.Message);
            _joinedChannel = false;
        }
        catch (Exception ex)
        {
            LogWarning("Set3DPosition failed: " + ex.Message);
        }
    }
}
