using UnityEngine;
using Unity.Netcode;

/// In-scene server spawner that creates a grid of corn orbs.
[RequireComponent(typeof(NetworkObject))]
public class NetworkCropFieldSpawner : NetworkBehaviour
{
    [Header("Corn Prefab (NetworkObject at root)")]
    public NetworkObject cornOrbPrefab;   // assign in inspector

    [Header("Grid")]
    public int rows = 5;
    public int cols = 6;
    public float spacing = 2f;
    public float orbHeight = 1.8f;        // Y position for the orb

    [Header("Optional: visual stalk")]
    public GameObject stalkVisualPrefab;  // OPTIONAL, no NetworkObject

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer) return;            // only server spawns corn

        SpawnField();
    }

    void SpawnField()
    {
        if (cornOrbPrefab == null)
        {
            Debug.LogError("NetworkCropFieldSpawner: cornOrbPrefab is NOT assigned.");
            return;
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Vector3 basePos = transform.position +
                                  new Vector3(c * spacing, 0f, r * spacing);

                // Optional purely-visual stalk (no NetworkObject!)
                if (stalkVisualPrefab != null)
                {
                    Instantiate(stalkVisualPrefab,
                        basePos,
                        Quaternion.identity,
                        transform); // parent for organization only
                }

                // Networked orb
                Vector3 orbPos = basePos + new Vector3(0f, orbHeight, 0f);

                NetworkObject orbNO = Instantiate(cornOrbPrefab, orbPos, Quaternion.identity);
                orbNO.Spawn(true); // spawn with observers
            }
        }
    }
}