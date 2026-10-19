using UnityEngine;

// Measures how well the player is doing and decides how hard the next wave should be
public class DifficultyDirector
{
    private readonly EnemySpawnData _data;

    private float _skill = 1f;
    private float _lifetimeSum;
    private int _killCount;

    public float Skill => _skill;
    public float CountFactor { get; private set; } = 1f;
    public int WaveShift { get; private set; }
    public float LastAverageLifetime { get; private set; }

    public DifficultyDirector(EnemySpawnData data)
    {
        _data = data;
    }

    // Called every time an enemy dies, with how long it survived
    public void RecordKill(float lifetime)
    {
        _lifetimeSum += lifetime;
        _killCount++;
    }

    // Called at the start of each wave. Uses the kills recorded during the previous wave.
    public void Evaluate(int wave, int towerCount)
    {
        if (!_data.adaptiveDifficulty)
        {
            ResetKillStats();
            return;
        }

        // Tower score: towers owned vs towers expected by this wave
        float expectedTowers = Mathf.Max(1f, _data.expectedStartTowers + _data.expectedTowersPerWave * (wave - 1));
        float towerScore = ClampSkill(towerCount / expectedTowers);

        // Kill score: faster than the target lifetime = above 1. No data yet (wave 1) = neutral.
        float killScore = 1f;
        if (_killCount > 0)
        {
            LastAverageLifetime = _lifetimeSum / _killCount;
            killScore = ClampSkill(_data.targetEnemyLifetime / Mathf.Max(LastAverageLifetime, 0.1f));
        }

        float rawSkill = Mathf.Lerp(killScore, towerScore, _data.towerInfluence);
        _skill = ClampSkill(Mathf.Lerp(_skill, rawSkill, _data.smoothing));

        // -1 = struggling as much as allowed, +1 = doing as well as allowed
        float bias = _skill >= 1f
            ? Mathf.InverseLerp(1f, _data.maxSkill, _skill)
            : -Mathf.InverseLerp(1f, _data.minSkill, _skill);

        CountFactor = bias >= 0f
            ? Mathf.Lerp(1f, _data.maxCountFactor, bias)
            : Mathf.Lerp(1f, _data.minCountFactor, -bias);

        WaveShift = Mathf.RoundToInt(bias * _data.maxWaveShift);

        ResetKillStats();
    }

    private float ClampSkill(float value)
    {
        return Mathf.Clamp(value, _data.minSkill, _data.maxSkill);
    }

    private void ResetKillStats()
    {
        _lifetimeSum = 0f;
        _killCount = 0;
    }
}