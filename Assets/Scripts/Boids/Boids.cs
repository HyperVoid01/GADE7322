using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class Boids : MonoBehaviour
{
    [Header("Boid Data")]
    [SerializeField] private BoidData data;
    [SerializeField] private GameObject boidPrefab;
    [SerializeField] private LayerMask obstacleLayer;
    
    [Header("Spawning")]
    [SerializeField] private float spawnRate;
    [SerializeField] private int numBoids;
    
    [Header("Boundaries (half extents, centred on this GameObject)")]
    [SerializeField] private float xSize;
    [SerializeField] private float ySize;
    [SerializeField] private float zSize;

    [Header("Vertical Control")]
    [Tooltip("Distance from the top/bottom of the box where boids start turning back.")]
    [SerializeField, Min(0.01f)] private float verticalMargin = 5f;
    [Tooltip("How strongly boids turn back from the top/bottom of the box.")]
    [SerializeField] private float verticalBoundaryWeight = 2f;
    [Tooltip("How strongly boids pull back towards level flight.")]
    [SerializeField] private float levelFlightWeight = 0.5f;
    [Tooltip("Steepest angle (degrees) a boid can point up or down.")]
    [SerializeField, Range(5f, 85f)] private float maxPitchAngle = 45f;

    [Header("Smoothing")]
    [Tooltip("Maximum turn speed in degrees per second.")]
    [SerializeField, Min(1f)] private float maxTurnSpeed = 120f;
    [Tooltip("How quickly the model's rotation catches up to its heading. Higher = snappier.")]
    [SerializeField, Min(0.1f)] private float rotationSmoothing = 8f;
    [Tooltip("How quickly cached obstacle avoidance blends to new SphereCast results. Higher = snappier.")]
    [SerializeField, Min(0.1f)] private float avoidanceSmoothing = 10f;

    [Header("Update Rules")]
    [SerializeField] private float cycleRate;
    [SerializeField] private bool separationRule;
    [SerializeField] private bool alignmentRule;
    [SerializeField] private bool cohesionRule;
    [SerializeField] private bool avoidObstacles;

    [Header("Audio")]
    [Tooltip("Random wait (min, max seconds) between bubbles from the school")]
    [SerializeField] private Vector2 bubbleInterval = new Vector2(2f, 5f);

    [Header("Performance")]
    [Tooltip("Each boid does its SphereCast once every N cycles. Higher = cheaper, slightly slower reactions.")]
    [SerializeField, Min(1)] private int obstacleCheckInterval = 3;

    // Hard cap on grid cells so a tiny perception radius can't create a huge grid
    private const int MaxCells = 2_000_000;

    // Cached boid data (index i is the same boid in every array)
    private Transform[] _transforms;
    private Vector3[] _positions;
    private Vector3[] _forwards;
    private Vector3[] _newForwards;
    private Vector3[] _avoidance;        // smoothed avoidance actually applied
    private Vector3[] _avoidanceTarget;  // latest SphereCast result
    private Quaternion[] _rotations;     // smoothed visual rotation
    private int _count;

    // Spatial hash grid (linked list per cell, allocation-free)
    private int[] _cellHead;     // first boid index in each cell, -1 = empty
    private int[] _nextInCell;   // next boid index in the same cell, -1 = end
    private int _cellsX, _cellsY, _cellsZ;
    private float _cellSize;
    private float _invCellSize;
    private Vector3 _gridMin;

    private int _cycleIndex;

    private Vector3 Center => transform.position;
    private Vector3 HalfExtents => new Vector3(xSize, ySize, zSize);

    private void Start()
    {
        StartCoroutine(SpawnBoids());
    }

    // ---------------- Spawning ----------------

    private IEnumerator SpawnBoids()
    {
        _transforms = new Transform[numBoids];
        _positions = new Vector3[numBoids];
        _forwards = new Vector3[numBoids];
        _newForwards = new Vector3[numBoids];
        _avoidance = new Vector3[numBoids];
        _avoidanceTarget = new Vector3[numBoids];
        _rotations = new Quaternion[numBoids];
        _nextInCell = new int[numBoids];
        _count = 0;

        for (int i = 0; i < numBoids; i++)
        {
            Vector3 offset = new Vector3(
                Random.Range(-xSize, xSize),
                Random.Range(-ySize, ySize),
                Random.Range(-zSize, zSize));

            // Spawns facing a mostly horizontal direction
            Vector3 startForward = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.2f, 0.2f), Random.Range(-1f, 1f));
            if (startForward.sqrMagnitude < 0.0001f)
                startForward = Vector3.forward;

            startForward = ClampPitch(startForward.normalized, Vector3.forward);
            Quaternion startRotation = Quaternion.LookRotation(startForward);

            GameObject boid = Instantiate(boidPrefab, Center + offset, startRotation, transform);

            _transforms[i] = boid.transform;
            _positions[i] = boid.transform.position;
            _forwards[i] = startForward;
            _rotations[i] = startRotation;
            _count++;

            if (spawnRate > 0f)
                yield return new WaitForSeconds(spawnRate);
        }

        BuildGridLayout();
        StartCoroutine(BoidCycle());
        StartCoroutine(FishBubbleRoutine());
    }

    // ---------------- Audio ----------------

    // Every few seconds a random fish lets out a few bubbles. Distant fish fade out naturally (3D sound).
    private IEnumerator FishBubbleRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(bubbleInterval.x, bubbleInterval.y));

            if (_count > 0 && SfxPlayer.Instance != null)
                SfxPlayer.Instance.PlayFishBubbles(_positions[Random.Range(0, _count)]);
        }
    }

    // ---------------- Main loop ----------------

    private IEnumerator BoidCycle()
    {
        float lastTime = Time.time;

        while (true)
        {
            float elapsed = Time.time - lastTime;
            lastTime = Time.time;

            PopulateGrid();
            UpdateDirections(elapsed);
            MoveBoids(elapsed);
            _cycleIndex++;

            if (cycleRate > 0f)
                yield return new WaitForSeconds(cycleRate);
            else
                yield return null;
        }
    }

    // ---------------- Grid ----------------

    // Works out cell size and grid dimensions. Cells are at least PerceptionRadius wide,
    // so checking the 3x3x3 block around a boid covers its whole perception sphere.
    private void BuildGridLayout()
    {
        Vector3 size = HalfExtents * 2f;
        _cellSize = Mathf.Max(data.PerceptionRadius, 0.5f);

        while (true)
        {
            _cellsX = Mathf.Max(1, Mathf.CeilToInt(size.x / _cellSize));
            _cellsY = Mathf.Max(1, Mathf.CeilToInt(size.y / _cellSize));
            _cellsZ = Mathf.Max(1, Mathf.CeilToInt(size.z / _cellSize));

            if ((long)_cellsX * _cellsY * _cellsZ <= MaxCells)
                break;

            _cellSize *= 2f;
        }

        _invCellSize = 1f / _cellSize;
        _cellHead = new int[_cellsX * _cellsY * _cellsZ];
    }

    // Drops every boid into its cell
    private void PopulateGrid()
    {
        _gridMin = Center - HalfExtents;
        Array.Fill(_cellHead, -1);

        for (int i = 0; i < _count; i++)
        {
            GetCellCoords(_positions[i], out int cx, out int cy, out int cz);
            int cell = CellIndex(cx, cy, cz);

            _nextInCell[i] = _cellHead[cell];
            _cellHead[cell] = i;
        }
    }

    private void GetCellCoords(Vector3 position, out int cx, out int cy, out int cz)
    {
        Vector3 local = position - _gridMin;
        cx = Mathf.Clamp((int)(local.x * _invCellSize), 0, _cellsX - 1);
        cy = Mathf.Clamp((int)(local.y * _invCellSize), 0, _cellsY - 1);
        cz = Mathf.Clamp((int)(local.z * _invCellSize), 0, _cellsZ - 1);
    }

    private int CellIndex(int x, int y, int z)
    {
        return x + y * _cellsX + z * _cellsX * _cellsY;
    }

    // ---------------- Rules ----------------

    private void UpdateDirections(float deltaTime)
    {
        float radiusSqr = data.PerceptionRadius * data.PerceptionRadius;
        float cosAngle = Mathf.Cos(Mathf.Clamp(data.PerceptionAngle, 0f, 180f) * Mathf.Deg2Rad);
        float maxTurnRadians = maxTurnSpeed * Mathf.Deg2Rad * deltaTime;
        float avoidanceBlend = 1f - Mathf.Exp(-avoidanceSmoothing * deltaTime);
        Vector3 center = Center;

        for (int i = 0; i < _count; i++)
        {
            Vector3 position = _positions[i];
            Vector3 forward = _forwards[i];

            Vector3 separation = Vector3.zero;
            Vector3 alignmentSum = Vector3.zero;
            Vector3 centerSum = Vector3.zero;
            int neighbours = 0;

            GetCellCoords(position, out int cx, out int cy, out int cz);

            // Only checks the 27 cells around this boid instead of every boid
            for (int dz = -1; dz <= 1; dz++)
            {
                int z = cz + dz;
                if (z < 0 || z >= _cellsZ) continue;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= _cellsY) continue;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= _cellsX) continue;

                        int j = _cellHead[CellIndex(x, y, z)];

                        while (j != -1)
                        {
                            if (j != i)
                            {
                                Vector3 toOther = _positions[j] - position;
                                float sqrDistance = toOther.sqrMagnitude;

                                if (sqrDistance <= radiusSqr && sqrDistance > 0.000001f)
                                {
                                    float distance = Mathf.Sqrt(sqrDistance);

                                    // Cone vision: angle between forward and direction to other boid
                                    if (Vector3.Dot(forward, toOther) >= cosAngle * distance)
                                    {
                                        neighbours++;

                                        // Away from other, weighted by 1 / distance
                                        separation -= toOther / sqrDistance;
                                        alignmentSum += _forwards[j];
                                        centerSum += _positions[j];
                                    }
                                }
                            }

                            j = _nextInCell[j];
                        }
                    }
                }
            }

            Vector3 steer = Vector3.zero;

            if (neighbours > 0)
            {
                if (separationRule)
                    steer += (forward + separation).normalized * data.SeparationWeight;

                if (alignmentRule)
                    steer += ((forward + alignmentSum) / neighbours).normalized * data.AlignmentWeight;

                if (cohesionRule)
                    steer += (centerSum / neighbours - position).normalized * data.CohesionWeight;
            }

            if (avoidObstacles)
            {
                // Staggers SphereCasts across cycles
                if ((i + _cycleIndex) % obstacleCheckInterval == 0)
                    _avoidanceTarget[i] = ObstacleAvoidance(position, forward);

                // Blends towards the latest result instead of jumping
                _avoidance[i] = Vector3.Lerp(_avoidance[i], _avoidanceTarget[i], avoidanceBlend);

                steer += _avoidance[i] * data.ObstacleAvoidanceWeight;
            }

            steer += VerticalBoundary(position.y - center.y) * verticalBoundaryWeight;

            // Pulls heading back towards horizontal
            steer += new Vector3(0f, -forward.y, 0f) * levelFlightWeight;

            // Steer is NOT normalised, so small forces cause small turns
            Vector3 desired = forward + steer;
            Vector3 newForward = forward;

            if (desired.sqrMagnitude > 0.000001f)
            {
                // Caps how far the boid can turn this cycle
                newForward = Vector3.RotateTowards(forward, desired.normalized, maxTurnRadians, 0f);
            }

            // Written to a separate buffer so every boid reads the same snapshot this cycle
            _newForwards[i] = ClampPitch(newForward, forward);
        }
    }

    // Stops boids pointing too steeply up or down (also prevents LookRotation flipping)
    private Vector3 ClampPitch(Vector3 direction, Vector3 fallback)
    {
        Vector3 horizontal = new Vector3(direction.x, 0f, direction.z);

        if (horizontal.sqrMagnitude < 0.000001f)
        {
            horizontal = new Vector3(fallback.x, 0f, fallback.z);

            if (horizontal.sqrMagnitude < 0.000001f)
                horizontal = Vector3.forward;
        }

        horizontal.Normalize();

        float maxPitch = maxPitchAngle * Mathf.Deg2Rad;
        float pitch = Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f));
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        return horizontal * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
    }

    // Steers down near the top of the box and up near the bottom, stronger the closer it gets
    private Vector3 VerticalBoundary(float localY)
    {
        float top = ySize - verticalMargin;
        float bottom = -ySize + verticalMargin;

        if (localY > top)
            return Vector3.down * Mathf.Clamp01((localY - top) / verticalMargin);

        if (localY < bottom)
            return Vector3.up * Mathf.Clamp01((bottom - localY) / verticalMargin);

        return Vector3.zero;
    }

    private Vector3 ObstacleAvoidance(Vector3 position, Vector3 forward)
    {
        if (!Physics.SphereCast(position, data.ObstacleAvoidanceRadius, forward,
                out RaycastHit hit, data.PerceptionRadius, obstacleLayer, QueryTriggerInteraction.Ignore))
            return Vector3.zero;

        // Sphere already overlapping something at the start of the cast, turn around
        if (hit.distance <= 0f)
            return -forward;

        // Stronger the closer the obstacle is
        float closeness = 1f - hit.distance / data.PerceptionRadius;

        // Slide along the surface instead of bouncing straight off it
        Vector3 slide = Vector3.ProjectOnPlane(forward, hit.normal);
        Vector3 avoid = (hit.normal + slide).normalized;

        return avoid * closeness;
    }

    // ---------------- Movement ----------------

    private void MoveBoids(float deltaTime)
    {
        Vector3 center = Center;
        float rotationBlend = 1f - Mathf.Exp(-rotationSmoothing * deltaTime);

        for (int i = 0; i < _count; i++)
        {
            Vector3 forward = _newForwards[i];
            _forwards[i] = forward;

            Vector3 local = _positions[i] + forward * (data.MoveSpeed * deltaTime) - center;

            // X and Z wrap to the opposite side
            if (local.x > xSize) local.x = -xSize;
            else if (local.x < -xSize) local.x = xSize;

            if (local.z > zSize) local.z = -zSize;
            else if (local.z < -zSize) local.z = zSize;

            // Y is a hard limit as a safety net, the soft boundary should turn them first
            local.y = Mathf.Clamp(local.y, -ySize, ySize);

            Vector3 position = center + local;
            _positions[i] = position;

            // Model eases towards the heading, hides any leftover frame-to-frame noise
            _rotations[i] = Quaternion.Slerp(_rotations[i], Quaternion.LookRotation(forward, Vector3.up), rotationBlend);

            // One transform write per boid per cycle
            _transforms[i].SetPositionAndRotation(position, _rotations[i]);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(transform.position, HalfExtents * 2f);

        // Inner box showing where vertical turning starts
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, new Vector3(xSize * 2f, Mathf.Max(0f, (ySize - verticalMargin) * 2f), zSize * 2f));
    }
}