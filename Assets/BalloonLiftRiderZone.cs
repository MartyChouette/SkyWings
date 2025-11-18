using UnityEngine;
using Unity.Netcode;

/// Trigger zone on top of the balloon platform that parents / unparents
/// players so they move with the lift instead of sliding off, and keeps
/// RiderCount in sync on the server.
[RequireComponent(typeof(Collider))]
public class BalloonLiftRiderZone : NetworkBehaviour
{
    public NetworkBalloonLift lift;

    void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
        lift = GetComponentInParent<NetworkBalloonLift>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!lift) lift = GetComponentInParent<NetworkBalloonLift>();

        var player = other.GetComponentInParent<NetworkPlayer>();
        if (!player) return;

        var playerNO = player.GetComponent<NetworkObject>();
        var liftNO = lift.GetComponent<NetworkObject>();

        if (playerNO != null && liftNO != null)
        {
            // Parent on the server; NGO will replicate to all clients.
            playerNO.TrySetParent(liftNO, worldPositionStays: true);

            // Tell the lift this client is riding.
            lift.RegisterRider(player.OwnerClientId);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;
        if (!lift) lift = GetComponentInParent<NetworkBalloonLift>();

        var player = other.GetComponentInParent<NetworkPlayer>();
        if (!player) return;

        var playerNO = player.GetComponent<NetworkObject>();
        if (playerNO != null)
        {
            // Detach back to world root.
            playerNO.TrySetParent((Transform)null, worldPositionStays: true);

            // Remove this rider from the lift list.
            lift.UnregisterRider(player.OwnerClientId);
        }
    }
}