using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Shared helpers for building particle effects from code.
// The effect scripts use these in Reset(), which runs when the component is added in the editor,
// so the Particle Systems arrive pre-configured and can then be tweaked by hand and saved as a prefab.
public static class ParticleSetup
{
    private const string UrpParticleMaterial = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

    // Finds a child Particle System by name, or creates it
    public static ParticleSystem GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);

        if (child == null)
        {
            GameObject go = new GameObject(childName);
            go.transform.SetParent(parent, false);
            child = go.transform;
#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(go, "Create " + childName);
#endif
        }

        ParticleSystem system = child.GetComponent<ParticleSystem>();
        if (system == null)
            system = child.gameObject.AddComponent<ParticleSystem>();

        return system;
    }

    // A system that plays once when told to, and never on its own
    public static void OneShot(ParticleSystem system, float duration, int maxParticles,
        ParticleSystemSimulationSpace space = ParticleSystemSimulationSpace.World)
    {
        ParticleSystem.MainModule main = system.main;
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = space;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = maxParticles;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[0]);
    }

    // Flat circle lying on the ground (a Circle shape is upright by default, so it's tipped over)
    public static void FlatCircle(ParticleSystem system, float radius, float thickness)
    {
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = thickness; // 0 = edge only, 1 = whole disc
        shape.arc = 360f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
    }

    // Alpha fades in, holds, then fades out. Multiplies whatever start color the particle has.
    public static Gradient Fade(float fadeInEnd, float fadeOutStart)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(fadeInEnd > 0f ? 0f : 1f, 0f),
                new GradientAlphaKey(1f, Mathf.Max(0.001f, fadeInEnd)),
                new GradientAlphaKey(1f, fadeOutStart),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    public static void FadeOverLifetime(ParticleSystem system, float fadeInEnd, float fadeOutStart)
    {
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(Fade(fadeInEnd, fadeOutStart));
    }

    public static void SizeOverLifetime(ParticleSystem system, AnimationCurve curve)
    {
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    // Short glowing streak behind each particle
    public static void Trails(ParticleSystem system, float lifetime, Material material)
    {
        ParticleSystem.TrailModule trails = system.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.ratio = 1f;
        trails.lifetime = lifetime;
        trails.minVertexDistance = 0.05f;
        trails.dieWithParticles = true;
        trails.inheritParticleColor = true;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.trailMaterial = material;
    }

    public static void SetRenderer(ParticleSystem system, Material material,
        ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard)
    {
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        if (material != null) renderer.sharedMaterial = material;

        if (mode == ParticleSystemRenderMode.Mesh)
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
    }

    // Rising, wobbling bubbles. Used by both the landing dust and the upgrade burst.
    public static void Bubbles(ParticleSystem system, Material material)
    {
        OneShot(system, 0.5f, 120);

        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.22f);
        main.startColor = new Color(1f, 1f, 1f, 0.8f);
        main.gravityModifier = -0.12f; // Negative gravity: they float up

        ParticleSystem.EmissionModule emission = system.emission;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 12),
            new ParticleSystem.Burst(0.25f, 6)
        });

        FlatCircle(system, 1f, 1f);

        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 1.5f;
        noise.scrollSpeed = 0.3f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        SizeOverLifetime(system, AnimationCurve.Linear(0f, 0.6f, 1f, 1.1f));
        FadeOverLifetime(system, 0.05f, 0.8f);
        SetRenderer(system, material);
    }

    // Editor only: the first material that exists out of the given project paths, then URP's default particle material.
    public static Material LoadMaterial(params string[] paths)
    {
#if UNITY_EDITOR
        foreach (string path in paths)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
        }

        return AssetDatabase.LoadAssetAtPath<Material>(UrpParticleMaterial);
#else
        return null;
#endif
    }

    // How long until every system under root has finished, so the effect can be destroyed safely
    public static float TotalDuration(ParticleSystem root)
    {
        float longest = 0f;

        foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = system.main;
            longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
        }

        return longest + 0.5f;
    }

    // Burst counts as authored in the prefab, so they can be scaled at runtime without drifting
    public static int[] ReadBursts(ParticleSystem system)
    {
        if (system == null) return new int[0];

        ParticleSystem.EmissionModule emission = system.emission;
        int[] counts = new int[emission.burstCount];

        for (int i = 0; i < counts.Length; i++)
            counts[i] = Mathf.RoundToInt(emission.GetBurst(i).count.constantMax);

        return counts;
    }

    public static void ScaleBursts(ParticleSystem system, int[] authored, float scale)
    {
        if (system == null) return;

        ParticleSystem.EmissionModule emission = system.emission;

        for (int i = 0; i < authored.Length && i < emission.burstCount; i++)
        {
            ParticleSystem.Burst burst = emission.GetBurst(i);
            burst.count = Mathf.Max(1, Mathf.RoundToInt(authored[i] * scale));
            emission.SetBurst(i, burst);
        }
    }
}