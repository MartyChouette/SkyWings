using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;

public class VivoxBootstrap : MonoBehaviour
{
    public static VivoxBootstrap Instance { get; private set; }

    [Header("Channel")]
    public string positionalChannelName = "cornfield";

    public bool IsReady => _isReady;
    public bool InChannel => _inChannel;

    bool _isReady;
    bool _inChannel;
    bool _joining;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    async void Start()
    {
        await InitAndJoinAsync();
    }

    public async Task InitAndJoinAsync()
    {
        if (_joining) return;
        _joining = true;

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            await VivoxService.Instance.InitializeAsync();

            if (!VivoxService.Instance.IsLoggedIn)
            {
                var options = new LoginOptions
                {
                    DisplayName = "Player-" + Guid.NewGuid().ToString("N").Substring(0, 6)
                };
                await VivoxService.Instance.LoginAsync(options);
            }

            _isReady = true;

            // join positional channel
            Channel3DProperties props = new Channel3DProperties(
                audibleDistance: 32,
                conversationalDistance: 2,
                audioFadeIntensityByDistanceaudio: 1f,
                audioFadeModel: AudioFadeModel.InverseByDistance
            );

            await VivoxService.Instance.JoinPositionalChannelAsync(
                positionalChannelName,
                ChatCapability.TextAndAudio,
                props,
                null
            );

            _inChannel = true;
            Debug.Log("[VivoxBootstrap] Joined positional channel " + positionalChannelName);
        }
        catch (Exception ex)
        {
            Debug.LogError("[VivoxBootstrap] Vivox init/join failed: " + ex);
            _isReady = false;
            _inChannel = false;
        }
        finally
        {
            _joining = false;
        }
    }
}
