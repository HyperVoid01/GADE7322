using UnityEngine;

// Synthesises short sound effects into AudioClips at runtime - no audio files needed.
// Every call produces a slightly different sound (random noise, detune), so baking a few
// variants per sound gives natural variation instead of the same sample repeating.
// Call these from the main thread only (AudioClip.Create / SetData are main-thread APIs).
public static class ProceduralSfx
{
    private static readonly System.Random Rng = new System.Random();
    private const float TwoPi = Mathf.PI * 2f;

    private static int SampleRate => AudioSettings.outputSampleRate;

    // Underwater bubble: a decaying sine whose pitch rises as the bubble settles.
    // Pitch comes from the Minnaert resonance (~3.26 Hz·m), so bigger bubbles = deeper "bloop".
    public static AudioClip Bubble(float radiusMm, float duration = 0.15f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;

        float freq = 3260f / Mathf.Max(0.5f, radiusMm) * RandomRange(0.9f, 1.1f);
        float decay = freq * 0.04f; // higher bubbles die faster
        float rise = RandomRange(2f, 5f); // pitch rise per second (relative)
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i * dt;
            float currentFreq = freq * (1f + rise * t);
            phase += TwoPi * currentFreq * dt;
            data[i] = Mathf.Sin(phase) * Mathf.Exp(-decay * t);
        }

