using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class EnemyDamageDealer : NetworkBehaviour
{
    [Header("Damage")]
    public float damage = 10f;

    [Tooltip("Knockback strength applied to the player on hit.")]
    public float knockbackForce = 5f;

    [Tooltip("Seconds between hits on the same player.")]
    public float hitCooldown = 0.8f;

    // clientId -> last hit time
    private readonly Dictionary<ulong, float> _lastHitTime =
        new Dictionary<ulong, float>();

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var hp = other.GetComponentInParent<NetworkPlayerHealth>();
        if (hp == null) return;

        ulong clientId = hp.OwnerClientId;
        float now = NetworkManager.ServerTime.TimeAsFloat;

        if (_lastHitTime.TryGetValue(clientId, out float last) &&
            (now - last) < hitCooldown)
        {
            return; // still on cooldown for this player
        }

        _lastHitTime[clientId] = now;

        Vector3 dir = (other.transform.position - transform.position).normalized;

        // Use the new knockback-aware damage method
        hp.ServerTakeDamage(damage, dir, knockbackForce);
    }
}