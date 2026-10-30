using UnityEngine;

// Music composed live from the terrain seed, so every generated map gets its own tune.
//  - Pad: soft, slowly gliding chords from a seeded 4-chord progression
//  - Melody: plucked notes wandering around a minor pentatonic scale (it can't sound "wrong")
//  - Bass pulse: fades in with 'tension' during waves
//  - Echo: a dark feedback delay so it all sounds like it's drifting through water
// Notes speed up and get busier as tension rises. EnemySpawner drives 'tension'.
//
// Runs on the audio thread: no Unity API calls inside OnAudioFilterRead, and the only allocation
// is the new random generator when the map's seed arrives.
[RequireComponent(typeof(AudioSource))]
public class GenerativeMusic : MonoBehaviour
{
    [Header("Mix")]
    [Range(0f, 1f)] public float volume = 0.3f;
    [Range(0f, 1f)] public float tension = 0f;

    [Header("Composition")]
    [Tooltip("Semitones from A4. -21 = C3")]
    [SerializeField] private int rootNote = -21;
    [Tooltip("Seconds per chord")]
    [SerializeField] private float chordLength = 8f;
    [SerializeField] private float calmNoteInterval = 0.9f;
    [SerializeField] private float tenseNoteInterval = 0.3f;

    [Header("Echo")]
    [SerializeField] private float echoTime = 0.42f;
    [Range(0f, 0.85f)] [SerializeField] private float echoFeedback = 0.45f;

    public static GenerativeMusic Instance;

    private const float TwoPi = Mathf.PI * 2f;
    private const float MaxEchoSeconds = 2f;

    private static readonly int[] Pentatonic = { 0, 3, 5, 7, 10 }; // minor pentatonic
    private static readonly int[] ChordChoices = { 0, 3, 5, 7, -2, 8 };

    private float _sampleRate;
    private System.Random _rng = new System.Random(0);

    // Main thread -> audio thread
    private volatile float _volume;
    private volatile float _tension;
    private volatile int _pendingSeed;
    private volatile bool _hasPendingSeed;

    // Composition
    private readonly int[] _progression = new int[4];
    private int _chordIndex;
    private int _samplesUntilChord;
    private int _samplesUntilNote;
    private int _melodyDegree = 5;
    private float _smoothTension;

    // Pad
    private float _padFreq;
    private float _padTarget;
    private float _padPhaseA;
    private float _padPhaseB;
    private float _padPhaseC;
    private float _padSwellPhase;

    // Melody voice
    private float _melodyFreq;
    private float _melodyPhase;
    private float _melodyAmp;
    private float _melodyAttack;

    // Bass voice
    private float _bassFreq;
    private float _bassPhase;
    private float _bassAmp;

    // Echo + tone
    private float[] _echo;
    private int _echoIndex;
    private float _echoLowPass;
    private float _outLowPass;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        _sampleRate = AudioSettings.outputSampleRate;
        _echo = new float[Mathf.CeilToInt(_sampleRate * MaxEchoSeconds)];

        Reseed(0);

