using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
public class NetworkEnemyBase : NetworkBehaviour
{
    [Header("Stats")]
    public float maxHealth = 10f;
    public float moveSpeed = 6f;
    public float hoverHeight = 3f;
    public float damagePerHit = 10f;
    public float attackCooldown = 1.5f;
    public float attackRange = 3f;

    [Header("VFX / SFX")]
    public ParticleSystem deathVfx;
    public AudioSource audioSource;
    public AudioClip deathClip;

    float _health;
    float _attackCd;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            _health = maxHealth;
        }
    }

    void Update()
    {
        if (!IsServer) return;

        _attackCd -= Time.deltaTime;
        TickAI();
    }

    protected virtual void TickAI()
    {
        // simple "track closest player and hover" behaviour
        var target = GetClosestPlayer();
        if (target == null) return;

        Vector3 targetPos = target.transform.position + Vector3.up * hoverHeight;
        Vector3 to = (targetPos - transform.position);
        Vector3 move = to.normalized * moveSpeed * Time.deltaTime;

        transform.position += move;

        if (move.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(to, Vector3.up);

        float dist = Vector3.Distance(transform.position, target.transform.position);
        if (dist <= attackRange && _attackCd <= 0f)
        {
            _attackCd = attackCooldown;
            var hp = target.GetComponent<NetworkPlayerHealth>();
            if (hp != null)
                hp.ServerTakeDamage(damagePerHit);
        }
    }

    NetworkPlayer GetClosestPlayer()
    {
        NetworkPlayer closest = null;
        float bestDist = float.MaxValue;

        if (NetworkManager.Singleton == null)
            return null;

        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            var po = kvp.Value.PlayerObject;
            if (!po) continue;

            var p = po.GetComponent<NetworkPlayer>();
            if (!p) continue;

            float d = Vector3.SqrMagnitude(p.transform.position - transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                closest = p;
            }
        }

        return closest;
    }

    /// <summary>
    /// Called by server (e.g. from weapon RPC) when this enemy is hit.
    /// </summary>
    public void ServerApplyDamage(float dmg)
    {
        if (!IsServer) return;
        if (_health <= 0f) return;

        _health -= dmg;
        if (_health <= 0f)
        {
            _health = 0f;
            Die();
        }
    }

    void Die()
    {
        // tell everyone to play VFX/SFX at this position
        if (deathVfx || (audioSource && deathClip))
        {
            SpawnDeathEffectsRpc(transform.position);
        }

        // Despawn on the server; NGO replicates that
        GetComponent<NetworkObject>().Despawn();
    }

    // New-style RPC, replaces [ClientRpc]
    [Rpc(SendTo.ClientsAndHost)]
    void SpawnDeathEffectsRpc(Vector3 pos, RpcParams rpcParams = default)
    {
        // VFX
        if (deathVfx)
        {
            var fx = Instantiate(deathVfx, pos, Quaternion.identity);
            fx.Play();
        }

        // SFX – 3D sound at enemy position
        if (deathClip)
        {
            // Option 1: use existing audioSource if it lives on this prefab
            if (audioSource)
            {
                audioSource.transform.position = pos;
                audioSource.clip = deathClip;
                audioSource.Play();
            }
            else
            {
                // Option 2: fire-and-forget AudioSource at pos
                var go = new GameObject("EnemyDeathAudio");
                go.transform.position = pos;
                var src = go.AddComponent<AudioSource>();
                src.spatialBlend = 1f; // 3D
                src.clip = deathClip;
                src.Play();
                Object.Destroy(go, deathClip.length + 0.25f);
            }
        }
    }
}
