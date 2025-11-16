using UnityEngine;
using Unity.Netcode;

public class NetworkPlayerHealth : NetworkBehaviour
{
    public float maxHealth = 100f;
    public float respawnDelay = 4f;

    public NetworkVariable<float> Health = new NetworkVariable<float>();

    bool _dead;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            Health.Value = maxHealth;
        }
    }

    public void ServerTakeDamage(float dmg)
    {
        if (!IsServer || _dead) return;

        Health.Value = Mathf.Max(0, Health.Value - dmg);
        if (Health.Value <= 0)
        {
            _dead = true;
            DeathClientRpc();
            StartCoroutine(RespawnCR());
        }
    }

    System.Collections.IEnumerator RespawnCR()
    {
        yield return new WaitForSeconds(respawnDelay);

        var cc = GetComponent<CharacterController>();
        if (cc) cc.enabled = false;

        // super simple respawn; swap in your spawn system here
        transform.position = Vector3.zero + Vector3.up * 1.5f;

        if (cc) cc.enabled = true;
        Health.Value = maxHealth;
        _dead = false;
        RespawnClientRpc();
    }

    [ClientRpc]
    void DeathClientRpc()
    {
        // TODO: death VFX, disable input, fade, etc.
        Debug.Log($"Player {OwnerClientId} died.");
    }

    [ClientRpc]
    void RespawnClientRpc()
    {
        // TODO: clear UI, enable input, etc.
        Debug.Log($"Player {OwnerClientId} respawned.");
    }
}