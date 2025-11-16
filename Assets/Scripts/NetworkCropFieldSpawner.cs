using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-only corn field spawner that instantiates networked orb prefabs
/// in a grid.
/// </summary>
public class NetworkCropFieldSpawner : NetworkBehaviour
{
    public int rows = 5, cols = 6;
    public float spacing = 2f;

    [Tooltip("Prefab with NetworkObject + NetworkCropOrb")]
    public GameObject cornOrbPrefab;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer) return;

        SpawnField();
    }

    void SpawnField()
    {
        if (!cornOrbPrefab)
        {
            Debug.LogError("NetworkCropFieldSpawner: cornOrbPrefab not assigned.");
            return;
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Vector3 basePos = transform.position + new Vector3(c * spacing, 0, r * spacing);

                GameObject orb = Instantiate(cornOrbPrefab, basePos, Quaternion.identity);
                var no = orb.GetComponent<NetworkObject>();
                if (no == null)
                {
                    Debug.LogError("cornOrbPrefab missing NetworkObject.");
                }
                else
                {
                    no.Spawn(true);
                }
            }
        }
    }
}