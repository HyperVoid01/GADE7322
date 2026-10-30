using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Bakes the procedural sound effects once at startup and plays them through pools of AudioSources:
//  - 3D voices for sounds that happen somewhere in the world (they get quieter with distance)
//  - 2D voices for sounds that are "in your head": UI, alarms, wave horns, your own breathing
// Using pools (instead of an AudioSource on each object) means a sound keeps playing even if the
// object that triggered it is destroyed that same frame.
//
// Volume is layered: Master (everything) x Effects (all sound effects) x the sound's own slider.
// All sliders can be changed while the game is running.
public class SfxPlayer : MonoBehaviour
{
    [Header("Pools")]
    [SerializeField] private int worldVoices = 32;
    [SerializeField] private int uiVoices = 10;
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 60f;

    [Header("Variation")]
    [SerializeField] private int variantsPerSound = 6;

    [Header("Terrain Digging")]
    [Tooltip("0 = damp, muddy dirt, 1 = dry, crunchy sand. Applied when the game starts")]
    [Range(0f, 1f)] [SerializeField] private float sandDryness = 0.6f;

    [Header("Overall Volume")]
    [Tooltip("Every sound in the game, including ambience and music")]
    [Range(0f, 1f)] [SerializeField] private float masterVolume = 1f;
    [Tooltip("All sound effects (not ambience or music)")]
    [Range(0f, 1f)] [SerializeField] private float effectsVolume = 0.8f;

    [Header("Mining Volume")]
    [Range(0f, 1f)] [SerializeField] private float terrainDigVolume = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float oreHitVolume = 0.7f;
    [Range(0f, 1f)] [SerializeField] private float resourceDepletedVolume = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float resourceCollectVolume = 0.3f;
    [Range(0f, 1f)] [SerializeField] private float pickaxeSwingVolume = 0.35f;

    [Header("Tower Volume")]
    [Range(0f, 1f)] [SerializeField] private float bubbleGunVolume = 0.45f;
    [Range(0f, 1f)] [SerializeField] private float projectileHitVolume = 0.4f;
    [Range(0f, 1f)] [SerializeField] private float harpoonVolume = 0.7f;
    [Range(0f, 1f)] [SerializeField] private float harpoonHitVolume = 0.55f;
    [Range(0f, 1f)] [SerializeField] private float laserVolume = 0.45f;
    [Range(0f, 1f)] [SerializeField] private float laserHumVolume = 0.25f;
    [Range(0f, 1f)] [SerializeField] private float towerHitVolume = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float towerDestroyedVolume = 0.8f;
    [Range(0f, 1f)] [SerializeField] private float towerLandVolume = 0.8f;
    [Range(0f, 1f)] [SerializeField] private float mainTowerAlarmVolume = 0.4f;

    [Header("Enemy Volume")]
    [Range(0f, 1f)] [SerializeField] private float enemyHitVolume = 0.35f;
    [Range(0f, 1f)] [SerializeField] private float enemyDeathVolume = 0.8f;
    [Range(0f, 1f)] [SerializeField] private float enemyAttackVolume = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float caveSpawnVolume = 0.5f;

    [Header("Game Flow Volume")]
    [Range(0f, 1f)] [SerializeField] private float waveStartVolume = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float waveClearedVolume = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float countdownVolume = 0.4f;
    [Range(0f, 1f)] [SerializeField] private float gameOverVolume = 0.8f;

    [Header("Building & Economy Volume")]
    [Range(0f, 1f)] [SerializeField] private float purchaseVolume = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float upgradeVolume = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float deniedVolume = 0.4f;

    [Header("Player Volume")]
    [Range(0f, 1f)] [SerializeField] private float footstepVolume = 0.3f;
    [Range(0f, 1f)] [SerializeField] private float jumpLandVolume = 0.45f;
    [Range(0f, 1f)] [SerializeField] private float breathingVolume = 0.25f;

    [Header("UI Volume")]
    [Range(0f, 1f)] [SerializeField] private float uiClickVolume = 0.4f;
    [Range(0f, 1f)] [SerializeField] private float uiHoverVolume = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float menuOpenCloseVolume = 0.4f;

    [Header("World Volume")]
    [Range(0f, 1f)] [SerializeField] private float fishBubblesVolume = 0.25f;
    [Range(0f, 1f)] [SerializeField] private float kelpVolume = 0.2f;

