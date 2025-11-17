using UnityEngine;
using Unity.Netcode;

/// Trigger zone on top of the balloon platform that parents / unparents
/// players so they move with the lift instead of sliding off, and lets
/// the lift know how many riders it currently has.
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
            playerNO.TrySetParent(liftNO, true);

            // Register this rider with the lift so it knows not to move empty.
            lift.RegisterRider(playerNO.OwnerClientId);
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
            playerNO.TrySetParent((Transform)null, true);

            // Let the lift know this rider is gone.
            lift.UnregisterRider(playerNO.OwnerClientId);
        }
    }
}