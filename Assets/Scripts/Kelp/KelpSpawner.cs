using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public class KelpSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject kelpPrefab;
    [SerializeField] private MarchingCubes marchingCubes; // chunks are children of this object
    [SerializeField] private LayerMask terrainLayer;

    [Header("Clusters")]
    [SerializeField] private int clusterCount = 40;
    [SerializeField] private Vector2Int kelpPerCluster = new Vector2Int(4, 12);
    [SerializeField] private float clusterRadius = 3f;
    [SerializeField] private int maxAttemptsPerKelp = 5;

    [Header("Placement")]
    [SerializeField, Range(0f, 90f)] private float maxSlope = 35f;
    [SerializeField] private float sinkDepth = 0.15f; // pushes the base into the ground so it never floats on slopes
    [SerializeField, Range(0f, 1f)] private float surfaceAlignment = 0.2f; // 0 = always straight up, 1 = follows ground normal

    [Header("Blocked Areas")]
    [SerializeField] private float pathClearance = 3f; // horizontal distance kept clear either side of enemy paths
    [SerializeField] private float pathSampleSpacing = 0.5f; // distance between cached points along each path
    [SerializeField] private float centerClearRadius = 10f; // keeps the center plaza clear
    [SerializeField] private LayerMask obstacleLayers; // towers, ores, main tower, etc.
    [SerializeField] private float obstacleClearance = 1f;
    [SerializeField] private bool drawBlockedGizmos = true;

    [Header("Height Variation")]
    [SerializeField] private Vector2 heightScaleRange = new Vector2(0.6f, 1.8f);
    [SerializeField, Range(0.2f, 3f)] private float heightBias = 1.3f; // above 1 favours shorter kelp, below 1 favours taller
    [SerializeField] private bool tallerAtClusterCenter = true;
    [SerializeField, Range(0f, 1f)] private float centerHeightInfluence = 0.5f;

    [Header("Shape Variation")]
    [SerializeField] private Vector2 widthScaleRange = new Vector2(0.8f, 1.2f);
    [SerializeField, Range(0f, 20f)] private float maxTilt = 8f;

    [Header("Sway Variation")]
    [SerializeField] private bool scaleSwayWithHeight = true; // taller kelp bends further

    [Header("Mining Cleanup")]
    [SerializeField] private float groundCheckPadding = 1f; // extra radius around a dig checked for floating kelp
    [SerializeField] private float groundTolerance = 0.3f; // how far the ground may drop before kelp counts as floating

    public static KelpSpawner Instance;

    private readonly List<Transform> _spawnedKelp = new List<Transform>();
    private readonly List<Vector2> _pathPoints = new List<Vector2>(); // XZ samples of every enemy path
    private Bounds _terrainBounds;
    private Vector2 _terrainCenterXZ;
    private MaterialPropertyBlock _propertyBlock;

    private static readonly int SwayStrengthId = Shader.PropertyToID("_SwayStrength");

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _propertyBlock = new MaterialPropertyBlock();

        if (marchingCubes == null)
        {
            Debug.LogError("KelpSpawner: MarchingCubes reference is not assigned.", this);
            return;
        }

        // Subscribed in Awake so we don't miss the event fired from MarchingCubes.Start
        marchingCubes.OnTerrainGenerated += HandleTerrainGenerated;
    }

    private void OnDestroy()
    {
        if (marchingCubes != null)
            marchingCubes.OnTerrainGenerated -= HandleTerrainGenerated;

        if (Instance == this)
            Instance = null;
    }

    private void HandleTerrainGenerated()
    {
        ClearKelp();

        // Make sure freshly assigned MeshColliders are registered before raycasting
        Physics.SyncTransforms();

        if (!CalculateTerrainBounds())
        {
            Debug.LogWarning("KelpSpawner: no terrain renderers found under the MarchingCubes object.", this);
            return;
        }

        CachePathPoints();
        SpawnClusters();
    }

    // ---------- Spawning ----------

    private void SpawnClusters()
    {
        for (int c = 0; c < clusterCount; c++)
        {
            if (!TryFindClusterCenter(out RaycastHit centerHit))
                continue;

            int count = Random.Range(kelpPerCluster.x, kelpPerCluster.y + 1);

            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < maxAttemptsPerKelp; attempt++)
                {
                    Vector2 offset = Random.insideUnitCircle * clusterRadius;
                    Vector3 candidate = centerHit.point + new Vector3(offset.x, 0f, offset.y);

                    if (!TryFindSpawnPoint(candidate, out RaycastHit hit))
                        continue;

                    // 0 at the cluster center, 1 at the edge
                    float distance01 = clusterRadius > 0f ? offset.magnitude / clusterRadius : 0f;
                    SpawnKelp(hit, distance01);
                    break;
                }
            }
        }
    }

    private bool TryFindClusterCenter(out RaycastHit hit)
    {
        for (int attempt = 0; attempt < maxAttemptsPerKelp; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(_terrainBounds.min.x, _terrainBounds.max.x),
                0f,
                Random.Range(_terrainBounds.min.z, _terrainBounds.max.z));

            if (TryFindSpawnPoint(candidate, out hit))
                return true;
        }

        hit = default;
        return false;
    }

    // Casts straight down onto the terrain at the candidate's XZ position
    private bool TryFindSpawnPoint(Vector3 candidate, out RaycastHit hit)
    {
        Vector3 origin = new Vector3(candidate.x, _terrainBounds.max.y + 1f, candidate.z);
        float distance = _terrainBounds.size.y + 2f;

        if (!Physics.Raycast(origin, Vector3.down, out hit, distance, terrainLayer, QueryTriggerInteraction.Ignore))
            return false;

        if (Vector3.Angle(hit.normal, Vector3.up) > maxSlope)
            return false;

        if (IsBlocked(hit.point))
            return false;

        return true;
    }

    // ---------- Blocked areas ----------

    private bool IsBlocked(Vector3 point)
    {
        Vector2 pointXZ = new Vector2(point.x, point.z);

        // Center plaza
        if ((pointXZ - _terrainCenterXZ).sqrMagnitude <= centerClearRadius * centerClearRadius)
            return true;

        // Enemy paths, horizontal distance only so height differences along the path don't matter
        float sqrPathClearance = pathClearance * pathClearance;
        for (int i = 0; i < _pathPoints.Count; i++)
        {
            if ((pointXZ - _pathPoints[i]).sqrMagnitude <= sqrPathClearance)
                return true;
        }

        // Towers, ores and anything else on the obstacle layers
        if (obstacleLayers.value != 0 &&
            Physics.CheckSphere(point, obstacleClearance, obstacleLayers, QueryTriggerInteraction.Ignore))
            return true;

        return false;
    }

    // Samples every spline in the scene once, so IsBlocked only does cheap distance checks
    private void CachePathPoints()
    {
        _pathPoints.Clear();

        SplineContainer[] containers = FindObjectsByType<SplineContainer>(FindObjectsSortMode.None);
        float spacing = Mathf.Max(0.05f, pathSampleSpacing);

        foreach (SplineContainer container in containers)
        {
            for (int s = 0; s < container.Splines.Count; s++)
            {
                float length = container.CalculateLength(s);
                int sampleCount = Mathf.Max(2, Mathf.CeilToInt(length / spacing) + 1);

                for (int i = 0; i < sampleCount; i++)
                {
                    float t = i / (float)(sampleCount - 1);
                    Vector3 worldPoint = container.EvaluatePosition(s, t); // world space
                    _pathPoints.Add(new Vector2(worldPoint.x, worldPoint.z));
                }
            }
        }

        if (_pathPoints.Count == 0)
            Debug.LogWarning("KelpSpawner: no SplineContainers found, kelp may spawn on enemy paths.", this);
    }

    // ---------- Kelp instance ----------

    private void SpawnKelp(RaycastHit hit, float distance01)
    {
        // ----- Height -----
        // Random value biased towards short or tall
        float heightT = Mathf.Pow(Random.value, heightBias);

        // Blend towards "tall in the middle, short at the edges" for natural looking patches
        if (tallerAtClusterCenter)
            heightT = Mathf.Lerp(heightT, 1f - distance01, centerHeightInfluence);

        float heightScale = Mathf.Lerp(heightScaleRange.x, heightScaleRange.y, heightT);
        float widthScale = Random.Range(widthScaleRange.x, widthScaleRange.y);

        // ----- Rotation -----
        Vector3 growDirection = Vector3.Slerp(Vector3.up, hit.normal, surfaceAlignment);
        Quaternion alignToGround = Quaternion.FromToRotation(Vector3.up, growDirection);
        Quaternion tilt = Quaternion.Euler(Random.Range(-maxTilt, maxTilt), 0f, Random.Range(-maxTilt, maxTilt));
        Quaternion yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        Quaternion rotation = alignToGround * tilt * yaw;

        // ----- Position -----
        Vector3 position = hit.point - (rotation * Vector3.up) * sinkDepth;

        GameObject kelp = Instantiate(kelpPrefab, position, rotation, transform);

        Vector3 baseScale = kelpPrefab.transform.localScale;
        kelp.transform.localScale = new Vector3(
            baseScale.x * widthScale,
            baseScale.y * heightScale,
            baseScale.z * widthScale);

        if (scaleSwayWithHeight)
            ApplySwayScale(kelp, heightScale);

        _spawnedKelp.Add(kelp.transform);
    }

    // The shader's sway offset is in world units, so without this a tall kelp bends
    // the same distance as a short one and looks stiff
    private void ApplySwayScale(GameObject kelp, float heightScale)
    {
        Renderer kelpRenderer = kelp.GetComponentInChildren<Renderer>();
        if (kelpRenderer == null) return;

        Material material = kelpRenderer.sharedMaterial;
        if (material == null || !material.HasProperty(SwayStrengthId)) return;

        kelpRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetFloat(SwayStrengthId, material.GetFloat(SwayStrengthId) * heightScale);
        kelpRenderer.SetPropertyBlock(_propertyBlock);
    }

    // ---------- Bounds ----------

    private bool CalculateTerrainBounds()
    {
        MeshRenderer[] renderers = marchingCubes.GetComponentsInChildren<MeshRenderer>();
        bool hasBounds = false;

        foreach (MeshRenderer meshRenderer in renderers)
        {
            if (!hasBounds)
            {
                _terrainBounds = meshRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                _terrainBounds.Encapsulate(meshRenderer.bounds);
            }
        }

        _terrainCenterXZ = new Vector2(_terrainBounds.center.x, _terrainBounds.center.z);
        return hasBounds;
    }

    // ---------- Mining cleanup ----------

    // Call from TerrainEditor after the chunk mesh and collider have been updated.
    // Kelp inside the dig radius is always removed. Kelp just outside it is removed
    // only if the ground under it has been dug away.
    public void RemoveKelpInRadius(Vector3 center, float radius)
    {
        // Collider changes from this frame must be registered before the ground checks
        Physics.SyncTransforms();

        float sqrRadius = radius * radius;
        float checkRadius = radius + groundCheckPadding;
        float sqrCheckRadius = checkRadius * checkRadius;

        for (int i = _spawnedKelp.Count - 1; i >= 0; i--)
        {
            Transform kelp = _spawnedKelp[i];

            if (kelp == null)
            {
                _spawnedKelp.RemoveAt(i);
                continue;
            }

            float sqrDistance = (kelp.position - center).sqrMagnitude;

            if (sqrDistance > sqrCheckRadius)
                continue;

            if (sqrDistance <= sqrRadius || !IsGrounded(kelp))
            {
                Destroy(kelp.gameObject);
                _spawnedKelp.RemoveAt(i);
            }
        }
    }

    // Casts down from just above the kelp's base to check the terrain is still there
    private bool IsGrounded(Transform kelp)
    {
        Vector3 origin = kelp.position + Vector3.up * (sinkDepth + 0.1f);
        float distance = sinkDepth + 0.1f + groundTolerance + sinkDepth;

        return Physics.Raycast(origin, Vector3.down, distance, terrainLayer, QueryTriggerInteraction.Ignore);
    }

    private void ClearKelp()
    {
        foreach (Transform kelp in _spawnedKelp)
        {
            if (kelp != null)
                Destroy(kelp.gameObject);
        }

        _spawnedKelp.Clear();
    }

    // ---------- Gizmos ----------

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.4f, 0.5f);
        Gizmos.DrawWireCube(_terrainBounds.center, _terrainBounds.size);

        if (!drawBlockedGizmos || !Application.isPlaying) return;

        float y = _terrainBounds.max.y;

        // Center plaza
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Vector3 center = new Vector3(_terrainCenterXZ.x, y, _terrainCenterXZ.y);
        Gizmos.DrawWireSphere(center, centerClearRadius);

        // Path clearance, every few samples to keep the scene view readable
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.4f);
        for (int i = 0; i < _pathPoints.Count; i += 4)
        {
            Vector3 point = new Vector3(_pathPoints[i].x, y, _pathPoints[i].y);
            Gizmos.DrawWireSphere(point, pathClearance);
        }
    }
}