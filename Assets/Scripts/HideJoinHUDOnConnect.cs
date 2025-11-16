using UnityEngine;
using Unity.Netcode;

public class HideJoinHUDOnConnect : MonoBehaviour
{
    [Tooltip("If true, just disables the object; if false, destroys it.")]
    public bool justDisable = true;

    bool _registered;
    bool _hidden;

    void Update()
    {
        if (_hidden) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return; // NetworkManager not created yet – just wait

        // Register callbacks once when NM appears
        if (!_registered)
        {
            nm.OnServerStarted += OnServerStarted;
            nm.OnClientConnectedCallback += OnClientConnected;
            _registered = true;
        }

        // If we’re already connected/hosting when this script runs, hide immediately
        if (nm.IsServer || nm.IsConnectedClient)
        {
            HideNow();
        }
    }

    void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (!_registered || nm == null) return;

        nm.OnServerStarted -= OnServerStarted;
        nm.OnClientConnectedCallback -= OnClientConnected;
    }

    void OnServerStarted()
    {
        // Host case
        HideNow();
    }

    void OnClientConnected(ulong clientId)
    {
        // Only hide on the local machine when its own client connects
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (clientId == nm.LocalClientId)
        {
            HideNow();
        }
    }

    void HideNow()
    {
        if (_hidden) return;
        _hidden = true;

        if (justDisable)
            gameObject.SetActive(false);
        else
            Destroy(gameObject);
    }
}