using Unity.Netcode;
using UnityEngine;

public class StalkSpawner : NetworkBehaviour
{
    public GameObject stalkPrefab;

    public void SpawnStalk(Vector3 pos, Quaternion rot)
    {
        if (!IsServer) return;

        var go = Instantiate(stalkPrefab, pos, rot);
        go.GetComponent<NetworkObject>().Spawn();
    }
}
