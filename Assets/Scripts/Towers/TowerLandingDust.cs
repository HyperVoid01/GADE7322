using UnityEngine;

// The sediment cloud kicked up when a tower lands on the sea floor.
// A ring of dust rolls out from the tower's base, chunks of rock bounce off the terrain,
// and bubbles rise from the impact. Size and strength scale with the tower and how fast it landed.
//
// Setup: add this to an empty GameObject. It builds its Particle Systems for you (Dust, Pebbles, Bubbles).
// Tweak them if you like, save it as a prefab, and assign it to TowerBuilder's Landing Dust Prefab.
// Every value is authored for a tower 1 unit in radius landing at full speed; Play() scales from there.
[RequireComponent(typeof(ParticleSystem))]
public class TowerLandingDust : MonoBehaviour
{
    [Header("Parts (built automatically when the component is added)")]
    [SerializeField] private ParticleSystem dust;
    [SerializeField] private ParticleSystem pebbles;
    [SerializeField] private ParticleSystem bubbles;

    [Header("Colours")]
    [SerializeField] private Color sedimentColor = new Color(0.63f, 0.56f, 0.43f, 0.6f);
    [SerializeField] private Color pebbleColor = new Color(0.36f, 0.31f, 0.25f, 1f);

    [Header("Scaling")]
    [Tooltip("Tower radius the effect was authored for")]
    [SerializeField] private float referenceRadius = 1f;
    [Tooltip("Landing speed that gives the full-strength cloud. Slower landings kick up less")]
    [SerializeField] private float fullStrengthSpeed = 35f;
    [Tooltip("Strength never drops below this, so even a gentle landing shows something")]
    [SerializeField, Range(0f, 1f)] private float minStrength = 0.35f;
    [Tooltip("Stops huge towers spawning an absurd number of particles")]
    [SerializeField] private float maxSizeScale = 3f;

    private int[] _dustBursts;
    private int[] _pebbleBursts;
    private int[] _bubbleBursts;
    private float _dustSpeed;
    private float _pebbleSpeed;

    public static TowerLandingDust Spawn(TowerLandingDust prefab, Vector3 position, float footprintRadius, float impactSpeed)
    {
        if (prefab == null) return null;

        TowerLandingDust effect = Instantiate(prefab, position, Quaternion.identity);
        effect.Play(footprintRadius, impactSpeed);
        return effect;
    }

    private void Awake()
    {
        if (dust == null) dust = GetComponent<ParticleSystem>();

        // Remember the authored values so Play() always scales from the same starting point
        _dustBursts = ParticleSetup.ReadBursts(dust);
        _pebbleBursts = ParticleSetup.ReadBursts(pebbles);
        _bubbleBursts = ParticleSetup.ReadBursts(bubbles);

        _dustSpeed = dust.main.startSpeedMultiplier;
        if (pebbles != null) _pebbleSpeed = pebbles.main.startSpeedMultiplier;
    }

    public void Play(float footprintRadius, float impactSpeed)
    {
        float strength = Mathf.Lerp(minStrength, 1f, Mathf.Clamp01(impactSpeed / Mathf.Max(0.01f, fullStrengthSpeed)));
        float size = Mathf.Clamp(footprintRadius / Mathf.Max(0.01f, referenceRadius), 0.25f, maxSizeScale);

        // Dust starts at the edge of the tower and rolls outward.
        // Bigger towers displace more sediment, and a harder landing throws it further.
        if (dust != null)
        {
            SetRadius(dust, footprintRadius);
            ParticleSetup.ScaleBursts(dust, _dustBursts, size * strength);

            ParticleSystem.MainModule main = dust.main;
            main.startSpeedMultiplier = _dustSpeed * Mathf.Sqrt(size) * strength;
            main.startColor = sedimentColor;
        }

        // Rock chips are flung up from under the tower and bounce off the marching cubes mesh
        if (pebbles != null)
        {
            SetRadius(pebbles, footprintRadius * 0.8f);
            ParticleSetup.ScaleBursts(pebbles, _pebbleBursts, size * strength);

            ParticleSystem.MainModule main = pebbles.main;
            main.startSpeedMultiplier = _pebbleSpeed * strength;
            main.startColor = pebbleColor;
        }

        // Bubbles rise from across the whole footprint
        if (bubbles != null)
        {
            SetRadius(bubbles, footprintRadius);
            ParticleSetup.ScaleBursts(bubbles, _bubbleBursts, size * strength);
        }

        dust.Play(true); // true = children play too
        Destroy(gameObject, ParticleSetup.TotalDuration(dust));
    }

    private static void SetRadius(ParticleSystem system, float radius)
    {
        ParticleSystem.ShapeModule shape = system.shape;
        shape.radius = Mathf.Max(0.05f, radius);
    }

    // ---------------- Editor setup ----------------

    // Runs when the component is added (or Reset from the component menu) and builds the effect
    private void Reset()
    {
        dust = GetComponent<ParticleSystem>();
        pebbles = ParticleSetup.GetOrCreateChild(transform, "Pebbles");
        bubbles = ParticleSetup.GetOrCreateChild(transform, "Bubbles");

        Material particleMaterial = ParticleSetup.LoadMaterial();

        BuildDust(dust, particleMaterial);
        BuildPebbles(pebbles, particleMaterial);
        ParticleSetup.Bubbles(bubbles, ParticleSetup.LoadMaterial("Assets/Materials/Bubbles.mat"));
    }

    private void BuildDust(ParticleSystem system, Material material)
    {
        ParticleSetup.OneShot(system, 0.2f, 300);

        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = sedimentColor;
        main.gravityModifier = -0.02f; // Hangs in the water and drifts up a little

        ParticleSystem.EmissionModule emission = system.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });

        // Edge of a flat ring: the cloud pushes out from under the tower's rim
        ParticleSetup.FlatCircle(system, 1f, 0.15f);

        // Water drag: fast at first, then the cloud slows and billows
        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.3f;
        limit.dampen = 0.08f;

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = 0f;
        velocity.y = 0.25f;
        velocity.z = 0f;

        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.15f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

        ParticleSetup.SizeOverLifetime(system, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.8f));
        ParticleSetup.FadeOverLifetime(system, 0.1f, 0.45f);
        ParticleSetup.SetRenderer(system, material);
    }

    private void BuildPebbles(ParticleSystem system, Material material)
    {
        ParticleSetup.OneShot(system, 0.1f, 80);

        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = pebbleColor;
        main.gravityModifier = 0.9f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

        // Upward cone, pointing out of the ground
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 0.8f;
        shape.radiusThickness = 1f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        // Chips land and bounce on the actual terrain mesh
        ParticleSystem.CollisionModule collision = system.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.Medium;
        collision.bounce = 0.25f;
        collision.dampen = 0.4f;
        collision.lifetimeLoss = 0f;

        // Water slows them down
        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = 1f;

        ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

        ParticleSetup.FadeOverLifetime(system, 0f, 0.8f);
        ParticleSetup.SetRenderer(system, material, ParticleSystemRenderMode.Mesh);
    }
}