        AudioSource source = GetComponent<AudioSource>();
        source.clip = null;
        source.loop = true;
        source.spatialBlend = 0f;
        source.playOnAwake = true;
    }

    private void Start()
    {
        GetComponent<AudioSource>().Play();

        // Use the map's seed. Subscribing also catches regenerated terrain later.
        if (MarchingCubes.Instance != null)
        {
            RequestSeed(MarchingCubes.Instance.Seed);
            MarchingCubes.Instance.OnTerrainGenerated += HandleTerrainGenerated;
        }
    }

    private void OnDestroy()
    {
        if (MarchingCubes.Instance != null)
            MarchingCubes.Instance.OnTerrainGenerated -= HandleTerrainGenerated;

        if (Instance == this)
            Instance = null;
    }

    private void HandleTerrainGenerated()
    {
        RequestSeed(MarchingCubes.Instance.Seed);
    }

    // The audio thread picks this up on its next buffer
    private void RequestSeed(int seed)
    {
        _pendingSeed = seed;
        _hasPendingSeed = true;
    }

    private void Update()
    {
        _volume = volume;
        _tension = tension;
    }

    private void Reseed(int seed)
    {
        _rng = new System.Random(seed);

        // A progression that always starts on the home chord
        _progression[0] = 0;
        for (int i = 1; i < _progression.Length; i++)
            _progression[i] = ChordChoices[_rng.Next(ChordChoices.Length)];

        _chordIndex = -1; // NextChord moves to 0, the home chord
        _samplesUntilChord = 0;
        _samplesUntilNote = 0;
        _melodyDegree = 5;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (_hasPendingSeed)
        {
            _hasPendingSeed = false;
            Reseed(_pendingSeed);
        }

        float dt = 1f / _sampleRate;
        float vol = _volume;
        float targetTension = _tension;

        int echoLength = Mathf.Clamp(Mathf.RoundToInt(echoTime * _sampleRate), 1, _echo.Length);
        float outCoef = 1f - Mathf.Exp(-TwoPi * 2500f * dt);
        float echoCoef = 1f - Mathf.Exp(-TwoPi * 1500f * dt);

        for (int i = 0; i < data.Length; i += channels)
        {
            _smoothTension += (targetTension - _smoothTension) * 0.00002f;

            // ---- Sequencing ----
            if (--_samplesUntilChord <= 0)
                NextChord();

            if (--_samplesUntilNote <= 0)
                NextNote();

            // ---- Pad: root, fifth and octave, gliding between chords ----
            _padFreq += (_padTarget - _padFreq) * 0.00005f;
            _padPhaseA += TwoPi * _padFreq * dt;
            _padPhaseB += TwoPi * _padFreq * 1.4983f * dt;
            _padPhaseC += TwoPi * _padFreq * 2.003f * dt;
            _padSwellPhase += TwoPi * 0.1f * dt;
            WrapPhase(ref _padPhaseA);
            WrapPhase(ref _padPhaseB);
            WrapPhase(ref _padPhaseC);
            WrapPhase(ref _padSwellPhase);

            float pad = (Mathf.Sin(_padPhaseA) + 0.6f * Mathf.Sin(_padPhaseB) + 0.35f * Mathf.Sin(_padPhaseC))
                        * (0.12f + 0.04f * Mathf.Sin(_padSwellPhase));

            // ---- Melody: soft pluck ----
            _melodyAttack = Mathf.Min(1f, _melodyAttack + dt / 0.01f);
            float melodyDecay = Mathf.Lerp(1.5f, 4f, _smoothTension);
            _melodyAmp *= 1f - melodyDecay * dt;
            _melodyPhase += TwoPi * _melodyFreq * dt;
            WrapPhase(ref _melodyPhase);
            float melody = (Mathf.Sin(_melodyPhase) + 0.3f * Mathf.Sin(2f * _melodyPhase)) * _melodyAmp * _melodyAttack;

            // ---- Bass pulse (only with tension) ----
            _bassAmp *= 1f - 6f * dt;
            _bassPhase += TwoPi * _bassFreq * dt;
            WrapPhase(ref _bassPhase);
            float bass = Mathf.Sin(_bassPhase) * _bassAmp;

            float dry = pad + melody * 0.35f + bass * 0.5f;

            // ---- Dark echo ----
            int readIndex = _echoIndex - echoLength;
            if (readIndex < 0) readIndex += _echo.Length;
            float delayed = _echo[readIndex];
            _echoLowPass += echoCoef * (delayed - _echoLowPass);
            _echo[_echoIndex] = dry + _echoLowPass * echoFeedback;
            _echoIndex = (_echoIndex + 1) % _echo.Length;

            float mixed = dry + _echoLowPass * 0.5f;
            _outLowPass += outCoef * (mixed - _outLowPass);

            float sample = _outLowPass * vol;

            for (int c = 0; c < channels; c++)
                data[i + c] = sample;
        }
    }

    private void NextChord()
    {
        _samplesUntilChord = Mathf.Max(1, Mathf.RoundToInt(chordLength * _sampleRate));
        _chordIndex = (_chordIndex + 1) % _progression.Length;
        _padTarget = NoteFreq(rootNote + _progression[_chordIndex]);

        if (_padFreq <= 0f)
            _padFreq = _padTarget;
    }

    private void NextNote()
    {
        float interval = Mathf.Lerp(calmNoteInterval, tenseNoteInterval, _smoothTension);
        _samplesUntilNote = Mathf.Max(1, Mathf.RoundToInt(interval * _sampleRate));

        // Calm music leaves more gaps
        float playChance = Mathf.Lerp(0.55f, 0.9f, _smoothTension);
        if (_rng.NextDouble() < playChance)
        {
            // Random walk over two octaves of the scale
            _melodyDegree = Mathf.Clamp(_melodyDegree + _rng.Next(-2, 3), 0, Pentatonic.Length * 2 - 1);
            int octave = _melodyDegree / Pentatonic.Length;
            int semitone = Pentatonic[_melodyDegree % Pentatonic.Length] + 12 * octave;

            _melodyFreq = NoteFreq(rootNote + 12 + semitone);
            _melodyAmp = 0.5f + (float)_rng.NextDouble() * 0.3f;
            _melodyAttack = 0f;
        }

        if (_smoothTension > 0.05f)
        {
            _bassFreq = NoteFreq(rootNote - 12 + _progression[_chordIndex]);
            _bassAmp = _smoothTension * 0.6f;
        }
    }

    private static float NoteFreq(int semitonesFromA4) => 440f * Mathf.Pow(2f, semitonesFromA4 / 12f);

    private static void WrapPhase(ref float phase)
    {
        if (phase > TwoPi) phase -= TwoPi;
    }
}
