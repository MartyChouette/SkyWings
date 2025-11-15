// BalloonArea.cs
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BalloonArea : MonoBehaviour
{
    public BalloonLift lift;

    void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var orb = other.GetComponentInParent<CropOrb>();
        if (orb && (orb.state == CropOrb.State.FollowingPlayer))
        {
            orb.GoToBalloon(lift);
        }
    }
}