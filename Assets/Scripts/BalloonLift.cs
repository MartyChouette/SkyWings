// BalloonLift.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BalloonLift : MonoBehaviour
{
    public Transform anchor;              // where orbs stick (basket)
    public int orbCount = 0;
    public int liftThreshold = 5;         // starts floating at 5
    public float forcePerOrb = 35f;       // extra Newtons per orb (after threshold)
    public float baseDamping = 2f;        // vertical damping
    public float maxUpForce = 6000f;
    public ParticleSystem gatherBurstPrefab; // assign in Inspector

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (!anchor)
        {
            var a = new GameObject("BalloonAnchor");
            a.transform.SetParent(transform, false);
            a.transform.localPosition = Vector3.zero;
            anchor = a.transform;
        }
    }

    // BalloonLift.cs (additions)
    
    public void AbsorbOrb(CropOrb orb)
    {
        orbCount++;
        if (gatherBurstPrefab && anchor)
            Instantiate(gatherBurstPrefab, anchor.position, Quaternion.identity).Play();
    }


    void FixedUpdate()
    {
        if (!rb) return;

        float extra = Mathf.Max(0, orbCount - (liftThreshold - 1)); // 0 until we reach threshold
        float upForce = Mathf.Min(maxUpForce, extra * forcePerOrb);

        // Apply buoyancy only when threshold reached
        if (extra > 0)
        {
            rb.AddForce(Vector3.up * upForce, ForceMode.Force);
        }

        // simple vertical damping for stability
        float damping = baseDamping * rb.mass * Mathf.Max(0f, rb.linearVelocity.y);
        rb.AddForce(Vector3.down * damping, ForceMode.Force);
    }


}