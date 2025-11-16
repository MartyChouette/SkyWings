using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
public class NetworkEnemyBase : NetworkBehaviour
{
    public float maxHealth = 10f;
    public float moveSpeed = 6f;
    public float hoverHeight = 3f;
    public float damagePerHit = 10f;
    public float attackCooldown = 1.5f;
    public float attackRange = 3f;

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

    // called by server when shot
    public void ServerApplyDamage(float dmg)
    {
        if (!IsServer) return;
        if (_health <= 0) return;

        _health -= dmg;
        if (_health <= 0)
        {
            _health = 0;
            Die();
        }
    }

    void Die()
    {
        if (deathVfx)
            SpawnDeathVfxClientRpc(transform.position);

        if (audioSource && deathClip)
            audioSource.PlayOneShot(deathClip);

        GetComponent<NetworkObject>().Despawn();
    }

    [ClientRpc]
    void SpawnDeathVfxClientRpc(Vector3 pos)
    {
        if (deathVfx)
            Instantiate(deathVfx, pos, Quaternion.identity).Play();
    }
}
