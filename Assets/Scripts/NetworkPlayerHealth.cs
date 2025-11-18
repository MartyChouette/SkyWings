using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerHealth : NetworkBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;

    // Synced health (server writes, everyone reads)
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(0f);

    [Header("Knockback")]
    [Tooltip("Default knockback if none is provided by the attacker.")]
    public float defaultKnockbackForce = 6f;

    [Tooltip("Optional respawn point; if null, uses starting position.")]
    public Transform respawnPoint;

    Rigidbody _rb;
    Vector3 _spawnPos;
    Quaternion _spawnRot;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _spawnPos = transform.position;
        _spawnRot = transform.rotation;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (currentHealth.Value <= 0f)
                currentHealth.Value = maxHealth;
        }
    }

    // =====================================================================
    // DAMAGE API
    // =====================================================================

    /// <summary>
    /// Legacy signature – no knockback information.
    /// You can keep calling this anywhere you already do.
    /// </summary>
    public void ServerTakeDamage(float damage)
    {
        ServerTakeDamage(damage, Vector3.zero, 0f);
    }

    /// <summary>
    /// Preferred version: apply damage + knockback.
    /// MUST be called on the server.
    /// </summary>
    public void ServerTakeDamage(float damage, Vector3 hitDirection, float knockbackForceOverride = 0f)
    {
        if (!IsServer) return;
        if (damage <= 0f) return;

        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - damage);

        float force = knockbackForceOverride > 0f ? knockbackForceOverride : defaultKnockbackForce;

        if (force > 0f && hitDirection.sqrMagnitude > 0.0001f)
        {
            KnockbackClientRpc(hitDirection.normalized, force);
        }

        if (currentHealth.Value <= 0f)
        {
            HandleDeathServer();
        }
    }

    // =====================================================================
    // DEATH / RESPAWN
    // =====================================================================

    void HandleDeathServer()
    {
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        DeathClientRpc();

        float respawnDelay = 1.5f;
        Invoke(nameof(RespawnServer), respawnDelay);
    }

    void RespawnServer()
    {
        if (!IsServer) return;

        currentHealth.Value = maxHealth;

        Vector3 pos = respawnPoint ? respawnPoint.position : _spawnPos;
        Quaternion rot = respawnPoint ? respawnPoint.rotation : _spawnRot;

        TeleportClientRpc(pos, rot);
    }

    // =====================================================================
    // RPCs
    // =====================================================================

    [ClientRpc]
    void KnockbackClientRpc(Vector3 dir, float force)
    {
        if (_rb == null) return;
        _rb.AddForce(dir * force, ForceMode.VelocityChange);
    }

    [ClientRpc]
    void TeleportClientRpc(Vector3 pos, Quaternion rot)
    {
        transform.position = pos;
        transform.rotation = rot;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    [ClientRpc]
    void DeathClientRpc()
    {
        // TODO: play death animation / VFX / sound here.
    }
}
