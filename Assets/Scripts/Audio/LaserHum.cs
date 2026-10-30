using UnityEngine;

// Continuous hum for the Lazer tower's beam, generated live on the audio thread.
// It fades in while the beam is on, and rises in pitch and wobbles faster as the beam overcharges.
//
// 3D trick: Unity applies distance and panning to the AudioSource's clip, then runs
// OnAudioFilterRead on the result. So the source plays a looping clip of all 1s (already
// spatialised), and the filter multiplies it by the generated hum.
//
// Lazer creates this itself, so no setup is needed.
[RequireComponent(typeof(AudioSource))]
public class LaserHum : MonoBehaviour
{
    private const float TwoPi = Mathf.PI * 2f;

    private static AudioClip _onesClip;

    private float _sampleRate;

    // Written on the main thread, read on the audio thread
    private volatile float _targetGain;
    private volatile float _charge;
    private volatile float _baseFreq = 110f;

    private bool _beamOn;
    private float _beamCharge;
    private int _level = 1;

    // Audio thread state
    private float _gain;
    private float _smoothCharge;
    private float _phase;
    private float _wobblePhase;

    public static LaserHum Create(Transform parent)
    {
        GameObject hum = new GameObject("Laser Hum");
        hum.transform.SetParent(parent, false);
        hum.AddComponent<AudioSource>();
        return hum.AddComponent<LaserHum>();
    }

    private void Awake()
    {
        _sampleRate = AudioSettings.outputSampleRate;

        AudioSource source = GetComponent<AudioSource>();
        source.clip = GetOnesClip();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = 40f;
        source.dopplerLevel = 0f;
        source.Play();
    }

    // Called by Lazer every frame. charge 0..1
    public void SetBeam(bool on, float charge, int level)
    {
        _beamOn = on;
        _beamCharge = charge;
        _level = level;
    }

    private void Update()
    {
        float volume = SfxPlayer.Instance != null ? SfxPlayer.Instance.LaserHumVolume : 0f;

        _targetGain = _beamOn ? volume : 0f;
        _charge = _beamCharge;
        _baseFreq = 110f * (1f + 0.1f * (_level - 1));
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float dt = 1f / _sampleRate;
        float targetGain = _targetGain;
        float targetCharge = _charge;
        float baseFreq = _baseFreq;

        for (int i = 0; i < data.Length; i += channels)
        {
            // Smooth fades so turning the beam on/off never clicks
            _gain += (targetGain - _gain) * 0.0015f;
            _smoothCharge += (targetCharge - _smoothCharge) * 0.0005f;

            float freq = baseFreq * (1f + 0.5f * _smoothCharge);
            _phase += TwoPi * freq * dt;
            if (_phase > TwoPi) _phase -= TwoPi;

            float wobbleRate = Mathf.Lerp(6f, 18f, _smoothCharge);
            _wobblePhase += TwoPi * wobbleRate * dt;
            if (_wobblePhase > TwoPi) _wobblePhase -= TwoPi;

            // A few harmonics for an electric buzz; overcharge adds a brighter edge
            float tone = Mathf.Sin(_phase)
                         + 0.5f * Mathf.Sin(2f * _phase)
                         + 0.25f * Mathf.Sin(3f * _phase)
                         + 0.3f * _smoothCharge * Mathf.Sin(5f * _phase);

            float wobble = 0.8f + 0.2f * Mathf.Sin(_wobblePhase);
            float sample = tone * 0.35f * wobble * _gain;

            for (int c = 0; c < channels; c++)
                data[i + c] *= sample;
        }
    }

    // One second of constant 1s, shared by every laser.
    // Checked with Unity's null so it's rebuilt if a scene change unloaded it.
    private static AudioClip GetOnesClip()
    {
        if (_onesClip != null) return _onesClip;

        int rate = AudioSettings.outputSampleRate;
        float[] ones = new float[rate];
        for (int i = 0; i < ones.Length; i++)
            ones[i] = 1f;

        _onesClip = AudioClip.Create("Ones", rate, 1, rate, false);
        _onesClip.SetData(ones, 0);
        return _onesClip;
    }
}
