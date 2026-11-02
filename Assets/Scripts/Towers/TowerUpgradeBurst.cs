using UnityEngine;

// Plays at the peak of a tower's level-up transition, the moment the old model is swapped for the new one.
// Glowing motes spiral up around the tower, a ring of sparks shoots out along the ground, and bubbles rise.
// Everything is sized to the tower's model and tinted to the tower's glow colour.
//
// Setup: add this to an empty GameObject. It builds its Particle Systems for you (Spiral, Ring, Bubbles).
// Save it as a prefab and assign it to each tower's Upgrade Burst Prefab.
[RequireComponent(typeof(ParticleSystem))]
public class TowerUpgradeBurst : MonoBehaviour
{
    [Header("Parts (built automatically when the component is added)")]
    [SerializeField] private ParticleSystem spiral;
    [SerializeField] private ParticleSystem ring;
    [SerializeField] private ParticleSystem bubbles;

    [Header("Scaling")]
    [Tooltip("Tower radius the effect was authored for")]
    [SerializeField] private float referenceRadius = 1f;
    [Tooltip("How many times the motes circle the tower on their way up")]
    [SerializeField] private float spiralTurns = 0.75f;

    private float _spiralRate;
    private float _ringSpeed;
    private int[] _ringBursts;
    private int[] _bubbleBursts;

    public static TowerUpgradeBurst Spawn(TowerUpgradeBurst prefab, Bounds modelBounds, Color color)
    {
        if (prefab == null) return null;

        TowerUpgradeBurst effect = Instantiate(prefab);
        effect.Play(modelBounds, color);
        return effect;
    }

    private void Awake()
    {
        if (spiral == null) spiral = GetComponent<ParticleSystem>();

        _spiralRate = spiral.emission.rateOverTimeMultiplier;
        if (ring != null) _ringSpeed = ring.main.startSpeedMultiplier;
        _ringBursts = ParticleSetup.ReadBursts(ring);
        _bubbleBursts = ParticleSetup.ReadBursts(bubbles);
    }

    public void Play(Bounds modelBounds, Color color)
    {
        float radius = Mathf.Max(0.3f, Mathf.Max(modelBounds.extents.x, modelBounds.extents.z));
        float height = Mathf.Max(0.5f, modelBounds.size.y);
        float size = radius / Mathf.Max(0.01f, referenceRadius);

        // Sit at the base of the model
        transform.SetPositionAndRotation(
            new Vector3(modelBounds.center.x, modelBounds.min.y, modelBounds.center.z),
            Quaternion.identity);

        ConfigureSpiral(radius, height, size, color);

        if (ring != null)
        {
            SetRadius(ring, radius * 0.3f);
            ParticleSetup.ScaleBursts(ring, _ringBursts, Mathf.Clamp(size, 0.5f, 2.5f));

            ParticleSystem.MainModule main = ring.main;
            main.startSpeedMultiplier = _ringSpeed * size; // Reaches about the same distance past any tower's edge
            main.startColor = color;
        }

        if (bubbles != null)
        {
            SetRadius(bubbles, radius);
            ParticleSetup.ScaleBursts(bubbles, _bubbleBursts, Mathf.Clamp(size, 0.5f, 2.5f));
        }

        spiral.Play(true);
        Destroy(gameObject, ParticleSetup.TotalDuration(spiral));
    }

    // Motes start on a ring around the model and climb exactly its height while orbiting it,
    // so a short tower gets a tight swirl and a tall one gets a long climb
    private void ConfigureSpiral(float radius, float height, float size, Color color)
    {
        SetRadius(spiral, radius * 1.05f);

        ParticleSystem.MainModule main = spiral.main;
        main.startColor = color;

        float lifetime = Mathf.Max(0.1f, (main.startLifetime.constantMin + main.startLifetime.constantMax) * 0.5f);

        ParticleSystem.VelocityOverLifetimeModule velocity = spiral.velocityOverLifetime;
        velocity.y = height / lifetime;
        velocity.orbitalY = spiralTurns * Mathf.PI * 2f / lifetime; // radians per second

        // Wider towers have a longer ring to fill
        ParticleSystem.EmissionModule emission = spiral.emission;
        emission.rateOverTimeMultiplier = _spiralRate * Mathf.Clamp(size, 0.5f, 2.5f);
    }

    private static void SetRadius(ParticleSystem system, float radius)
    {
        ParticleSystem.ShapeModule shape = system.shape;
        shape.radius = Mathf.Max(0.05f, radius);
    }

    // ---------------- Editor setup ----------------

    private void Reset()
    {
        spiral = GetComponent<ParticleSystem>();
        ring = ParticleSetup.GetOrCreateChild(transform, "Ring");
        bubbles = ParticleSetup.GetOrCreateChild(transform, "Bubbles");

        Material spark = ParticleSetup.LoadMaterial("Assets/Materials/Spark.mat");

        BuildSpiral(spiral, spark);
        BuildRing(ring, spark);
        ParticleSetup.Bubbles(bubbles, ParticleSetup.LoadMaterial("Assets/Materials/Bubbles.mat"));
    }

    private static void BuildSpiral(ParticleSystem system, Material material)
    {
        // Local space, so the orbit is around the tower and not the world origin
        ParticleSetup.OneShot(system, 0.5f, 200, ParticleSystemSimulationSpace.Local);

        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
        main.startColor = new Color(0.4f, 1.5f, 1.7f, 1f);

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 140f; // Streams out over the half-second duration

        ParticleSetup.FlatCircle(system, 1f, 0f);

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = 0f;
        velocity.y = 2.5f;     // Set from the tower's height at runtime
        velocity.z = 0f;
        velocity.orbitalY = 4f; // Set from spiralTurns at runtime
        velocity.radial = -0.25f; // Pulls in slightly so the spiral tightens near the top

        ParticleSetup.SizeOverLifetime(system, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));
        ParticleSetup.FadeOverLifetime(system, 0.1f, 0.7f);
        ParticleSetup.Trails(system, 0.25f, material);
        ParticleSetup.SetRenderer(system, material);
    }

    private static void BuildRing(ParticleSystem system, Material material)
    {
        ParticleSetup.OneShot(system, 0.1f, 120);
        system.transform.localPosition = new Vector3(0f, 0.2f, 0f);

        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
        main.startColor = new Color(0.4f, 1.5f, 1.7f, 1f);

        ParticleSystem.EmissionModule emission = system.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 48) });

        ParticleSetup.FlatCircle(system, 0.3f, 0f);

        // Spaces a burst evenly around the circle, so it reads as a clean shockwave ring
        ParticleSystem.ShapeModule shape = system.shape;
        shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;

        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.5f;
        limit.dampen = 0.2f;

        ParticleSetup.SizeOverLifetime(system, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        ParticleSetup.FadeOverLifetime(system, 0f, 0.3f);
        ParticleSetup.Trails(system, 0.4f, material);
        ParticleSetup.SetRenderer(system, material);
    }
}