    [Header("Breathing")]
    [SerializeField] private bool enableBreathing = true;
    [Tooltip("Seconds for one full breath while walking. Sprinting breathes faster")]
    [SerializeField] private float breathCycle = 5f;
    [Range(0.3f, 1f)] [SerializeField] private float sprintBreathScale = 0.6f;

    [Header("Throttling")]
    [Tooltip("Minimum time between main tower alarms")]
    [SerializeField] private float alarmInterval = 1.5f;
    [Tooltip("Minimum time between resource collect chimes")]
    [SerializeField] private float collectInterval = 0.12f;

    public static SfxPlayer Instance;

    // ---------- For a settings menu: hook these up to UI sliders ----------

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

    // Read every frame by each LaserHum, so the slider works live
    public float LaserHumVolume => laserHumVolume * effectsVolume;

    // ---------- Internals ----------

    private AudioSource[] _world;
    private AudioSource[] _ui;
    private int _nextWorld;
    private int _nextUi;

    private float _nextAlarmTime;
    private float _nextCollectTime;

    private AudioClip[] _bubbles;
    private AudioClip[] _bigBubbles;
    private AudioClip[] _sandDigs;
    private AudioClip[] _footsteps;
    private AudioClip[] _oreHits;
    private AudioClip[] _plucks;
    private AudioClip[] _harpoonHits;
    private AudioClip[] _zaps;
    private AudioClip[] _thuds;
    private AudioClip[] _softThuds;
    private AudioClip[] _crumbles;
    private AudioClip[] _towerHits;
    private AudioClip[] _bites;
    private AudioClip[] _crackles;
    private AudioClip[] _swings;
    private AudioClip[] _kicks;
    private AudioClip[] _kelpSwishes;
    private AudioClip[] _rumbles;
    private AudioClip[] _inhales;
    private AudioClip[] _exhales;

    private AudioClip _inflate;
    private const float InflateClipLength = 0.6f;
    private AudioClip _harpoonDock;
    private AudioClip _mainTowerAlarm;
    private AudioClip _waveStart;
    private AudioClip _waveCleared;
    private AudioClip _countdownTick;
    private AudioClip _countdownFinal;
    private AudioClip _gameOver;
    private AudioClip _purchase;
    private AudioClip _upgrade;
    private AudioClip _levelUp;
    private AudioClip _denied;
    private AudioClip _collect;
    private AudioClip _uiClick;
    private AudioClip _uiHover;
    private AudioClip _menuOpen;
    private AudioClip _menuClose;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Game over pauses the listener; a new scene (restart / main menu) must undo that
        AudioListener.pause = false;

