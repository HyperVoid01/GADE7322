using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(100)] // Runs after MarchingCubes.Awake so Instance is guaranteed to exist
public class ResourceSpawner : MonoBehaviour
{
    [System.Serializable]
    private class Resource
    {
        [FormerlySerializedAs("oreName")]
        public string resourceName;

        [Tooltip("One of these is picked at random for every spawn")]
        public GameObject[] prefabs;

        [Min(0f), Tooltip("Higher values are more common")]
        public float rarity = 1f;

        [Min(0f), Tooltip("Minimum distance from any other resource. Two resources keep the larger of their two values apart. 0 = no spacing.")]
        public float minSpacing = 2f;

        [Header("Underground Height Range")]
        [Tooltip("Only used when Spawn On Surface is off")]
        public float minHeight;
        public float maxHeight;

        [Header("Surface Spawning")]
        [Tooltip("Place on top of the terrain instead of at a random point in the height range")]
        public bool spawnOnSurface;

        [Tooltip("Tilt the object to match the slope. Off = stays upright.")]
        public bool alignToSurface = true;

        [Tooltip("Pushes the object along the surface normal. Use a small negative value to sink it into the ground.")]
        public float surfaceOffset;

        [Tooltip("Only spawn where the terrain surface is within the range below (world height)")]
        public bool limitSurfaceHeight;
        public float surfaceMinHeight;
        public float surfaceMaxHeight = 100f;

        public bool CanSpawn => rarity > 0f && HasAnyPrefab();

        private bool HasAnyPrefab()
        {
            if (prefabs == null) return false;

            foreach (GameObject prefab in prefabs)
            {
                if (prefab != null) return true;
            }

            return false;
        }

        // Random variant, skipping any empty slots in the array
        public GameObject GetRandomPrefab()
        {
            int start = Random.Range(0, prefabs.Length);

            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject prefab = prefabs[(start + i) % prefabs.Length];
                if (prefab != null) return prefab;
            }

