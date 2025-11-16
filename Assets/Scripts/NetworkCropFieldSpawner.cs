using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkCropFieldSpawner : NetworkBehaviour
{
    public NetworkObject cornOrbPrefab;
    public int rows = 5;
    public int cols = 6;
    public float spacing = 2f;
    public float orbHeight = 1.8f;
    public GameObject stalkVisualPrefab;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer) return;   // ✅ server-only spawn

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
        for (int c = 0; c < cols; c++)
        {
            Vector3 basePos = transform.position +
                              new Vector3(c * spacing, 0f, r * spacing);

            // purely visual stalk
            if (stalkVisualPrefab != null)
            {
                Instantiate(stalkVisualPrefab,
                    basePos,
                    Quaternion.identity,
                    transform); // parent under spawner (fine)
            }

            // networked orb
            Vector3 orbPos = basePos + new Vector3(0f, orbHeight, 0f);

            NetworkObject orbNO = Instantiate(cornOrbPrefab, orbPos, Quaternion.identity);
            orbNO.Spawn(true); // ✅ server spawns, all clients see same set
        }
    }
}