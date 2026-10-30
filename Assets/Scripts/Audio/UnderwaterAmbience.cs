using UnityEngine;

// Generates the underwater background sound live, sample by sample, on the audio thread:
//  - muffled brown noise that slowly swells like moving water
//  - random bubbles drifting past
//  - a low detuned drone that fades in as 'tension' rises (drive it from the wave state)
//
// Rules for OnAudioFilterRead (it runs on the AUDIO thread, not the main thread):
//  - no Unity API calls (no transform, no UnityEngine.Random, no GetComponent)
//  - no allocations
//  - only read plain fields that Update() copies across
[RequireComponent(typeof(AudioSource))]
public class UnderwaterAmbience : MonoBehaviour
{
    [Header("Mix")]
    [Range(0f, 1f)] public float volume = 0.35f;
    [Range(0f, 1f)] public float tension = 0f; // 0 = calm, 1 = mid-wave

    [Header("Water")]
    [SerializeField] private float baseCutoff = 350f; // Hz - lower = more muffled
    [SerializeField] private float swellSpeed = 0.08f; // swells per second

    [Header("Bubbles")]
    [SerializeField] private float bubblesPerSecond = 1.5f;

    [Header("Drone")]
    [SerializeField] private float droneFrequency = 55f;

    public static UnderwaterAmbience Instance;

    private const float TwoPi = Mathf.PI * 2f;

    private System.Random _rng;
    private float _sampleRate;

    // Copied from the main thread every frame
    private volatile float _volume;
    private volatile float _tension;

    // Audio thread state
    private float _brown;
    private float _lowPass;
    private float _swellPhase;
    private float _dronePhaseA;
    private float _dronePhaseB;
    private float _droneGain;

    private float _bubblePhase;
    private float _bubbleFreq;
    private float _bubbleAmp;
    private float _bubbleDecayMul;
    private float _bubbleRiseMul;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        _sampleRate = AudioSettings.outputSampleRate;
        _rng = new System.Random();

        AudioSource source = GetComponent<AudioSource>();
        source.clip = null;
        source.loop = true;
        source.spatialBlend = 0f; // ambience is 2D, it surrounds the player
        source.playOnAwake = true;
    }

    private void Start()
    {
        GetComponent<AudioSource>().Play();
    }

    private void Update()
    {
        _volume = volume;
        _tension = tension;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float dt = 1f / _sampleRate;
        float vol = _volume;
        float targetDrone = _tension * 0.25f;

        // Water gets slightly brighter/louder when tension is high
        float cutoff = baseCutoff + _tension * 500f;
        float lpCoef = 1f - Mathf.Exp(-TwoPi * cutoff * dt);
        float bubbleChance = bubblesPerSecond * dt;

        for (int i = 0; i < data.Length; i += channels)
        {
            // Muffled moving water
            float white = (float)(_rng.NextDouble() * 2.0 - 1.0);
            _brown = (_brown + 0.02f * white) / 1.02f;
            _lowPass += lpCoef * (_brown * 3.5f - _lowPass);

            _swellPhase += swellSpeed * dt;
            if (_swellPhase > 1f) _swellPhase -= 1f;
            float swell = 0.6f + 0.4f * Mathf.Sin(TwoPi * _swellPhase);

            float water = _lowPass * swell;

            // Random bubbles
            if (_bubbleAmp < 0.001f && _rng.NextDouble() < bubbleChance)
                TriggerBubble(dt);

            float bubble = 0f;
            if (_bubbleAmp >= 0.001f)
            {
                _bubblePhase += TwoPi * _bubbleFreq * dt;
                if (_bubblePhase > TwoPi) _bubblePhase -= TwoPi;
                bubble = Mathf.Sin(_bubblePhase) * _bubbleAmp;
                _bubbleAmp *= _bubbleDecayMul;
                _bubbleFreq *= _bubbleRiseMul;
            }

            // Tension drone (two detuned sines beat against each other)
            _droneGain += (targetDrone - _droneGain) * 0.00005f; // slow glide, no clicks
            _dronePhaseA += TwoPi * droneFrequency * dt;
            _dronePhaseB += TwoPi * droneFrequency * 1.007f * dt;
            if (_dronePhaseA > TwoPi) _dronePhaseA -= TwoPi;
            if (_dronePhaseB > TwoPi) _dronePhaseB -= TwoPi;
            float drone = (Mathf.Sin(_dronePhaseA) + Mathf.Sin(_dronePhaseB)) * 0.5f * _droneGain;

            float sample = (water * 0.8f + bubble + drone) * vol;

            for (int c = 0; c < channels; c++)
                data[i + c] = sample;
        }
    }

    private void TriggerBubble(float dt)
    {
        float radiusMm = 1.5f + (float)_rng.NextDouble() * 4f;
        _bubbleFreq = 3260f / radiusMm;
        _bubbleAmp = 0.04f + (float)_rng.NextDouble() * 0.12f;
        _bubblePhase = 0f;
        _bubbleDecayMul = Mathf.Exp(-_bubbleFreq * 0.04f * dt);
        _bubbleRiseMul = 1f + 3f * dt;
    }
}
