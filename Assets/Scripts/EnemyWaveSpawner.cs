using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class EnemyWaveSpawner : NetworkBehaviour
{
    [System.Serializable]
    public class Wave
    {
        public GameObject enemyPrefab;
        public int count = 5;
        public float spawnRadius = 15f;
        public float spawnInterval = 0.5f;
    }

    public Wave[] gatherPhaseWaves;
    public Wave[] flightPhaseWaves;

    public float timeBetweenGatherWaves = 8f;
    public float timeBetweenFlightWaves = 6f;

    bool _flightPhase;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer) return;

        StartCoroutine(GatherPhaseLoop());
    }

    public void BeginFlightPhase()
    {
        if (!IsServer) return;
        _flightPhase = true;
        StopAllCoroutines();
        StartCoroutine(FlightPhaseLoop());
    }

    IEnumerator GatherPhaseLoop()
    {
        while (!_flightPhase)
        {
            foreach (var wave in gatherPhaseWaves)
            {
                yield return SpawnWave(wave);
            }
            yield return new WaitForSeconds(timeBetweenGatherWaves);
        }
    }

    IEnumerator FlightPhaseLoop()
    {
        while (_flightPhase)
        {
            foreach (var wave in flightPhaseWaves)
            {
                yield return SpawnWave(wave);
            }
            yield return new WaitForSeconds(timeBetweenFlightWaves);
        }
    }

    IEnumerator SpawnWave(Wave wave)
    {
        if (!wave.enemyPrefab) yield break;

        for (int i = 0; i < wave.count; i++)
        {
            Vector2 circle = Random.insideUnitCircle * wave.spawnRadius;
            Vector3 pos = transform.position + new Vector3(circle.x, 4f, circle.y); // 4 units up

            var go = Instantiate(wave.enemyPrefab, pos, Quaternion.identity);
            var no = go.GetComponent<NetworkObject>();
            if (no) no.Spawn(true);

            yield return new WaitForSeconds(wave.spawnInterval);
        }
    }
}
