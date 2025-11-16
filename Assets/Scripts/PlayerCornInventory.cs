using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class PlayerCornInventory : NetworkBehaviour
{
    [Header("Ammo (corn kernels)")]
    // This is our ammo count, replicated to everyone
    public NetworkVariable<int> CurrentCorn = new NetworkVariable<int>(0);

    // Optional: max ammo cap (0 = unlimited)
    public int maxAmmo = 0;

    public int Ammo => CurrentCorn.Value;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            CurrentCorn.Value = 0;
        }
    }

    /// <summary>
    /// Server-side: add ammo (corn) to this player.
    /// </summary>
    public void ServerAddAmmo(int amount)
    {
        if (!IsServer || amount <= 0) return;

        int newValue = CurrentCorn.Value + amount;

        if (maxAmmo > 0)
            newValue = Mathf.Min(newValue, maxAmmo);

        CurrentCorn.Value = newValue;
        // Debug.Log($"[SERVER] {name} ammo now {CurrentCorn.Value}");
    }

    /// <summary>
    /// Server-side: consume ammo if available. Returns true if successful.
    /// </summary>
    public bool ServerConsumeAmmo(int amount)
    {
        if (!IsServer || amount <= 0) return false;

        if (CurrentCorn.Value < amount)
            return false;

        CurrentCorn.Value -= amount;
        // Debug.Log($"[SERVER] {name} spent {amount} ammo, now {CurrentCorn.Value}");
        return true;
    }
}