        _world = BuildPool("World Voice", worldVoices, true);
        _ui = BuildPool("UI Voice", uiVoices, false);
        BakeSounds();
        ApplyMasterVolume();
    }

    private void Start()
    {
        AddButtonSoundsInScene();

        if (enableBreathing)
            StartCoroutine(BreathingRoutine());
    }

    // Lets the master slider work live while tweaking in the Inspector during Play mode
    private void OnValidate()
    {
        if (Application.isPlaying && Instance == this)
            ApplyMasterVolume();
    }

    // The AudioListener's volume scales everything the player hears, so master covers ambience and music too
    private void ApplyMasterVolume()
    {
        AudioListener.volume = masterVolume;
    }

    private AudioSource[] BuildPool(string label, int count, bool is3D)
    {
        AudioSource[] pool = new AudioSource[count];

        for (int i = 0; i < count; i++)
        {
            GameObject child = new GameObject($"{label} {i}");
            child.transform.SetParent(transform);

            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.dopplerLevel = 0f;

            if (is3D)
            {
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = minDistance;
                source.maxDistance = maxDistance;
            }
            else
            {
                source.spatialBlend = 0f;
                // Keeps working while the game over screen has paused everything else
                source.ignoreListenerPause = true;
            }

            pool[i] = source;
        }

        return pool;
    }

    private void BakeSounds()
    {
        int n = variantsPerSound;

        // Water
        _bubbles = ProceduralSfx.Bake(n, () => ProceduralSfx.Bubble(ProceduralSfx.RandomRange(1.5f, 5f)));
        _bigBubbles = ProceduralSfx.Bake(n, () => ProceduralSfx.Bubble(ProceduralSfx.RandomRange(6f, 10f), 0.25f));
        _swings = ProceduralSfx.Bake(n, () => ProceduralSfx.Swoosh(500f, 1600f, 0.2f, 1.5f));
        _kicks = ProceduralSfx.Bake(n, () => ProceduralSfx.Swoosh(250f, 700f, 0.35f, 1.2f));
        _kelpSwishes = ProceduralSfx.Bake(n, () => ProceduralSfx.Swoosh(350f, ProceduralSfx.RandomRange(500f, 800f), 0.8f, 1f));
        _rumbles = ProceduralSfx.Bake(n, () => ProceduralSfx.Rumble(ProceduralSfx.RandomRange(1f, 1.4f)));
        _inhales = ProceduralSfx.Bake(3, () => ProceduralSfx.Breath(true, 1.3f));
        _exhales = ProceduralSfx.Bake(3, () => ProceduralSfx.Breath(false, 1.7f));
        _inflate = ProceduralSfx.Inflate(InflateClipLength);

        // Ground
        _sandDigs = ProceduralSfx.Bake(n, () => ProceduralSfx.SandDig(sandDryness));
        _footsteps = ProceduralSfx.Bake(n, () => ProceduralSfx.SandDig(sandDryness * 0.5f, 0.18f));
        _crumbles = ProceduralSfx.Bake(n, () => ProceduralSfx.SandDig(sandDryness, 0.9f));
        _thuds = ProceduralSfx.Bake(n, () => ProceduralSfx.Thud(ProceduralSfx.RandomRange(55f, 75f)));
        _softThuds = ProceduralSfx.Bake(n, () => ProceduralSfx.Thud(ProceduralSfx.RandomRange(110f, 150f), 0.15f));

        // Hits
        _oreHits = ProceduralSfx.Bake(n, () => ProceduralSfx.Impact(ProceduralSfx.RandomRange(600f, 900f), 0.5f));
        _harpoonHits = ProceduralSfx.Bake(n, () => ProceduralSfx.Impact(ProceduralSfx.RandomRange(260f, 340f), 0.9f, 0.25f));
        _towerHits = ProceduralSfx.Bake(n, () => ProceduralSfx.Impact(ProceduralSfx.RandomRange(130f, 170f), 1f, 0.3f));
        _bites = ProceduralSfx.Bake(n, () => ProceduralSfx.Impact(ProceduralSfx.RandomRange(1000f, 1300f), 1f, 0.1f));
        _harpoonDock = ProceduralSfx.Impact(450f, 0.6f, 0.15f);

        // Weapons
        _plucks = ProceduralSfx.Bake(n, () => ProceduralSfx.Pluck(ProceduralSfx.RandomRange(90f, 120f)));
        _zaps = ProceduralSfx.Bake(n, () => ProceduralSfx.Zap(ProceduralSfx.RandomRange(1600f, 2000f), 250f));
        _crackles = ProceduralSfx.Bake(n, () => ProceduralSfx.Crackle(0.35f, ProceduralSfx.RandomRange(80f, 110f)));

        // Game flow (notes in a minor key: A, C, E, A)
        _mainTowerAlarm = ProceduralSfx.Alarm(ProceduralSfx.Note(-5), ProceduralSfx.Note(0));
        _waveStart = ProceduralSfx.Horn(ProceduralSfx.Note(-33), ProceduralSfx.Note(-31), 1.6f);
        _waveCleared = ProceduralSfx.Arpeggio(new[] { ProceduralSfx.Note(0), ProceduralSfx.Note(4), ProceduralSfx.Note(7), ProceduralSfx.Note(12) }, 0.09f, 0.9f);
        _countdownTick = ProceduralSfx.Click(880f, 0.06f);
        _countdownFinal = ProceduralSfx.Click(1320f, 0.12f);
        _gameOver = ProceduralSfx.Horn(ProceduralSfx.Note(-29), ProceduralSfx.Note(-41), 2.8f);

        // Building & economy
        _purchase = ProceduralSfx.Arpeggio(new[] { ProceduralSfx.Note(3), ProceduralSfx.Note(10) }, 0.08f, 0.6f);
        _upgrade = ProceduralSfx.Arpeggio(new[] { ProceduralSfx.Note(0), ProceduralSfx.Note(3), ProceduralSfx.Note(7) }, 0.07f, 0.6f);
        _levelUp = ProceduralSfx.Arpeggio(new[] { ProceduralSfx.Note(0), ProceduralSfx.Note(3), ProceduralSfx.Note(7), ProceduralSfx.Note(12), ProceduralSfx.Note(15) }, 0.08f, 1.1f);
        _denied = ProceduralSfx.Buzz(110f, 0.3f, 2);
        _collect = ProceduralSfx.Chime(ProceduralSfx.Note(15), 0.4f);

        // UI
        _uiClick = ProceduralSfx.Click(1000f, 0.05f);
        _uiHover = ProceduralSfx.Click(1800f, 0.025f);
        _menuOpen = ProceduralSfx.Swoosh(400f, 1400f, 0.25f, 2.5f);
        _menuClose = ProceduralSfx.Swoosh(1400f, 400f, 0.22f, 2.5f);
    }

    // ================= Mining =================

    // Ores ring with a metallic "clink", terrain gets the soft sand/dirt dig
    public void PlayMineHit(Vector3 position, bool isOre)
    {
        if (isOre)
            PlayWorld(Pick(_oreHits), position, oreHitVolume, 0.08f);
        else
            PlayWorld(Pick(_sandDigs), position, terrainDigVolume, 0.12f);
    }

    // A resource breaking apart: deep crumble plus a cloud of bubbles
    public void PlayResourceDepleted(Vector3 position)
    {
        PlayWorld(Pick(_thuds), position, resourceDepletedVolume, 0.1f, 1.4f);
        PlayBubbles(position, 6, resourceDepletedVolume);
    }

    // Each resource type has its own note, so you can hear what you're collecting
    public void PlayCollect(ResourceType type)
    {
        if (Time.time < _nextCollectTime) return;
        _nextCollectTime = Time.time + collectInterval;

        float semitones = type switch
        {
            ResourceType.IronOre => 0f,
            ResourceType.CopperOre => 3f,
            ResourceType.Pearl => 7f,
            ResourceType.Wood => -5f,
            ResourceType.Gold => 12f,
            _ => 0f
        };

        PlayUi(_collect, resourceCollectVolume, 0.02f, Mathf.Pow(2f, semitones / 12f));
    }

    public void PlayPickaxeSwing(Vector3 position) =>
        PlayWorld(Pick(_swings), position, pickaxeSwingVolume, 0.1f);

    // ================= Towers =================

    // Bubble Gun shots
    public void PlayBubbleBurst(Vector3 position, int count = 4) =>
        PlayBubbles(position, count, bubbleGunVolume);

    public void PlayProjectileHit(Vector3 position) =>
        PlayWorld(Pick(_bigBubbles), position, projectileHitVolume, 0.15f);

    // Bubble Burst splash upgrade
    public void PlaySplash(Vector3 position)
    {
        PlayWorld(Pick(_bigBubbles), position, projectileHitVolume, 0.1f, 0.7f);
        PlayBubbles(position, 8, projectileHitVolume);
    }

    // Higher tower levels sound slightly higher and punchier
    public void PlayHarpoon(Vector3 position, int level) =>
        PlayWorld(Pick(_plucks), position, harpoonVolume, 0.05f, 1f + 0.12f * (level - 1));

    public void PlayHarpoonHit(Vector3 position) =>
        PlayWorld(Pick(_harpoonHits), position, harpoonHitVolume, 0.08f);

    public void PlayHarpoonDock(Vector3 position) =>
        PlayWorld(_harpoonDock, position, harpoonHitVolume * 0.6f, 0.05f);

    public void PlayLaser(Vector3 position, int level) =>
        PlayWorld(Pick(_zaps), position, laserVolume, 0.05f, 1f + 0.1f * (level - 1));

    public void PlayTowerHit(Vector3 position) =>
        PlayWorld(Pick(_towerHits), position, towerHitVolume, 0.1f);

    // Heard from anywhere, since losing the main tower ends the game
    public void PlayMainTowerAlarm()
    {
        if (Time.time < _nextAlarmTime) return;
        _nextAlarmTime = Time.time + alarmInterval;

        PlayUi(_mainTowerAlarm, mainTowerAlarmVolume);
    }

    // Deep crash, sand pouring down, and a big cloud of bubbles
    public void PlayTowerDestroyed(Vector3 position)
    {
        PlayWorld(Pick(_thuds), position, towerDestroyedVolume, 0.05f, 0.7f);
        PlayWorld(Pick(_crumbles), position, towerDestroyedVolume * 0.8f, 0.1f, 0.8f);
        PlayBubbles(position, 10, towerDestroyedVolume * 0.6f);
    }

    // A tower slamming into the seabed after its drop
    public void PlayTowerLand(Vector3 position)
    {
        PlayWorld(Pick(_thuds), position, towerLandVolume, 0.05f, 0.8f);
        PlayWorld(Pick(_crumbles), position, towerLandVolume * 0.6f, 0.1f);
        PlayBubbles(position, 6, towerLandVolume * 0.4f);
    }

    // ================= Enemies =================

    public void PlayEnemyHit(Vector3 position) =>
        PlayWorld(Pick(_softThuds), position, enemyHitVolume, 0.15f);

    // size > 1 = bigger enemy = deeper thud
    public void PlayEnemyDeath(Vector3 position, float size = 1f)
    {
        PlayWorld(Pick(_thuds), position, enemyDeathVolume, 0.1f, 1f / Mathf.Max(0.25f, size));
        PlayBubbles(position, 3, enemyDeathVolume * 0.4f);
    }

    public void PlayBite(Vector3 position) =>
        PlayWorld(Pick(_bites), position, enemyAttackVolume, 0.15f);

    // The clip is stretched to match how long the pufferfish actually takes to inflate
    public void PlayPufferInflate(Vector3 position, float inflateTime)
    {
        float pitch = Mathf.Clamp(InflateClipLength / Mathf.Max(0.05f, inflateTime), 0.5f, 2f);
        PlayWorld(_inflate, position, enemyAttackVolume, 0.05f, pitch);
    }

    public void PlayPufferPop(Vector3 position)
    {
        PlayWorld(Pick(_bigBubbles), position, enemyAttackVolume, 0.1f, 0.6f);
        PlayBubbles(position, 6, enemyAttackVolume * 0.6f);
    }

    public void PlayJellyZap(Vector3 position) =>
        PlayWorld(Pick(_crackles), position, enemyAttackVolume, 0.1f, 1.2f);

    public void PlayJellyImpact(Vector3 position) =>
        PlayWorld(Pick(_crackles), position, enemyAttackVolume, 0.1f, 0.8f);

    public void PlayCaveSpawn(Vector3 position)
    {
        PlayWorld(Pick(_rumbles), position, caveSpawnVolume, 0.1f);
        PlayBubbles(position, 4, caveSpawnVolume * 0.5f);
    }

    // ================= Game flow =================

    public void PlayWaveStart() => PlayUi(_waveStart, waveStartVolume);

    public void PlayWaveCleared() => PlayUi(_waveCleared, waveClearedVolume);

    public void PlayCountdownTick(bool final) =>
        PlayUi(final ? _countdownFinal : _countdownTick, countdownVolume);

    // Silences the rest of the game (ambience, music, world sounds) and plays the game over horn.
    // UI voices ignore the pause, so menu clicks still work on the game over screen.
    public void PlayGameOver()
    {
        AudioListener.pause = true;
        PlayUi(_gameOver, gameOverVolume);
    }

    // ================= Building & economy =================

    public void PlayPurchase() => PlayUi(_purchase, purchaseVolume);

    // Every upgrade bought on a tower raises the chime a little
    public void PlayUpgrade(int totalUpgrades) =>
        PlayUi(_upgrade, upgradeVolume, 0f, Mathf.Pow(2f, Mathf.Min(totalUpgrades, 12) / 12f));

    public void PlayLevelUp() => PlayUi(_levelUp, upgradeVolume);

    public void PlayDenied() => PlayUi(_denied, deniedVolume, 0.02f);

    // ================= Player =================

    public void PlayFootstep(Vector3 position, bool running) =>
        PlayWorld(Pick(_footsteps), position, footstepVolume * (running ? 1.2f : 1f), 0.15f, running ? 1.1f : 1f);

    public void PlayJump(Vector3 position) =>
        PlayWorld(Pick(_kicks), position, jumpLandVolume, 0.1f);

    // strength 0..1 from how fast the player was falling
    public void PlayLand(Vector3 position, float strength)
    {
        PlayWorld(Pick(_footsteps), position, jumpLandVolume * Mathf.Lerp(0.6f, 1.3f, strength), 0.1f, 0.8f);
        PlayWorld(Pick(_softThuds), position, jumpLandVolume * strength, 0.1f, 0.6f);
    }

    // ================= UI =================

    public void PlayUiClick() => PlayUi(_uiClick, uiClickVolume, 0.03f);

    public void PlayUiHover() => PlayUi(_uiHover, uiHoverVolume, 0.05f);

    public void PlayMenuOpen() => PlayUi(_menuOpen, menuOpenCloseVolume);

    public void PlayMenuClose() => PlayUi(_menuClose, menuOpenCloseVolume);

    // Adds hover/click sounds to every button in the scene (including hidden menus).
    // Buttons spawned later (like upgrade rows) add a ButtonSound themselves.
    private void AddButtonSoundsInScene()
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Button button in buttons)
        {
            if (button.GetComponent<ButtonSound>() == null)
                button.gameObject.AddComponent<ButtonSound>();
        }
    }

    // ================= World =================

    public void PlayFishBubbles(Vector3 position) =>
        PlayBubbles(position, Random.Range(2, 5), fishBubblesVolume);

    public void PlayKelpRustle(Vector3 position) =>
        PlayWorld(Pick(_kelpSwishes), position, kelpVolume, 0.15f);

    // ================= Breathing =================

    // Slow inhale through the regulator, pause, exhale with bubbles rising past your mask.
    // Faster while sprinting. Only runs while there's a player in the scene.
    private IEnumerator BreathingRoutine()
    {
        while (true)
        {
            if (PlayerMovement.Instance == null || !enableBreathing)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            float scale = PlayerMovement.Instance.IsRunning ? sprintBreathScale : 1f;
            float cycle = breathCycle * scale;

            PlayUi(Pick(_inhales), breathingVolume, 0.03f, 1f / scale * 0.9f + 0.1f);
            yield return new WaitForSeconds(cycle * 0.35f);

            PlayUi(Pick(_exhales), breathingVolume, 0.03f, 1f / scale * 0.9f + 0.1f);
            yield return new WaitForSeconds(cycle * 0.1f);

            // Exhaled air bubbling up past your head
            PlayBubbles2D(5, breathingVolume * 0.8f);
            yield return new WaitForSeconds(cycle * 0.55f);
        }
    }

    // ================= Internals =================

    // A cluster of bubbles staggered slightly in time so they don't all pop at once
    private void PlayBubbles(Vector3 position, int count, float volume)
    {
        double start = AudioSettings.dspTime;

        for (int i = 0; i < count; i++)
        {
            double delay = i * 0.035 + ProceduralSfx.RandomRange(0f, 0.03f);
            PlayWorld(Pick(_bubbles), position, volume, 0.2f, 1f, start + delay);
        }
    }

    private void PlayBubbles2D(int count, float volume)
    {
        double start = AudioSettings.dspTime;

        for (int i = 0; i < count; i++)
        {
            double delay = i * 0.07 + ProceduralSfx.RandomRange(0f, 0.05f);
            PlayFrom(_ui, ref _nextUi, Pick(_bubbles), Vector3.zero, volume, 0.25f, 1f, start + delay);
        }
    }

    private void PlayWorld(AudioClip clip, Vector3 position, float volume, float pitchJitter,
        float pitchMultiplier = 1f, double scheduledTime = 0) =>
        PlayFrom(_world, ref _nextWorld, clip, position, volume, pitchJitter, pitchMultiplier, scheduledTime);

    private void PlayUi(AudioClip clip, float volume, float pitchJitter = 0f, float pitchMultiplier = 1f) =>
        PlayFrom(_ui, ref _nextUi, clip, Vector3.zero, volume, pitchJitter, pitchMultiplier, 0);

    private void PlayFrom(AudioSource[] pool, ref int next, AudioClip clip, Vector3 position, float volume,
        float pitchJitter, float pitchMultiplier, double scheduledTime)
    {
        if (clip == null || pool == null || volume <= 0f || effectsVolume <= 0f) return;

        // Round-robin: the oldest voice gets reused if all are busy
        AudioSource source = pool[next];
        next = (next + 1) % pool.Length;

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