using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class BalloonArea : NetworkBehaviour
{
    public NetworkBalloonLift lift;

    void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;   // only the server routes orbs to balloon

        var orb = other.GetComponentInParent<NetworkCropOrb>();
        if (orb != null)
        {
            orb.GoToBalloon(lift);
        }
    }
}