using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EnemySpawnEntry
{
    public GameObject prefab;

    [Tooltip("Enemy never spawns before this wave")]
    public int minWave = 1;

    [Tooltip("Enemy never spawns after this wave. 0 = never stops.")]
    public int maxWave = 0;

    [Tooltip("Spawn weight on the first wave this enemy appears")]
    public float startWeight = 1f;

    [Tooltip("Spawn weight once the ramp is finished")]
    public float endWeight = 1f;

    [Tooltip("Wave at which the weight reaches End Weight. It then stays there.")]
    public int endWave = 20;

    public float GetWeight(int wave)
    {
        if (prefab == null || wave < minWave) return 0f;
        if (maxWave > 0 && wave > maxWave) return 0f;

        float t = Mathf.InverseLerp(minWave, endWave, wave);
        return Mathf.Max(0f, Mathf.Lerp(startWeight, endWeight, t));
    }
}

[CreateAssetMenu(fileName = "NewEnemySpawnData", menuName = "Scriptable Objects/Enemy Spawn Data")]
public class EnemySpawnData : ScriptableObject
{
    public List<EnemySpawnEntry> enemies = new List<EnemySpawnEntry>();

    public float timeBetweenSpawns;
    public float spawnRate;
    public int startSpawnAmount;
    public float spawnMultiplier;

    [Header("Adaptive Difficulty")]
    public bool adaptiveDifficulty = true;

    [Tooltip("Towers the player is expected to have on wave 1")]
    public float expectedStartTowers = 1f;

    [Tooltip("Extra towers expected for each wave after that")]
    public float expectedTowersPerWave = 1f;

    [Tooltip("Average seconds an enemy should survive after spawning. Killing faster than this counts as ahead.")]
    public float targetEnemyLifetime = 20f;

    [Range(0f, 1f), Tooltip("0 = only kill speed matters, 1 = only tower count matters")]
    public float towerInfluence = 0.5f;

    [Range(0f, 1f), Tooltip("How fast skill reacts to the latest wave. 1 = instantly, lower = more gradual")]
    public float smoothing = 0.6f;

    [Tooltip("Skill is clamped to this range. 1 is neutral, so keep min below 1 and max above 1")]
    public float minSkill = 0.5f;
    public float maxSkill = 2f;

    [Tooltip("Enemy count multiplier at the lowest and highest skill")]
    public float minCountFactor = 0.7f;
    public float maxCountFactor = 1.5f;

    [Tooltip("How many waves ahead (strong player) or behind (struggling player) the enemy mix can shift")]
    public int maxWaveShift = 4;
}