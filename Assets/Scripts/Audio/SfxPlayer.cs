using UnityEngine;

// Bakes the procedural sound effects once at startup and plays them through a pool of
// 3D AudioSources. Using a pool (instead of an AudioSource on the enemy/projectile) means a
// sound keeps playing even if the object that triggered it is destroyed that same frame.
//
// Volume is layered: Master (everything, ambience included) x Effects (all sound effects) x the
// individual sound's slider. All sliders can be changed while the game is running.
public class SfxPlayer : MonoBehaviour
{
    [Header("Pool")]
    [SerializeField] private int poolSize = 24;
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 60f;

    [Header("Variation")]
    [SerializeField] private int variantsPerSound = 6;

    [Header("Terrain Digging")]
    [Tooltip("0 = damp, muddy dirt, 1 = dry, crunchy sand. Applied when the game starts")]
    [Range(0f, 1f)] [SerializeField] private float sandDryness = 0.6f;

    [Header("Overall Volume")]
    [Tooltip("Every sound in the game, including the underwater ambience")]
    [Range(0f, 1f)] [SerializeField] private float masterVolume = 1f;
    [Tooltip("All sound effects (not the ambience)")]
    [Range(0f, 1f)] [SerializeField] private float effectsVolume = 0.8f;

    [Header("Mining Volume")]
    [Range(0f, 1f)] [SerializeField] private float terrainDigVolume = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float oreHitVolume = 0.7f;
    [Range(0f, 1f)] [SerializeField] private float resourceDepletedVolume = 0.5f;

    [Header("Tower Volume")]
    [Range(0f, 1f)] [SerializeField] private float bubbleGunVolume = 0.45f;
    [Range(0f, 1f)] [SerializeField] private float harpoonVolume = 0.7f;
    [Range(0f, 1f)] [SerializeField] private float laserVolume = 0.45f;

    [Header("Enemy Volume")]
    [Range(0f, 1f)] [SerializeField] private float enemyDeathVolume = 0.8f;

    public static SfxPlayer Instance;

    // For a settings menu: hook these up to UI sliders
    public float MasterVolume
    {
        get => masterVolume;
        set
        {
            masterVolume = Mathf.Clamp01(value);
            ApplyMasterVolume();
        }
    }

    public float EffectsVolume
    {
        get => effectsVolume;
        set => effectsVolume = Mathf.Clamp01(value);
    }

    private AudioSource[] _pool;
    private int _next;

    private AudioClip[] _bubbles;
    private AudioClip[] _sandDigs;
    private AudioClip[] _oreHits;
    private AudioClip[] _plucks;
    private AudioClip[] _zaps;
    private AudioClip[] _thuds;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        BuildPool();
        BakeSounds();
        ApplyMasterVolume();
    }

    // Lets the master slider work live while tweaking in the Inspector during Play mode
    private void OnValidate()
    {
        if (Application.isPlaying && Instance == this)
            ApplyMasterVolume();
    }

    // The AudioListener's volume scales everything the player hears, so master covers the ambience too
    private void ApplyMasterVolume()
    {
        AudioListener.volume = masterVolume;
    }

    private void BuildPool()
    {
        _pool = new AudioSource[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            GameObject child = new GameObject($"Sfx Voice {i}");
            child.transform.SetParent(transform);

            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;

            _pool[i] = source;
        }
    }

    private void BakeSounds()
    {
        int n = variantsPerSound;

        _bubbles = ProceduralSfx.Bake(n, () => ProceduralSfx.Bubble(ProceduralSfx.RandomRange(1.5f, 5f)));
        _sandDigs = ProceduralSfx.Bake(n, () => ProceduralSfx.SandDig(sandDryness));
        _oreHits = ProceduralSfx.Bake(n, () => ProceduralSfx.Impact(ProceduralSfx.RandomRange(600f, 900f), 0.5f));
        _plucks = ProceduralSfx.Bake(n, () => ProceduralSfx.Pluck(ProceduralSfx.RandomRange(90f, 120f)));
        _zaps = ProceduralSfx.Bake(n, () => ProceduralSfx.Zap(ProceduralSfx.RandomRange(1600f, 2000f), 250f));
        _thuds = ProceduralSfx.Bake(n, () => ProceduralSfx.Thud(ProceduralSfx.RandomRange(55f, 75f)));
    }

    // ---------- Game-facing calls ----------

    // Bubble Gun shots
    public void PlayBubbleBurst(Vector3 position, int count = 4) =>
        PlayBubbles(position, count, bubbleGunVolume);

    // Ores ring with a metallic "clink", terrain gets the soft sand/dirt dig
    public void PlayMineHit(Vector3 position, bool isOre)
    {
        if (isOre)
            Play(Pick(_oreHits), position, oreHitVolume, 0.08f);
        else
            Play(Pick(_sandDigs), position, terrainDigVolume, 0.12f);
    }

    // A resource breaking apart: deep crumble plus a cloud of bubbles
    public void PlayResourceDepleted(Vector3 position)
    {
        Play(Pick(_thuds), position, resourceDepletedVolume, 0.1f, 1.4f);
        PlayBubbles(position, 6, resourceDepletedVolume);
    }

    // Higher tower levels sound slightly higher and punchier
    public void PlayHarpoon(Vector3 position, int level) =>
        Play(Pick(_plucks), position, harpoonVolume, 0.05f, 1f + 0.12f * (level - 1));

    public void PlayLaser(Vector3 position, int level) =>
        Play(Pick(_zaps), position, laserVolume, 0.05f, 1f + 0.1f * (level - 1));

    // size > 1 = bigger enemy = deeper thud
    public void PlayEnemyDeath(Vector3 position, float size = 1f)
    {
        Play(Pick(_thuds), position, enemyDeathVolume, 0.1f, 1f / Mathf.Max(0.25f, size));
        PlayBubbles(position, 3, enemyDeathVolume * 0.4f);
    }

    // ---------- Internals ----------

    // A cluster of bubbles staggered slightly in time so they don't all pop at once
    private void PlayBubbles(Vector3 position, int count, float volume)
    {
        double start = AudioSettings.dspTime;

        for (int i = 0; i < count; i++)
        {
            double delay = i * 0.035 + ProceduralSfx.RandomRange(0f, 0.03f);
            Play(Pick(_bubbles), position, volume, 0.2f, 1f, start + delay);
        }
    }

    private void Play(AudioClip clip, Vector3 position, float volume, float pitchJitter,
        float pitchMultiplier = 1f, double scheduledTime = 0)
    {
        if (clip == null || volume <= 0f || effectsVolume <= 0f) return;

        // Round-robin: the oldest voice gets reused if all are busy
        AudioSource source = _pool[_next];
        _next = (_next + 1) % _pool.Length;

        source.transform.position = position;
        source.clip = clip;
        source.volume = volume * effectsVolume;
        source.pitch = pitchMultiplier * (1f + Random.Range(-pitchJitter, pitchJitter));

        if (scheduledTime > 0)
            source.PlayScheduled(scheduledTime);
        else
            source.Play();
    }

    private static AudioClip Pick(AudioClip[] clips) =>
        clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
}