using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Attach to the NetworkPlayer; responsible for joining the right positional voice channel
/// and updating 3D position for voice.
/// You will need to plug this into your Vivox/Voice library.
/// </summary>
public class ProximityVoicePlayer : NetworkBehaviour
{
    public string mainChannelName = "cornfield";

    public override async void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner) return;

        // PSEUDO-CODE — replace with real Vivox/voice integration
        /*
        await VivoxService.Instance.InitializeAsync();
        await VivoxService.Instance.LoginAsync(OwnerClientId.ToString());
        await VivoxService.Instance.JoinPositionalChannelAsync(mainChannelName, transform);
        */
    }

    void Update()
    {
        if (!IsOwner) return;

        // PSEUDO: if voice SDK needs manual position update:
        // VivoxService.Instance.Update3DPosition(transform.position, transform.forward, Vector3.up);
    }

    public void JoinBalloonChannel()
    {
        if (!IsOwner) return;
        // PSEUDO:
        // VivoxService.Instance.SwitchToChannel("balloon");
    }

    public void ReturnToMainChannel()
    {
        if (!IsOwner) return;
        // PSEUDO:
        // VivoxService.Instance.SwitchToChannel(mainChannelName);
    }
}