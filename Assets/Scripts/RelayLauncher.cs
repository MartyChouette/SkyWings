using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

// For RelayServerData
using Unity.Networking.Transport.Relay;

public class RelayLauncher : MonoBehaviour
{
    [Header("UI")]
    public Button hostButton;
    public Button joinButton;
    public TMP_InputField joinCodeInput;
    public TMP_Text statusText;
    public TMP_Text hostJoinCodeDisplay;

    bool servicesReady;

    UnityTransport Transport =>
        (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

    async void Start()
    {
        await InitServices();

        if (hostButton != null)
            hostButton.onClick.AddListener(HostGame);
        if (joinButton != null)
            joinButton.onClick.AddListener(JoinGame);
    }

    // ─────────────────────────────────────────────
    // Unity Services init
    // ─────────────────────────────────────────────
    async Task InitServices()
    {
        if (servicesReady) return;

        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            servicesReady = true;
            Log("Relay ready.");
        }
        catch (Exception e)
        {
            Log("Init failed: " + e.Message);
        }
    }

    // ─────────────────────────────────────────────
    // HOST
    // ─────────────────────────────────────────────
    public async void HostGame()
    {
        await InitServices();
        if (!servicesReady) return;

        try
        {
            // 1. Create allocation
            Allocation alloc =
                await RelayService.Instance.CreateAllocationAsync(4);

            // 2. Get join code
            string joinCode =
                await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

            if (hostJoinCodeDisplay != null)
                hostJoinCodeDisplay.text = "Join Code: " + joinCode;

            Log("Hosting. Code: " + joinCode);

            // 3. Convert allocation -> RelayServerData (Unity helper)
            RelayServerData serverData =
                AllocationUtils.ToRelayServerData(alloc, "dtls");

            // 4. Feed into transport
            Transport.SetRelayServerData(serverData);

            // 5. Start host
            if (!NetworkManager.Singleton.StartHost())
            {
                Log("StartHost() failed.");
            }
        }
        catch (Exception e)
        {
            Log("Host error: " + e.Message);
        }
    }

    // ─────────────────────────────────────────────
    // JOIN
    // ─────────────────────────────────────────────
    public async void JoinGame()
    {
        await InitServices();
        if (!servicesReady) return;

        string code = joinCodeInput
            ? joinCodeInput.text.Trim().ToUpperInvariant()
            : "";

        if (string.IsNullOrEmpty(code))
        {
            Log("Enter join code.");
            return;
        }

        try
        {
            // 1. Ask Relay about this join code
            JoinAllocation joinAlloc =
                await RelayService.Instance.JoinAllocationAsync(code);

            Log("JoinAllocation OK. Region: " + joinAlloc.Region);

            // 2. Convert join allocation -> RelayServerData
            RelayServerData serverData =
                AllocationUtils.ToRelayServerData(joinAlloc, "dtls");

            // 3. Feed into transport
            Transport.SetRelayServerData(serverData);

            // 4. Start client
            if (!NetworkManager.Singleton.StartClient())
            {
                Log("StartClient() failed.");
            }
        }
        catch (RelayServiceException e)
        {
            // This is where "join code not found" shows up
            Log("Relay join failed: " + e.Reason + " / " + e.Message);
        }
        catch (Exception e)
        {
            Log("Join error: " + e.Message);
        }
    }

    // ─────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────
    void Log(string msg)
    {
        Debug.Log("[RelayLauncher] " + msg);
        if (statusText != null) statusText.text = msg;
    }
}
