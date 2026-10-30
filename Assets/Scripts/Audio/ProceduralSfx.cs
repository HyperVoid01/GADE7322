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

    // ---------------- Water ----------------

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

    // Moving water: noise through a resonant filter that sweeps from startFreq to endFreq.
    // Short and high = a fast swing through water; long and low = kelp swaying, a swim kick.
    public static AudioClip Swoosh(float startFreq, float endFreq, float duration, float resonance = 2f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        Svf filter = new Svf();
        float damping = 1f / Mathf.Max(0.5f, resonance);

        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;
            float freq = startFreq * Mathf.Pow(endFreq / startFreq, k);
            float envelope = Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.6f)); // quick rise, slow fall

            data[i] = filter.BandPass(Noise(), SvfCoefficient(freq), damping) * envelope;
        }

        return MakeClip("Swoosh", data);
    }

    // Deep, slow underwater rumble (enemies bursting out of a cave)
    public static AudioClip Rumble(float duration = 1.2f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;

        float brown = 0f;
        float lowPass = 0f;
        float lpCoef = 1f - Mathf.Exp(-TwoPi * 120f * dt);
        float wobblePhase = 0f;
        float wobbleRate = RandomRange(4f, 7f);

        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;

            brown = (brown + 0.02f * Noise()) / 1.02f;
            lowPass += lpCoef * (brown - lowPass);

            wobblePhase += TwoPi * wobbleRate * dt;
            float wobble = 0.75f + 0.25f * Mathf.Sin(wobblePhase);
            float envelope = Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.4f));

            data[i] = lowPass * wobble * envelope;
        }

        return MakeClip("Rumble", data);
    }

    // Scuba regulator breath. Inhale = bright, airy hiss with a faint whistle.
    // Exhale = lower, fluttering hiss (the regulator's valve vibrating as air pushes out).
    public static AudioClip Breath(bool inhale, float duration)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;

        float highCut = inhale ? 4200f : 1600f;
        float lowCut = inhale ? 1200f : 300f;
        float highCoef = 1f - Mathf.Exp(-TwoPi * highCut * dt);
        float lowCoef = 1f - Mathf.Exp(-TwoPi * lowCut * dt);
        float highLp = 0f;
        float lowLp = 0f;

        float whistlePhase = 0f;
        float flutterPhase = 0f;
        float flutterRate = RandomRange(22f, 30f);

        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;

            float white = Noise();
            highLp += highCoef * (white - highLp);
            lowLp += lowCoef * (white - lowLp);
            float hiss = highLp - lowLp;

            // Breaths swell in and tail off
            float envelope = Mathf.Pow(Mathf.Sin(Mathf.PI * k), inhale ? 1.5f : 1.1f);

            float sample;
            if (inhale)
            {
                whistlePhase += TwoPi * 2200f * dt;
                sample = hiss + Mathf.Sin(whistlePhase) * 0.04f;
            }
            else
            {
                flutterPhase += TwoPi * flutterRate * dt;
                sample = hiss * (0.7f + 0.3f * Mathf.Sin(flutterPhase));
            }

            data[i] = sample * envelope;
        }

        return MakeClip(inhale ? "Inhale" : "Exhale", data);
    }

    // Pufferfish gulping water: a rising tone plus a rising whoosh, cut off sharply at the end
    public static AudioClip Inflate(float duration = 0.6f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;
        Svf filter = new Svf();
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;

            float freq = 120f * Mathf.Pow(380f / 120f, k);
            phase += TwoPi * freq * dt;
            float tone = Mathf.Sin(phase) * 0.6f;

            float filterFreq = Mathf.Lerp(300f, 1400f, k);
            float whoosh = filter.BandPass(Noise(), SvfCoefficient(filterFreq), 0.5f);

            data[i] = (tone + whoosh) * Mathf.Pow(k, 0.5f);
        }

        return MakeClip("Inflate", data);
    }

    // ---------------- Ground ----------------

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
        float decay = RandomRange(9f, 13f) * 0.32f / duration; // longer clips decay slower

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
            float currentRate = grainRate * Mathf.Exp(-6f * t * 0.32f / duration);
            if (Rng.NextDouble() < currentRate * dt)
                grainGain = RandomRange(0.5f, 1.5f);
            grainGain *= grainDecayMul;

            thumpPhase += TwoPi * thumpFreq * dt;
            float thump = Mathf.Sin(thumpPhase) * Mathf.Exp(-30f * t) * 0.35f;

            data[i] = band * (0.4f + grainGain) * envelope + thump;
        }

        return MakeClip("SandDig", data);
    }

    // Rock / ore / metal hit: a noise click plus inharmonic "modes" (the ratios of a struck bar).
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

    // Low thud: a sine that drops in pitch plus muffled noise. Deaths, landings, crumbling.
    public static AudioClip Thud(float freq, float duration = 0.45f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;
        float phase = 0f;
        float noiseLp = 0f;
        float decay = 9f * 0.45f / duration;

        for (int i = 0; i < n; i++)
        {
            float t = i * dt;
            float currentFreq = freq * (1f + 1.5f * Mathf.Exp(-25f * t));
            phase += TwoPi * currentFreq * dt;

            noiseLp += 0.03f * (Noise() - noiseLp);

            data[i] = (Mathf.Sin(phase) + noiseLp * 4f) * Mathf.Exp(-decay * t);
        }

        return MakeClip("Thud", data);
    }

    // ---------------- Weapons ----------------

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

    // Electric crackle (jellyfish sting): random sparks over a mains-like buzz
    public static AudioClip Crackle(float duration = 0.35f, float buzzFreq = 90f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;

        float spark = 0f;
        float sparkDecayMul = Mathf.Exp(-dt / 0.0008f);
        float buzzPhase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i * dt;
            float envelope = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-8f * t);

            if (Rng.NextDouble() < 900f * dt)
                spark = RandomRange(0.4f, 1f);
            spark *= sparkDecayMul;

            buzzPhase += TwoPi * buzzFreq * dt;
            float buzz = Mathf.Sign(Mathf.Sin(buzzPhase)) * 0.15f;

            data[i] = (Noise() * spark + buzz) * envelope;
        }

        return MakeClip("Crackle", data);
    }

    // ---------------- Musical / UI ----------------

    // Bell-like chime: a few harmonics that die away, higher ones faster
    public static AudioClip Chime(float freq, float duration = 0.8f)
    {
        float[] data = new float[Samples(duration)];
        AddChime(data, 0, freq, data.Length);
        return MakeClip("Chime", data);
    }

    // Several chimes one after another (purchase, upgrade, level up, wave cleared)
    public static AudioClip Arpeggio(float[] freqs, float noteGap, float noteDuration)
    {
        int gap = Samples(noteGap);
        int noteLength = Samples(noteDuration);
        float[] data = new float[gap * (freqs.Length - 1) + noteLength];

        for (int i = 0; i < freqs.Length; i++)
            AddChime(data, i * gap, freqs[i], noteLength);

        return MakeClip("Arpeggio", data);
    }

    // Short UI blip. Low freq + short = soft tick, high freq = sharper click.
    public static AudioClip Click(float freq, float duration = 0.05f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;
        float phase = 0f;
        float decay = 5f / duration;

        for (int i = 0; i < n; i++)
        {
            float t = i * dt;
            phase += TwoPi * freq * dt;
            float transient = t < 0.001f ? Noise() * 0.3f : 0f;
            data[i] = (Mathf.Sin(phase) + transient) * Mathf.Exp(-decay * t);
        }

        return MakeClip("Click", data);
    }

    // "Denied" buzz: low square-ish pulses, filtered so it isn't harsh
    public static AudioClip Buzz(float freq, float duration = 0.3f, int pulses = 2)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;
        float phase = 0f;
        float lowPass = 0f;
        float lpCoef = 1f - Mathf.Exp(-TwoPi * 900f * dt);
        int pulseLength = n / Mathf.Max(1, pulses);

        for (int i = 0; i < n; i++)
        {
            phase += TwoPi * freq * dt;
            float square = Mathf.Sign(Mathf.Sin(phase));
            lowPass += lpCoef * (square - lowPass);

            // Each pulse is on for the first 70% of its slot
            float inPulse = (i % pulseLength) / (float)pulseLength;
            float gate = inPulse < 0.7f ? 1f : 0f;

            data[i] = lowPass * gate;
        }

        return MakeClip("Buzz", data);
    }

    // Brassy horn: a filtered sawtooth that swells in and out, gliding from startFreq to endFreq.
    // fifthLevel > 0 adds a second voice a fifth above (a power chord) for a fuller blast.
    public static AudioClip Horn(float startFreq, float endFreq, float duration, float fifthLevel = 0.6f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;

        float phaseA = 0f;
        float phaseB = 0f;
        float vibratoPhase = 0f;
        float lowPass = 0f;

        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;

            float attack = Mathf.Clamp01(k / 0.2f);
            float release = Mathf.Clamp01((1f - k) / 0.3f);
            float envelope = attack * release;

            vibratoPhase += TwoPi * 5f * dt;
            float freq = Mathf.Lerp(startFreq, endFreq, k) * (1f + 0.006f * Mathf.Sin(vibratoPhase));

            phaseA += freq * dt;
            phaseB += freq * 1.5f * dt;
            phaseA -= Mathf.Floor(phaseA);
            phaseB -= Mathf.Floor(phaseB);

            float saw = (phaseA * 2f - 1f) + (phaseB * 2f - 1f) * fifthLevel;

            // Brass gets brighter as it gets louder
            float cutoff = Mathf.Lerp(200f, 1800f, envelope);
            lowPass += (1f - Mathf.Exp(-TwoPi * cutoff * dt)) * (saw - lowPass);

            data[i] = lowPass * envelope;
        }

        return MakeClip("Horn", data);
    }

    // Two-tone alarm (main tower under attack)
    public static AudioClip Alarm(float lowFreq, float highFreq, float duration = 0.6f, float toneLength = 0.15f)
    {
        int n = Samples(duration);
        float[] data = new float[n];
        float dt = 1f / SampleRate;
        float phase = 0f;
        float lowPass = 0f;
        float lpCoef = 1f - Mathf.Exp(-TwoPi * 1500f * dt);

        for (int i = 0; i < n; i++)
        {
            float t = i * dt;
            bool high = Mathf.FloorToInt(t / toneLength) % 2 == 0;
            phase += TwoPi * (high ? highFreq : lowFreq) * dt;

            float square = Mathf.Sign(Mathf.Sin(phase));
            lowPass += lpCoef * (square - lowPass);

            float envelope = Mathf.Clamp01((1f - (float)i / n) / 0.2f);
            data[i] = lowPass * envelope;
        }

        return MakeClip("Alarm", data);
    }

    // ---------------- Helpers ----------------

    // Builds an array of variants so each play can pick a different one
    public static AudioClip[] Bake(int count, System.Func<AudioClip> generator)
    {
        AudioClip[] clips = new AudioClip[count];
        for (int i = 0; i < count; i++)
            clips[i] = generator();
        return clips;
    }

    // Musical note frequency, 0 = A4 (440 Hz), +12 = an octave up
    public static float Note(int semitonesFromA4) => 440f * Mathf.Pow(2f, semitonesFromA4 / 12f);

    public static float RandomRange(float min, float max) => min + (float)Rng.NextDouble() * (max - min);

    private static void AddChime(float[] data, int start, float freq, int length)
    {
        float dt = 1f / SampleRate;
        float[] ratios = { 1f, 2f, 3f, 4.2f };
        float[] amps = { 1f, 0.4f, 0.18f, 0.08f };
        float[] decays = { 4f, 6f, 9f, 14f };

        for (int m = 0; m < ratios.Length; m++)
        {
            float phase = 0f;
            float step = TwoPi * freq * ratios[m] * dt;

            for (int i = 0; i < length && start + i < data.Length; i++)
            {
                float t = i * dt;
                phase += step;
                float attack = Mathf.Min(1f, t / 0.003f);
                data[start + i] += Mathf.Sin(phase) * amps[m] * attack * Mathf.Exp(-decays[m] * t);
            }
        }
    }

    private static float Noise() => (float)(Rng.NextDouble() * 2.0 - 1.0);

    private static int Samples(float seconds) => Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate));

    // Frequency coefficient for the state variable filter (kept below a stable maximum)
    private static float SvfCoefficient(float freq) =>
        2f * Mathf.Sin(Mathf.PI * Mathf.Min(freq, SampleRate / 6f) / SampleRate);

    // Chamberlin state variable filter: a cheap resonant filter for sweeping noise
    private struct Svf
    {
        private float _low;
        private float _band;

        // damping: 1/Q. Lower = more resonant ("whistly"), higher = broader
        public float BandPass(float input, float frequency, float damping)
        {
            _low += frequency * _band;
            float high = input - _low - damping * _band;
            _band += frequency * high;
            return _band;
        }
    }

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