            return null;
        }
    }

    // Counts why spots were rejected, so a failure can be diagnosed
    private class SpawnStats
    {
        public int misses;
        public int heightRejects;
        public int protectedRejects;
        public int spacingRejects;
    }

    // Remembers where resources were placed so new ones can keep their distance.
    // Positions are bucketed into cells, so a check only looks at nearby resources instead of all of them.
    private class SpacingGrid
    {
        private struct Placed
        {
            public Vector3 position;
            public float spacing;
        }

        private readonly Dictionary<Vector3Int, List<Placed>> _cells = new Dictionary<Vector3Int, List<Placed>>();
        private readonly float _cellSize;

        public SpacingGrid(float cellSize)
        {
            _cellSize = Mathf.Max(cellSize, 0.01f);
        }

        private Vector3Int CellOf(Vector3 p)
        {
            return new Vector3Int(
                Mathf.FloorToInt(p.x / _cellSize),
                Mathf.FloorToInt(p.y / _cellSize),
                Mathf.FloorToInt(p.z / _cellSize));
        }

        public bool IsFree(Vector3 position, float spacing)
        {
            Vector3Int cell = CellOf(position);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!_cells.TryGetValue(cell + new Vector3Int(dx, dy, dz), out List<Placed> list)) continue;

                        foreach (Placed other in list)
                        {
                            float required = Mathf.Max(spacing, other.spacing);
                            if ((other.position - position).sqrMagnitude < required * required)
                                return false;
                        }
                    }
                }
            }

            return true;
        }

        public void Add(Vector3 position, float spacing)
        {
            Vector3Int cell = CellOf(position);

            if (!_cells.TryGetValue(cell, out List<Placed> list))
            {
                list = new List<Placed>();
                _cells.Add(cell, list);
            }

            list.Add(new Placed { position = position, spacing = spacing });
        }
    }

    [Header("Resource Types")]
    [SerializeField] private List<Resource> oreTypes = new List<Resource>();
    [SerializeField] private List<Resource> coralTypes = new List<Resource>();
    [SerializeField] private List<Resource> shipWreckTypes = new List<Resource>();

    [Header("Spawn Settings")]
    [SerializeField, Min(0)] private int totalOreCount;
    [SerializeField, Min(0)] private int totalCoralCount;
    [SerializeField, Min(0)] private int totalShipWreckCount;
    [SerializeField] private Transform resourceParent;

    [Header("Placement Settings")]
    [SerializeField] private LayerMask terrainLayer;
    [FormerlySerializedAs("maxSurfaceAttempts")]
    [Tooltip("How many random spots to try per resource before giving up on it")]
    [SerializeField, Min(1)] private int maxPlacementAttempts = 30;
    [Tooltip("Keeps resources (surface and underground) off the enemy paths and the center plaza")]
    [SerializeField] private bool avoidProtectedAreas = true;
    [Tooltip("Extra distance kept clear around the center plaza, on top of its protected radius")]
    [SerializeField, Min(0f)] private float centerMargin = 5f;

    private Bounds _terrainBounds;
    private Vector3 _origin;
    private Vector3 _mapSize;
    private SpacingGrid _spacingGrid; // Null when no resource uses spacing

    private void Awake()
    {
        if (MarchingCubes.Instance == null)
        {
            Debug.LogWarning("ResourceSpawner: no MarchingCubes found, nothing will spawn.");
            return;
        }

        MarchingCubes.Instance.OnTerrainGenerated += SpawnAll;
    }

    private void OnDestroy()
    {
        if (MarchingCubes.Instance != null)
            MarchingCubes.Instance.OnTerrainGenerated -= SpawnAll;
    }

    private void SpawnAll()
    {
        MarchingCubes.Instance.OnTerrainGenerated -= SpawnAll;

        // The terrain colliders were just created, so make sure the physics scene knows about them before raycasting
        Physics.SyncTransforms();

        if (terrainLayer.value == 0)
            Debug.LogWarning("ResourceSpawner: Terrain Layer is empty, so surface raycasts can never hit anything.");

        _origin = MarchingCubes.Instance.transform.position;
        _mapSize = MarchingCubes.Instance.WorldSize;

        // Use the real extent of the generated terrain rather than assuming where it sits
        if (!TryGetTerrainBounds(out _terrainBounds))
            _terrainBounds = new Bounds(_origin + _mapSize * 0.5f, _mapSize);

        // One grid shared by every category, so different resource types also keep their distance
        float maxSpacing = Mathf.Max(MaxSpacing(oreTypes), MaxSpacing(coralTypes), MaxSpacing(shipWreckTypes));
        _spacingGrid = maxSpacing > 0f ? new SpacingGrid(maxSpacing) : null;

        SpawnCategory(oreTypes, totalOreCount);
        SpawnCategory(coralTypes, totalCoralCount);
        SpawnCategory(shipWreckTypes, totalShipWreckCount);
    }

    private static float MaxSpacing(List<Resource> types)
    {
        float max = 0f;

        if (types != null)
        {
            foreach (Resource resource in types)
                max = Mathf.Max(max, resource.minSpacing);
        }

        return max;
    }

    // Combines the bounds of every chunk collider
    private bool TryGetTerrainBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;

        foreach (MeshCollider chunkCollider in MarchingCubes.Instance.GetComponentsInChildren<MeshCollider>())
        {
            if (chunkCollider.sharedMesh == null || chunkCollider.sharedMesh.vertexCount == 0) continue;

            if (!found)
            {
                bounds = chunkCollider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(chunkCollider.bounds);
            }
        }

        return found;
    }

    // Spawns totalCount objects split between the resource types by rarity
    private void SpawnCategory(List<Resource> types, int totalCount)
    {
        if (types == null || totalCount <= 0) return;

        // Only resources that can actually spawn take part in the split
        List<Resource> valid = new List<Resource>();
        float totalRarity = 0f;

        foreach (Resource resource in types)
        {
            if (!resource.CanSpawn) continue;

            valid.Add(resource);
            totalRarity += resource.rarity;
        }

        if (valid.Count == 0) return;

        int[] counts = DistributeCounts(valid, totalRarity, totalCount);

        for (int i = 0; i < valid.Count; i++)
        {
            Resource resource = valid[i];
            SpawnStats stats = new SpawnStats();
            int failed = 0;

            for (int n = 0; n < counts[i]; n++)
            {
                if (!TryGetPose(resource, stats, out Vector3 position, out Quaternion rotation))
                {
                    failed++;
                    continue;
                }

                GameObject spawned = Instantiate(resource.GetRandomPrefab(), position, rotation, resourceParent);
                spawned.name = resource.resourceName;

                if (resource.minSpacing > 0f)
                    _spacingGrid?.Add(position, resource.minSpacing);
            }

            if (failed > 0)
            {
                Debug.LogWarning($"ResourceSpawner: {failed} of {counts[i]} '{resource.resourceName}' spawns failed. " +
                                 $"Rejected spots: {stats.spacingRejects} too close to another resource, {stats.misses} raycast misses, " +
                                 $"{stats.heightRejects} outside the surface height range, {stats.protectedRejects} inside protected areas.");
            }
        }
    }

    // Tries random spots until one passes every check
    private bool TryGetPose(Resource resource, SpawnStats stats, out Vector3 position, out Quaternion rotation)
    {
        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            bool found = resource.spawnOnSurface
                ? TryGetSurfaceCandidate(resource, stats, out position, out rotation)
                : TryGetUndergroundCandidate(resource, out position, out rotation);

            if (!found) continue;

            if (avoidProtectedAreas && IsInProtectedArea(position))
            {
                stats.protectedRejects++;
                continue;
            }

            if (resource.minSpacing > 0f && _spacingGrid != null && !_spacingGrid.IsFree(position, resource.minSpacing))
            {
                stats.spacingRejects++;
                continue;
            }

            return true;
        }

        position = default;
        rotation = default;
        return false;
    }

    private bool TryGetUndergroundCandidate(Resource resource, out Vector3 position, out Quaternion rotation)
    {
        position = _origin + new Vector3(
            Random.Range(0f, _mapSize.x),
            Random.Range(resource.minHeight, resource.maxHeight),
            Random.Range(0f, _mapSize.z));

        rotation = Random.rotation;
        return true;
    }

    // One raycast down from above the terrain at a random x/z
    private bool TryGetSurfaceCandidate(Resource resource, SpawnStats stats, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = default;

        Vector3 start = new Vector3(
            Random.Range(_terrainBounds.min.x, _terrainBounds.max.x),
            _terrainBounds.max.y + 5f,
            Random.Range(_terrainBounds.min.z, _terrainBounds.max.z));

        if (!Physics.Raycast(start, Vector3.down, out RaycastHit hit, _terrainBounds.size.y + 10f, terrainLayer, QueryTriggerInteraction.Ignore))
        {
            stats.misses++;
            return false;
        }

        if (resource.limitSurfaceHeight &&
            (hit.point.y < resource.surfaceMinHeight || hit.point.y > resource.surfaceMaxHeight))
        {
            stats.heightRejects++;
            return false;
        }
        
        position = hit.point + hit.normal * resource.surfaceOffset;

        Quaternion yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        rotation = resource.alignToSurface
            ? Quaternion.FromToRotation(Vector3.up, hit.normal) * yaw
            : yaw;

        return true;
    }

    // Splits totalCount by rarity so the counts always add up to exactly totalCount.
    // Rounding each share separately can leave the total one or two off.
    private static int[] DistributeCounts(List<Resource> resources, float totalRarity, int totalCount)
    {
        int[] counts = new int[resources.Count];
        float[] remainders = new float[resources.Count];
        int assigned = 0;

        for (int i = 0; i < resources.Count; i++)
        {
            float exact = totalCount * (resources[i].rarity / totalRarity);
            counts[i] = Mathf.FloorToInt(exact);
            remainders[i] = exact - counts[i];
            assigned += counts[i];
        }

        // Hand the leftover spawns to the types with the biggest fractional parts
        for (int left = totalCount - assigned; left > 0; left--)
        {
            int best = 0;
            for (int i = 1; i < remainders.Length; i++)
            {
                if (remainders[i] > remainders[best]) best = i;
            }

            counts[best]++;
            remainders[best] = -1f;
        }

        return counts;
    }
    
    private bool IsInProtectedArea(Vector3 point)
    {
        MarchingCubes terrain = MarchingCubes.Instance;

        // Center plaza at any height, plus the margin
        if (terrain.CenterProtectionRadius > 0f)
        {
            Vector3 center = terrain.MapCenter;
            float dx = point.x - center.x;
            float dz = point.z - center.z;
            float clearRadius = terrain.CenterProtectionRadius + centerMargin;

            if (dx * dx + dz * dz < clearRadius * clearRadius)
                return true;
        }

        return terrain.IsWithinPathProtection(point);
    }
}