using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private EnemySpawnData data;
    [SerializeField] private bool logAdaptation = true;

    public int spawnAmount; // Base amount that grows every wave, before adaptation
    public List<Vector3> spawnPoints = new List<Vector3>();
    public List<GameObject> currentEnemies = new List<GameObject>();
    private List<SplineContainer> _pathSplineContainers = new List<SplineContainer>();

    private int _waveNumber;
    private int _waveSize;      // Actual number of enemies in the current wave
    private bool _isSpawning;

    private DifficultyDirector _director;
    private readonly Dictionary<GameObject, float> _spawnTimes = new Dictionary<GameObject, float>();

    public static EnemySpawner Instance;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Only spawn enemies after terrain is generated
        MarchingCubes.Instance.OnTerrainGenerated += Initialize;
    }

    private void OnDestroy()
    {
        if (MarchingCubes.Instance != null)
            MarchingCubes.Instance.OnTerrainGenerated -= Initialize;
    }

    private void Initialize()
    {
        spawnPoints = MarchingCubes.Instance.GetPathEndPositions();
        _pathSplineContainers = MarchingCubes.Instance.GetPathSplineContainers();
        spawnAmount = data.startSpawnAmount;
        _director = new DifficultyDirector(data);
        StartCoroutine(SpawnEnemiesCoroutine());

        MarchingCubes.Instance.OnTerrainGenerated -= Initialize;
    }

    // Adapts to the player, picks the wave's enemies, then spawns them at the path starts
    private IEnumerator SpawnEnemies()
    {
        _isSpawning = true;
        _waveNumber++;
        GameManager.Instance.IncrementWaveNumber();

        int towerCount = FindObjectsByType<Tower>(FindObjectsSortMode.None).Length;
        _director.Evaluate(_waveNumber, towerCount);

        _waveSize = Mathf.Max(1, Mathf.RoundToInt(spawnAmount * _director.CountFactor));
        int effectiveWave = Mathf.Max(1, _waveNumber + _director.WaveShift);

        if (logAdaptation)
        {
            Debug.Log($"Wave {_waveNumber}: towers {towerCount}, avg enemy lifetime {_director.LastAverageLifetime:F1}s, " +
                      $"skill {_director.Skill:F2}, enemies {_waveSize} (base {spawnAmount}), mix as wave {effectiveWave}");
        }

        List<GameObject> wave = BuildWave(_waveSize, effectiveWave);
        float delay = data.spawnRate / spawnPoints.Count;

        for (int i = 0; i < wave.Count; i++)
        {
            // Spread enemies evenly across the paths
            int point = i % spawnPoints.Count;

            GameObject newEnemy = Instantiate(wave[i], spawnPoints[point], Quaternion.identity);
            newEnemy.GetComponent<SplineAnimate>().Container = _pathSplineContainers[point];
            currentEnemies.Add(newEnemy);
            _spawnTimes[newEnemy] = Time.time;

            if (SfxPlayer.Instance != null)
                SfxPlayer.Instance.PlayCaveSpawn(spawnPoints[point]);

            yield return new WaitForSeconds(delay);
        }

        _isSpawning = false;
        HUDManager.Instance.UpdateWaveMeterSlider(currentEnemies.Count, currentEnemies.Count);
    }

    // Rolls a weighted random enemy for every slot in the wave
    private List<GameObject> BuildWave(int size, int waveNumber)
    {
        List<GameObject> wave = new List<GameObject>(size);

        for (int i = 0; i < size; i++)
            wave.Add(PickEnemy(waveNumber));

        return wave;
    }

    private GameObject PickEnemy(int waveNumber)
    {
        float totalWeight = 0f;
        foreach (EnemySpawnEntry entry in data.enemies)
            totalWeight += entry.GetWeight(waveNumber);

        // Misconfigured data: nothing has weight this wave, so fall back to the first enemy
        if (totalWeight <= 0f)
        {
            Debug.LogWarning($"No enemy has any spawn weight on wave {waveNumber}. Check EnemySpawnData.");
            return data.enemies[0].prefab;
        }

        float roll = Random.value * totalWeight;
        GameObject last = null;

        foreach (EnemySpawnEntry entry in data.enemies)
        {
            float weight = entry.GetWeight(waveNumber);
            if (weight <= 0f) continue;

            last = entry.prefab;
            roll -= weight;
            if (roll <= 0f) return entry.prefab;
        }

        return last; // Only reached through floating point rounding
    }

    // Spawns enemy waves every few intervals
    private IEnumerator SpawnEnemiesCoroutine()
    {
        while (true)
        {
            // Grace period - Player has time to mine
            HUDManager.Instance.StartTimer(data.timeBetweenSpawns);
            yield return new WaitForSeconds(data.timeBetweenSpawns);

            HUDManager.Instance.ShowWaveInProgress();
            SetTension(1f);
            if (SfxPlayer.Instance != null) SfxPlayer.Instance.PlayWaveStart();
            StartCoroutine(SpawnEnemies());

            // Waits until the wave has finished spawning AND all enemies are dead
            yield return new WaitUntil(() => !_isSpawning && currentEnemies.Count == 0);
            HUDManager.Instance.UpdateWaveMeterSlider(0, 1);
            SetTension(0f);
            if (SfxPlayer.Instance != null) SfxPlayer.Instance.PlayWaveCleared();

            spawnAmount = Mathf.CeilToInt(spawnAmount * data.spawnMultiplier);
        }
    }

    // Ambience drone and music get more intense during a wave and calm down in the break
    private static void SetTension(float tension)
    {
        if (UnderwaterAmbience.Instance != null)
            UnderwaterAmbience.Instance.tension = tension;

        if (GenerativeMusic.Instance != null)
            GenerativeMusic.Instance.tension = tension;
    }

    // Removes dead enemies from the list and records how long they survived
    public void RemoveEnemy(GameObject enemy)
    {
        if (_spawnTimes.TryGetValue(enemy, out float spawnedAt))
        {
            _director.RecordKill(Time.time - spawnedAt);
            _spawnTimes.Remove(enemy);
        }

        currentEnemies.Remove(enemy);
        HUDManager.Instance.UpdateWaveMeterSlider(currentEnemies.Count, _waveSize);
    }
}