        return MakeClip("Bubble", data);
    }

    // Rock / ore hit: a noise click plus inharmonic "modes" (the ratios of a struck bar).
    // baseFreq sets the size/material, brightness 0..1 sets how much clicky noise there is.
    public static AudioClip Impact(float baseFreq, float brightness, float duration = 0.4f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;

        float[] ratios = { 1f, 2.76f, 5.40f, 8.93f };
        float[] amps = { 1f, 0.55f, 0.3f, 0.18f };
        float[] decays = { 18f, 30f, 45f, 70f };
        float[] phases = new float[ratios.Length];
        float[] freqs = new float[ratios.Length];

        for (int m = 0; m < ratios.Length; m++)
            freqs[m] = baseFreq * ratios[m] * RandomRange(0.97f, 1.03f);

        float noiseLp = 0f;
        float noiseCoef = Mathf.Lerp(0.05f, 0.6f, brightness);

        for (int i = 0; i < n; i++)
        {
            float t = i * dt;
            float sample = 0f;

            for (int m = 0; m < ratios.Length; m++)
            {
                phases[m] += TwoPi * freqs[m] * dt;
                sample += Mathf.Sin(phases[m]) * amps[m] * Mathf.Exp(-decays[m] * t);
            }

            // Short filtered noise burst = the "crack" of the hit
            noiseLp += noiseCoef * (Noise() - noiseLp);
            sample += noiseLp * 1.5f * Mathf.Exp(-60f * t);

            data[i] = sample;
        }

        return MakeClip("Impact", data);
    }

    // Digging into sand/dirt: a soft "shhk" made of many tiny grains of noise.
    // - Band-passed noise is the hiss of loose particles sliding (no ringing tones, unlike rock)
    // - Random grains of extra loudness give the gritty, crunchy texture
    // - Grains thin out over time, like particles settling after the dig
    // - A faint low thump is the pickaxe pushing into the soft ground
    // brightness 0..1: 0 = damp, muddy dirt, 1 = dry, crunchy sand
    public static AudioClip SandDig(float brightness, float duration = 0.32f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;

        // Band-pass = low-pass (removes harsh highs) minus a lower low-pass (removes rumble)
        float highCut = Mathf.Lerp(1400f, 3500f, brightness) * RandomRange(0.85f, 1.15f);
        float lowCut = Mathf.Lerp(180f, 400f, brightness);
        float highCoef = 1f - Mathf.Exp(-TwoPi * highCut * dt);
        float lowCoef = 1f - Mathf.Exp(-TwoPi * lowCut * dt);
        float highLp = 0f;
        float lowLp = 0f;

        float attack = 0.012f;
        float decay = RandomRange(9f, 13f);

        float grainRate = Mathf.Lerp(250f, 600f, brightness); // grains per second at the start
        float grainGain = 0f;
        float grainDecayMul = Mathf.Exp(-dt / 0.003f); // each grain lasts ~3ms

        float thumpFreq = RandomRange(60f, 80f);
        float thumpPhase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i * dt;
            float envelope = Mathf.Min(1f, t / attack) * Mathf.Exp(-decay * t);

            float white = Noise();
            highLp += highCoef * (white - highLp);
            lowLp += lowCoef * (white - lowLp);
            float band = highLp - lowLp;

            // Grains get rarer as the sound decays
            float currentRate = grainRate * Mathf.Exp(-6f * t);
            if (Rng.NextDouble() < currentRate * dt)
                grainGain = RandomRange(0.5f, 1.5f);
            grainGain *= grainDecayMul;

            thumpPhase += TwoPi * thumpFreq * dt;
            float thump = Mathf.Sin(thumpPhase) * Mathf.Exp(-30f * t) * 0.35f;

            data[i] = band * (0.4f + grainGain) * envelope + thump;
        }

        return MakeClip("SandDig", data);
    }

    // Plucked string (Karplus-Strong): a burst of noise fed through a short delay line.
    // Good for the harpoon's launcher "twang".
    public static AudioClip Pluck(float freq, float duration = 0.6f, float damping = 0.995f)
    {
        int n = Samples(duration);
        float[] data = new float[n];

        int period = Mathf.Max(2, Mathf.RoundToInt(SampleRate / freq));
        float[] ring = new float[period];
        for (int i = 0; i < period; i++)
            ring[i] = Noise();

        int index = 0;
        for (int i = 0; i < n; i++)
        {
            int next = (index + 1) % period;
            float value = ring[index];
            ring[index] = 0.5f * (ring[index] + ring[next]) * damping;
            data[i] = value;
            index = next;
        }

        return MakeClip("Pluck", data);
    }

    // Laser zap: a sine sweeping from startFreq down to endFreq with a slight buzz.
    public static AudioClip Zap(float startFreq, float endFreq, float duration = 0.25f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;
            float freq = Mathf.Lerp(startFreq, endFreq, k * k);
            phase += TwoPi * freq * dt;

            float sine = Mathf.Sin(phase);
            float buzz = Mathf.Sign(sine) * 0.25f; // square-ish harmonics
            data[i] = (sine + buzz) * Mathf.Exp(-6f * k);
        }

        return MakeClip("Zap", data);
    }

    // Low thud: a sine that drops in pitch plus muffled noise. Enemy deaths, digging, depleted ores.
    public static AudioClip Thud(float freq, float duration = 0.45f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;
        float phase = 0f;
        float noiseLp = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i * dt;
            float currentFreq = freq * (1f + 1.5f * Mathf.Exp(-25f * t));
            phase += TwoPi * currentFreq * dt;

            noiseLp += 0.03f * (Noise() - noiseLp);

            data[i] = (Mathf.Sin(phase) + noiseLp * 4f) * Mathf.Exp(-9f * t);
        }

        return MakeClip("Thud", data);
    }

    // Builds an array of variants so each play can pick a different one
    public static AudioClip[] Bake(int count, System.Func<AudioClip> generator)
    {
        AudioClip[] clips = new AudioClip[count];
        for (int i = 0; i < count; i++)
            clips[i] = generator();
        return clips;
    }

    public static float RandomRange(float min, float max) => min + (float)Rng.NextDouble() * (max - min);

    private static float Noise() => (float)(Rng.NextDouble() * 2.0 - 1.0);

    private static int Samples(float seconds) => Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate));

    // Normalises to a safe peak and fades the tail so clips never end on a click
    private static AudioClip MakeClip(string clipName, float[] data)
    {
        float peak = 0f;
        foreach (float s in data)
            peak = Mathf.Max(peak, Mathf.Abs(s));

        float gain = peak > 0.0001f ? 0.9f / peak : 0f;
        int fade = Mathf.Min(256, data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            float tail = i >= data.Length - fade ? (data.Length - i) / (float)fade : 1f;
            data[i] *= gain * tail;
        }

        AudioClip clip = AudioClip.Create(clipName, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}