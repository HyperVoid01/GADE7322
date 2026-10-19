using UnityEngine;
using UnityEngine.Splines;

// Put this on a Particle System prefab. PathParticleSpawner creates one copy per enemy path,
// and each copy places its own particles along that path's spline every frame.
[RequireComponent(typeof(ParticleSystem))]
public class PathParticleFollower : MonoBehaviour
{
    [Header("Flow")]
    [Tooltip("How fast particles travel along the path, in world units per second")]
    [SerializeField] private float flowSpeed = 4f;
    [Tooltip("On: flows from the cave toward the map center (the way enemies walk). Off: the other way.")]
    [SerializeField] private bool flowTowardCenter = true;
    [Tooltip("How many particles are alive per unit of path length")]
    [SerializeField] private float particlesPerUnitLength = 1f;

    [Header("Spread")]
    [Tooltip("Fraction of the tunnel's width the particles can use")]
    [SerializeField, Range(0f, 1.5f)] private float widthScale = 0.8f;
    [Tooltip("Fraction of the tunnel's height the particles can use")]
    [SerializeField, Range(0f, 1.5f)] private float heightScale = 0.8f;
    [Tooltip("Sideways and up/down drift, in world units")]
    [SerializeField] private float wobbleAmount = 0.4f;
    [SerializeField] private float wobbleSpeed = 1.5f;

    private const int SampleCount = 96;

    private ParticleSystem _system;
    private ParticleSystem.Particle[] _particles;

    // The path is sampled once, so moving particles never has to evaluate the spline
    private readonly Vector3[] _points = new Vector3[SampleCount + 1];
    private readonly Vector3[] _tangents = new Vector3[SampleCount + 1];

    private float _radius;
    private float _halfHeight;
    private float _taperFraction;
    private bool _ready;

    public void Initialize(SplineContainer container)
    {
        _system = GetComponent<ParticleSystem>();

        if (container == null || MarchingCubes.Instance == null) return;

        // Sample the path in world space
        Spline spline = container.Spline;
        for (int i = 0; i <= SampleCount; i++)
        {
            Vector3 local = spline.EvaluatePosition((float)i / SampleCount);
            _points[i] = container.transform.TransformPoint(local);
        }

        float length = 0f;
        for (int i = 0; i <= SampleCount; i++)
        {
            Vector3 before = _points[Mathf.Max(i - 1, 0)];
            Vector3 after = _points[Mathf.Min(i + 1, SampleCount)];
            Vector3 direction = after - before;
            _tangents[i] = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

            if (i > 0) length += Vector3.Distance(_points[i - 1], _points[i]);
        }

        if (length <= 0f) return;

        // Match the carved tunnel, including the way it narrows to nothing at both ends
        MarchingCubes terrain = MarchingCubes.Instance;
        _radius = terrain.PathRadius * widthScale;
        _halfHeight = terrain.PathHeight * heightScale;
        _taperFraction = Mathf.Max(terrain.PathEndTaperFraction, 0.01f);

        ConfigureSystem(length);

        _ready = true;
    }

    // Sets up the parts of the particle system this script depends on, so the prefab only
    // needs the look (material, size, color, trails)
    private void ConfigureSystem(float length)
    {
        // A particle lives exactly as long as it takes to travel the whole path
        float lifetime = length / Mathf.Max(flowSpeed, 0.01f);
        int expected = Mathf.CeilToInt(length * particlesPerUnitLength);

        // The prefab may have started playing on Instantiate
        _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = _system.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = true;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.9f, lifetime * 1.1f);
        main.maxParticles = Mathf.Max(expected + expected / 2 + 8, 16);

        ParticleSystem.EmissionModule emission = _system.emission;
        emission.enabled = true;
        emission.rateOverTime = expected / lifetime;

        // Particles are spawned exactly on the emitter, which sits at the start of the path
        ParticleSystem.ShapeModule shape = _system.shape;
        shape.enabled = false;

        transform.position = flowTowardCenter ? _points[0] : _points[SampleCount];

        _particles = new ParticleSystem.Particle[main.maxParticles];

        // Fill the whole path straight away instead of letting it build up from the start
        _system.Simulate(lifetime, true, true);
        _system.Play();
    }

    private void LateUpdate()
    {
        if (!_ready) return;

        int count = _system.GetParticles(_particles);

        for (int i = 0; i < count; i++)
        {
            ref ParticleSystem.Particle particle = ref _particles[i];

            // 0 = just spawned, 1 = about to die
            float age = particle.startLifetime - particle.remainingLifetime;
            float progress = Mathf.Clamp01(age / particle.startLifetime);
            float pathT = flowTowardCenter ? progress : 1f - progress;

            SamplePath(pathT, out Vector3 center, out Vector3 tangent);

            Vector3 side = Vector3.Cross(tangent, Vector3.up);
            side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;

            // Each particle keeps its own random spot in the tunnel, from its random seed
            uint seed = particle.randomSeed;
            float lateral = Hash01(seed, 1u) * 2f - 1f;
            float vertical = Hash01(seed, 2u) * 2f - 1f;
            float phase = Hash01(seed, 3u) * Mathf.PI * 2f;

            // Narrow with the tunnel at both ends
            float taper = Mathf.Clamp01(Mathf.Min(pathT, 1f - pathT) / _taperFraction);

            float wobble = Mathf.Sin(age * wobbleSpeed + phase) * wobbleAmount;
            float bob = Mathf.Cos(age * wobbleSpeed * 0.7f + phase) * wobbleAmount * 0.5f;

            particle.position = center
                + side * (lateral * _radius * taper + wobble * taper)
                + Vector3.up * (vertical * _halfHeight * taper + bob * taper);

            // Velocity along the path, so stretched billboards and trails point the right way
            particle.velocity = (flowTowardCenter ? tangent : -tangent) * flowSpeed;
        }

        _system.SetParticles(_particles, count);
    }

    // Looks up a position and direction anywhere along the sampled path
    private void SamplePath(float t, out Vector3 position, out Vector3 tangent)
    {
        float scaled = Mathf.Clamp01(t) * SampleCount;
        int index = Mathf.Min((int)scaled, SampleCount - 1);
        float blend = scaled - index;

        position = Vector3.Lerp(_points[index], _points[index + 1], blend);
        tangent = Vector3.Lerp(_tangents[index], _tangents[index + 1], blend).normalized;
    }

    // Cheap deterministic random number from 0 to 1, so a particle's offset never changes between frames
    private static float Hash01(uint seed, uint salt)
    {
        uint h = seed * 747796405u + salt * 2891336453u;
        h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return (h & 0xFFFFFFu) / 16777216f;
    }
}