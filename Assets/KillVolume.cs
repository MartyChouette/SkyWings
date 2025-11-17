using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class KillVolume : NetworkBehaviour
{
    public float damage = 9999f; // effectively instant kill

    void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var hp = other.GetComponentInParent<NetworkPlayerHealth>();
        if (hp != null)
        {
            hp.ServerTakeDamage(damage);
        }
    }
}