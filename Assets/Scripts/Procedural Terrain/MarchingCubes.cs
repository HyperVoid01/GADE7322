using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;
using Random = UnityEngine.Random;

public partial class MarchingCubes : MonoBehaviour
{
    [Header("Grid")] // Size of each chunk
    [SerializeField] private int xSize = 24;
    [SerializeField] private int ySize = 24;
    [SerializeField] private int zSize = 24;
    
    [Header("Chunks")] // Number of chunks in map
    [SerializeField] private int chunkXSize = 16;
    [SerializeField] private int chunkZSize = 16;
    [SerializeField] private GameObject chunkPrefab;
    
    [Header("Terrain Settings")]
    [SerializeField] private TerrainSettings terrainSettings;
    
    [Header("Surface")]
    [SerializeField] private bool interpolate = true; // smooth vertex placement along cube edges rather than snapping to midpoints
    [SerializeField] private bool smoothShading = true; // welds vertices so normals blend
    [SerializeField] private bool flipNormals = false;

    [Header("3D Perlin Noise")]
    [SerializeField] private Vector3 noiseOffset = Vector3.zero;
    [SerializeField] private int seed = 0; // world seed
    [SerializeField] private bool sealEdges = true; // closes the mesh at the bounds

    [Header("Path")]
    [SerializeField] private float pathCount; // number of paths
    [SerializeField] private float minPathSpawnDistance; // minimum spawn distance from center
    [SerializeField] private float maxPathSpawnDistance; // max spawn distance from center
    [SerializeField] private SplineContainer pathSplineContainerPrefab;
    
    [SerializeField] private int controlPointCount = 6; // number of spline knots per path
    [SerializeField] private float pathWindiness = 15f;
    [SerializeField] private float pathRadius = 4f; // horizontal half-width of the carved-out trail
    [SerializeField] private float pathHeight = 1.5f; // vertical half-height of the trail
    [SerializeField] private float pathProtectionRadius = 6f; // horizontal distance within which player edits are blocked
    [Range(0.01f, 0.5f)] [SerializeField] private float pathEndTaperFraction = 0.15f; // fraction of the path length over which width/height shrinks to 0

    [Header("Path Overlap Avoidance")]
    [SerializeField] private float minPathSeparation = 10f; // minimum allowed distance between two different paths
    [SerializeField] private int maxPathGenerationAttempts = 15; // how many times to retry a path before giving up
    [Range(0f, 1f)] [SerializeField] private float pathOverlapCheckMinT = 0.15f; // ignores this fraction of each path's start (all paths share the same start point, so they naturally converge there)

    [Header("Center Flatten")]
    [SerializeField] private float centerCutOffRadius = 10f; // horizontal radius of the flattened area at the map center
    [SerializeField] private float centerFlattenHeight = 1.5f; // height of the plaza's flat surface, relative to the paths' height (_mapCenter.y) - 0 = level with the paths, positive = raised, negative = lowered
    [SerializeField] private float centerProtectionRadius = 10f; // horizontal distance within which player edits are blocked, regardless of terrain height
    public float CenterProtectionRadius => centerProtectionRadius;
    
    [Header("Caves")]
    [SerializeField] private CaveSettings caveSettings = new CaveSettings(); // cellular automata caves, see CaveGenerator.cs

    [Header("Terrain Editing")]
    [SerializeField] private int minDigLayer = 3; // voxel layers above y=0 that are protected from digging, keeps the sealed floor intact
    
    [Header("Texturing")]
    [SerializeField] private Material terrainMaterial;

    [Header("Ground Blending")]
    [SerializeField] private float patchNoiseScale = 0.06f; // smaller = bigger patches
    [Range(0f, 1f)] [SerializeField] private float layer1Coverage = 0.5f; // share of flat ground that uses layer 1
    [Range(0.01f, 0.5f)] [SerializeField] private float patchBlend = 0.12f; // softness of the patch edges
    [Range(0f, 90f)] [SerializeField] private float slopeStartAngle = 30f; // slope where layer 2 starts to show
    [Range(0f, 180f)] [SerializeField] private float slopeEndAngle = 55f; // slope where layer 2 fully replaces the ground

    [Header("Path Texture")]
    [SerializeField] private float pathTextureWidth = 1.3f; // multiplier on pathRadius
    [SerializeField, Min(0.1f)] private float pathTextureBlend = 2f; // soft edge of the path texture, in world units
    [SerializeField] private float pathTextureHeight = 4f; // vertical range around the path where the path texture applies
    
    private readonly List<Color32> _colors = new List<Color32>();
    private readonly List<Vector3> _normals = new List<Vector3>();
    private readonly List<PathSegment> _textureSegments = new List<PathSegment>();
    
    [Header("Gizmos")]
    [SerializeField] private bool showMapBounds = true;
    [SerializeField] private bool showChunkGrid = true;
    [SerializeField] private bool showMapCenter = true;
    [SerializeField] private bool showPathSpawnRadii = true;
    [SerializeField] private bool showCenterFlatten = true;
    
    public Vector3 MapCenter => _mapCenter;
    public Vector3 WorldSize => new Vector3(xSize * chunkXSize, ySize, zSize * chunkZSize);
    public float MapWidth => chunkXSize * xSize;
    public float MapDepth => chunkZSize * zSize;
    
    // Fired once terrain generation has fully finished
    public event Action OnTerrainGenerated;
    
    // A single sampled point along a path, plus how far along the path (0-1) it is - used for end tapering
    private struct PathPoint
    {
        public readonly Vector3 position;
        public readonly float t;

        public PathPoint(Vector3 position, float t)
        {
            this.position = position;
            this.t = t;
        }
    }
    
    private struct PathSegment
    {
        public PathPoint a;
        public PathPoint b;
    }

    private readonly List<PathSegment> _chunkSegments = new List<PathSegment>();
    
    private readonly float[] _cubeCorners = new float[8];
    private int[] _edgeVertexIndex; // One slot per grid edge: vertex index, or -1
    private static Vector3Int[] _edgeStart;
    private static int[] _edgeAxis;

    private List<Path> _paths = new List<Path>(); // start/target pairs, one per path
    private List<SplineContainer> _pathSplineContainers = new List<SplineContainer>();
    private List<List<PathPoint>> _globalPathSamples = new List<List<PathPoint>>(); // world space sample points of every path, with their t along the path

    private float _pathNoiseSeed;

    private float[,,] _densities;
    private readonly List<Vector3> _vertices = new List<Vector3>();
    private readonly List<int> _triangles = new List<int>();

    private GameObject[,] _chunks;
    private Chunk[,] _chunkComponents; // Used for holding each chunk's densities
    private Vector3 _seedOffset;
    private Vector3 _mapCenter; // Center of generated terrain
    private float _averageHeight; // Average surface height of the terrain, estimated before chunks are built
    private CaveGenerator _caveGenerator; // World-wide cave field, built after paths and before chunks

    // Maps each of the 12 edges to the two corner indices it connects
    private static int[,] _edgeCorners;
    
    public static MarchingCubes Instance;

    // The current world seed. GenerativeMusic uses it so each map gets its own tune.
    public int Seed => seed;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        
        BuildEdgeCornerLookup();
        
        _edgeVertexIndex = new int[(xSize + 1) * (ySize + 1) * (zSize + 1) * 3];
    }

    private void Start()
    {
        seed = Random.Range(int.MinValue, int.MaxValue);

        System.Random prng = new System.Random(seed);
        _pathNoiseSeed = prng.Next(-100000, 100000) * 0.0001f;

        // Needed up-front so density sampling (for the height estimate and path carving) matches what BuildChunks will later generate
        _seedOffset = ComputeSeedOffset(seed);

        // Estimate where the terrain surface roughly sits before any chunks exist, so paths can spawn on it rather than at a fixed height
        _averageHeight = EstimateAverageTerrainHeight();

        _paths.Clear();
        _pathSplineContainers.Clear();
        _globalPathSamples.Clear();
        GeneratePaths();

        // Caves need the paths to exist so they can stay clear of them
        GenerateCaves();

        BuildChunks();
        
        OnTerrainGenerated?.Invoke();
    }

    [ContextMenu("Build Chunks")]
    private void RebuildChunks()
    {
        // Reset chunks
        foreach (GameObject chunk in _chunks)
        {
            Destroy(chunk);
        }
        
        _chunks = new GameObject[chunkXSize, chunkZSize];
        _chunkComponents = new Chunk[chunkXSize, chunkZSize];

        _densities = null;
        _vertices.Clear();
        _triangles.Clear();
        
        seed = Random.Range(int.MinValue, int.MaxValue);

        // Cave surface sampling needs the new seed's noise offset before chunks are built
        _seedOffset = ComputeSeedOffset(seed);
        GenerateCaves();
        
        BuildChunks();
    }

    // Caves
    // Runs the cellular automata over the whole map once, so caves line up across chunk borders
    private void GenerateCaves()
    {
        if (caveSettings == null || !caveSettings.enabled)
        {
            _caveGenerator = null;
            return;
        }

        // Every path sample point is protected, which also covers the enemy cave entrances at the path ends
        List<Vector3> protectedPoints = new List<Vector3>();
        foreach (List<PathPoint> path in _globalPathSamples)
        {
            if (path == null) continue;
            foreach (PathPoint point in path)
                protectedPoints.Add(point.position);
        }

        Vector3Int worldSize = new Vector3Int(xSize * chunkXSize, ySize, zSize * chunkZSize);

        _caveGenerator = new CaveGenerator(caveSettings);
        _caveGenerator.Generate(seed, worldSize, EstimateSurfaceHeight, protectedPoints, _mapCenter);
    }

    private void Generate(GameObject chunkObj, Vector3Int chunkCoord)
    {
        MeshFilter meshFilter = chunkObj.GetComponent<MeshFilter>();
        MeshCollider meshCollider = chunkObj.GetComponent<MeshCollider>();
        
        if (terrainMaterial != null)
            chunkObj.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;

        Chunk chunk = chunkObj.GetComponent<Chunk>();
        if (chunk == null) chunk = chunkObj.AddComponent<Chunk>();
        chunk.chunkCoord = chunkCoord;
        
        Mesh newMesh = new Mesh { name = "Marching Cubes", indexFormat = IndexFormat.UInt32 };
        meshFilter.sharedMesh = newMesh;
        
        SetDensities(chunkObj.transform.position);
        chunk.densities = _densities; // stores chunk's densities to be edited later
        MarchCubes();
        SetMesh(newMesh, chunkObj.transform.position);
        
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = newMesh;
    }

    // Instantiates and generates every chunk in the grid
    private void BuildChunks()
    {
        _seedOffset = ComputeSeedOffset(seed);
        
        _chunks = new GameObject[chunkXSize, chunkZSize];
        _chunkComponents = new Chunk[chunkXSize, chunkZSize];
        
        for (int x = 0; x < chunkXSize; x++)
        {
            for (int z = 0; z < chunkZSize; z++)
            {
                _chunks[x, z] = Instantiate(chunkPrefab, new Vector3(x * xSize, 0, z * zSize), Quaternion.identity);
                _chunks[x, z].transform.SetParent(transform);
                Generate(_chunks[x, z], new Vector3Int(x, 0, z));
                _chunkComponents[x, z] = _chunks[x, z].GetComponent<Chunk>();

                _seedOffset.z += zSize;
            }

            _seedOffset.z -= chunkZSize * zSize;
            _seedOffset.x += xSize;
        }
    }

    // Deterministically derives the noise coordinate offset from the world seed.
    // Kept as its own method so BuildChunks and the pre-chunk height estimate always agree.
    private Vector3 ComputeSeedOffset(int seedValue)
    {
        System.Random rng = new System.Random(seedValue);
        return new Vector3(rng.Next(-100000, 100000), rng.Next(-100000, 100000), rng.Next(-100000, 100000)) * 0.0001f;
    }

    // Terrain height estimation
    // Samples the density field on a coarse grid (before any chunks exist) to approximate where the terrain surface sits on average
    private float EstimateAverageTerrainHeight(int sampleGridResolution = 8)
    {
        float totalHeight = 0f;
        int count = 0;

        int worldXSize = xSize * chunkXSize;
        int worldZSize = zSize * chunkZSize;

        for (int i = 0; i < sampleGridResolution; i++)
        {
            for (int j = 0; j < sampleGridResolution; j++)
            {
                float sampleX = (i + 0.5f) / sampleGridResolution * worldXSize;
                float sampleZ = (j + 0.5f) / sampleGridResolution * worldZSize;

                totalHeight += EstimateSurfaceHeight(sampleX, sampleZ);
                count++;
            }
        }

        return count > 0 ? totalHeight / count : ySize * 0.5f;
    }

    // Walks a single x/z column top-down and returns the first y where density crosses the iso level
    private float EstimateSurfaceHeight(float worldX, float worldZ)
    {
        for (int y = ySize; y >= 0; y--)
        {
            if (SampleTerrainDensity(worldX, y, worldZ) > terrainSettings.isoLevel)
                return y;
        }

        return 0f;
    }

    // Same density formula as SetDensities (minus path carving/edge sealing), used for height estimation ahead of chunk generation
    private float SampleTerrainDensity(float worldX, float worldY, float worldZ)
    {
        float noise = FractalNoise3D(worldX, worldY, worldZ);
        noise = Mathf.Clamp01((noise - 0.5f) * terrainSettings.noiseContrast + 0.5f);

        float gradient = 1f - worldY / ySize;

        return Mathf.Lerp(noise, gradient, terrainSettings.groundBlend);
    }
    
    // Paths
    // Generates every path, retrying a path's target if it overlaps an already-accepted path
    private void GeneratePaths()
    {
        _mapCenter = new Vector3((xSize * (float)chunkXSize) / 2f, _averageHeight, (zSize * (float)chunkZSize) / 2f);

        for (int i = 0; i < pathCount; i++)
        {
            SplineContainer acceptedContainer = null;
            List<PathPoint> acceptedSamples = null;
            Path acceptedPath = default;

            for (int attempt = 0; attempt < maxPathGenerationAttempts; attempt++)
            {
                Vector3 target = GetRandomPathTarget();
                Path candidate = new Path(_mapCenter, target);

                SplineContainer container = Instantiate(pathSplineContainerPrefab, transform);
                container.name = $"Path_{i}";
                GeneratePathSpline(container, candidate.start, candidate.target, i);
                List<PathPoint> samples = SamplePathPoints(container, 100);

                bool isLastAttempt = attempt == maxPathGenerationAttempts - 1;

                // Give up and accept whatever we have on the last attempt, rather than risk fewer paths than requested
                if (!isLastAttempt && OverlapsExistingPaths(samples))
                {
                    Destroy(container.gameObject);
                    continue;
                }

                acceptedContainer = container;
                acceptedSamples = samples;
                acceptedPath = candidate;
                break;
            }

            _paths.Add(acceptedPath);
            _pathSplineContainers.Add(acceptedContainer);
            _globalPathSamples.Add(acceptedSamples);
        }
    }

    // Picks a random target point within the allowed spawn distance band from the map center
    private Vector3 GetRandomPathTarget()
    {
        float randomX = 0f;
        float randomZ = 0f;
        float distance = float.MaxValue;

        while (distance > maxPathSpawnDistance || distance < minPathSpawnDistance)
        {
            randomX = Random.Range(0f, xSize * chunkXSize);
            randomZ = Random.Range(0f, zSize * chunkZSize);

            distance = Vector3.Distance(_mapCenter, new Vector3(randomX, _averageHeight, randomZ));
        }

        return new Vector3(randomX, _averageHeight, randomZ);
    }

    // Checks a candidate path's samples against every already-accepted path.
    private bool OverlapsExistingPaths(List<PathPoint> candidate)
    {
        float convergenceThreshold = 1f - pathOverlapCheckMinT;

        foreach (List<PathPoint> existing in _globalPathSamples)
        {
            if (existing == null) continue;

            foreach (PathPoint candidatePoint in candidate)
            {
                if (candidatePoint.t > convergenceThreshold) continue;

                foreach (PathPoint existingPoint in existing)
                {
                    if (existingPoint.t > convergenceThreshold) continue;

                    if (Vector3.Distance(candidatePoint.position, existingPoint.position) < minPathSeparation)
                        return true;
                }
            }
        }

        return false;
    }
    
    private void GeneratePathSpline(SplineContainer container, Vector3 start, Vector3 target, int pathIndex)
    {
        Spline spline = new Spline();

        Vector3 direction = (target - start).normalized; // Direction from start to target
        Vector3 perpendicular = Vector3.Cross(direction, Vector3.up);
        float pathSeed = _pathNoiseSeed + pathIndex * 37.7f; //Offsets noise

        for (int i = 0; i <= controlPointCount; i++)
        {
            float t = (float)i / controlPointCount;
            Vector3 point = Vector3.Lerp(target, start, t);
            
            if (i != 0 && i != controlPointCount)
            {
                float offset = (Mathf.PerlinNoise(i * 0.7f, pathSeed) - 0.5f) * 2f;
                point += perpendicular * offset * pathWindiness;
            }
            
            spline.Add(new BezierKnot(point), TangentMode.AutoSmooth);
        }

        container.Spline = spline;
    }
    
    // Walks along the spline at fixed intervals and returns world-space points, each tagged with its t (0-1) along the path
    private List<PathPoint> SamplePathPoints(SplineContainer container, int sampleCount)
    {
        List<PathPoint> points = new List<PathPoint>();
        Spline spline = container.Spline;

        for (int i = 0; i <= sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            Vector3 localPos = spline.EvaluatePosition(t);
            Vector3 worldPos = container.transform.TransformPoint(localPos); // convert to world space
            points.Add(new PathPoint(worldPos, t));
        }

        return points;
    }
    
    public List<Vector3> GetPathEndPositions()
    {
        List<Vector3> endpoints = new List<Vector3>();

        foreach (Path path in _paths)
        {
            endpoints.Add(path.target);
        }

        return endpoints;
    }
    
    public List<SplineContainer> GetPathSplineContainers()
    {
        return _pathSplineContainers;
    }
    
    // Terrain editing
    // Edits terrain in a radius around a point
    // Dig(true) - remove terrain || Dig(false) - add terrain
    public void EditTerrain(Vector3 worldPoint, float radius, float strength, bool dig)
    {
        if (_chunkComponents == null) return;

        for (int x = 0; x < chunkXSize; x++)
        {
            for (int z = 0; z < chunkZSize; z++)
            {
                Chunk chunk = _chunkComponents[x, z];
                if (chunk == null || chunk.densities == null) continue;

                Vector3 origin = chunk.transform.position;
                Bounds chunkBounds = new Bounds(origin + new Vector3(xSize, ySize, zSize) * 0.5f, new Vector3(xSize, ySize, zSize));

                // Skip chunks player can't reach
                Vector3 closest = chunkBounds.ClosestPoint(worldPoint);
                if (Vector3.Distance(closest, worldPoint) > radius) continue;

                EditChunkDensities(chunk, origin, worldPoint, radius, strength, dig);
                RegenerateChunk(chunk);
            }
        }
    }
    
    // Digs a vertical shaft from just above 'position' to the top of the map.
    // Skips the same voxels the player can't edit: protected areas, sealed edges and the floor layers.
    public void CarveShaft(Vector3 position, float radius, float bottomOffset)
    {
        if (_chunkComponents == null) return;
    
        float bottomY = position.y + bottomOffset;
    
        int minCx = Mathf.Max(0, Mathf.FloorToInt((position.x - radius) / xSize));
        int maxCx = Mathf.Min(chunkXSize - 1, Mathf.FloorToInt((position.x + radius) / xSize));
        int minCz = Mathf.Max(0, Mathf.FloorToInt((position.z - radius) / zSize));
        int maxCz = Mathf.Min(chunkZSize - 1, Mathf.FloorToInt((position.z + radius) / zSize));
    
        for (int cx = minCx; cx <= maxCx; cx++)
        {
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                Chunk chunk = _chunkComponents[cx, cz];
                if (chunk == null || chunk.densities == null) continue;
    
                if (CarveShaftInChunk(chunk, position, radius, bottomY))
                    RegenerateChunk(chunk);
            }
        }
    }

    // Returns true if any voxel changed, so unchanged chunks aren't rebuilt
    private bool CarveShaftInChunk(Chunk chunk, Vector3 axis, float radius, float bottomY)
    {
        Vector3 origin = chunk.transform.position;
        Vector3 local = axis - origin;
    
        int minX = Mathf.Max(0, Mathf.FloorToInt(local.x - radius));
        int maxX = Mathf.Min(xSize, Mathf.CeilToInt(local.x + radius));
        int minZ = Mathf.Max(0, Mathf.FloorToInt(local.z - radius));
        int maxZ = Mathf.Min(zSize, Mathf.CeilToInt(local.z + radius));
        int minY = Mathf.Max(minDigLayer + 1, Mathf.CeilToInt(bottomY - origin.y));
    
        bool changed = false;
    
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                float dx = origin.x + x - axis.x;
                float dz = origin.z + z - axis.z;
                float horizontal = Mathf.Sqrt(dx * dx + dz * dz);
                if (horizontal > radius) continue;
    
                // Full strength inside the shaft, softening over the last voxel so the wall is smooth
                float carve = Mathf.Clamp01(radius - horizontal);
    
                for (int y = minY; y <= ySize; y++)
                {
                    if (sealEdges && (x == 0 || y == 0 || z == 0 || x == xSize || y == ySize || z == zSize))
                        continue;
    
                    float current = chunk.densities[x, y, z];
                    if (current <= 0f) continue; // Already air
    
                    Vector3 worldPos = origin + new Vector3(x, y, z);
                    if (IsWithinPathProtection(worldPos) || IsWithinCenterProtection(worldPos))
                        continue;
    
                    float updated = Mathf.Lerp(current, 0f, carve);
                    if (updated != current)
                    {
                        chunk.densities[x, y, z] = updated;
                        changed = true;
                    }
                }
            }
        }
    
        return changed;
    }
    
    private void EditChunkDensities(Chunk chunk, Vector3 origin, Vector3 worldPoint, float radius, float strength, bool dig)
    {
        Vector3 local = worldPoint - origin;

        // Clamp the affected voxel range to the chunk bounds so we don't loop over more than needed
        int minX = Mathf.Max(0, Mathf.FloorToInt(local.x - radius));
        int maxX = Mathf.Min(xSize, Mathf.CeilToInt(local.x + radius));
        
        int minY = Mathf.Max(0, Mathf.FloorToInt(local.y - radius));
        int maxY = Mathf.Min(ySize, Mathf.CeilToInt(local.y + radius));
        
        int minZ = Mathf.Max(0, Mathf.FloorToInt(local.z - radius));
        int maxZ = Mathf.Min(zSize, Mathf.CeilToInt(local.z + radius));

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    // Does not work with sealed edges
                    if (sealEdges && (x == 0 || y == 0 || z == 0 || x == xSize || y == ySize || z == zSize))
                        continue;
                    
                    // Protects the bottom N layers so mining can never thin out the sealed floor into a hole
                    if (y <= minDigLayer)
                        continue;

                    Vector3 gridWorldPos = origin + new Vector3(x, y, z);

                    // Stops player from editing terrain within the path's (flattened, tapered) protected zone
                    if (_globalPathSamples != null && IsWithinPathProtection(gridWorldPos))
                        continue;
                    
                    if (IsWithinCenterProtection(gridWorldPos))
                        continue;

                    float dist = Vector3.Distance(gridWorldPos, worldPoint);
                    if (dist > radius) continue;

                    float falloff = 1f - (dist / radius); // softer at the edge of the sphere
                    float delta = strength * falloff * (dig ? -1f : 1f); // Dig / build

                    chunk.densities[x, y, z] = Mathf.Clamp01(chunk.densities[x, y, z] + delta);
                }
            }
        }
    }

    // Rebuilds chunk mesh
    private void RegenerateChunk(Chunk chunk)
    {
        // Remake chunk's mesh using edited densities
        _densities = chunk.densities;
        MarchCubes();

        MeshFilter meshFilter = chunk.GetComponent<MeshFilter>();
        MeshCollider meshCollider = chunk.GetComponent<MeshCollider>();

        SetMesh(meshFilter.sharedMesh, chunk.transform.position);

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = meshFilter.sharedMesh;
    }

    // Density
    // Fills the density grid for one chunk
    private void SetDensities(Vector3 chunkOrigin)
    {
        _densities = new float[xSize + 1, ySize + 1, zSize + 1];

        // Only the path segments that can actually reach this chunk
        CollectChunkSegments(chunkOrigin);
        bool hasPaths = _chunkSegments.Count > 0;

        for (int x = 0; x <= xSize; x++)
        {
            for (int y = 0; y <= ySize; y++)
            {
                for (int z = 0; z <= zSize; z++)
                {
                    float noise = FractalNoise3D(x, y, z);
                    noise = Mathf.Clamp01((noise - 0.5f) * terrainSettings.noiseContrast + 0.5f);

                    float gradient = 1f - (float)y / ySize;
                    float density = Mathf.Lerp(noise, gradient, terrainSettings.groundBlend);

                    Vector3 worldPos = chunkOrigin + new Vector3(x, y, z);

                    // Carves cellular automata caves - applied before paths and the plaza so those always win
                    if (_caveGenerator != null && _caveGenerator.HasCaves)
                        density = Mathf.Min(density, _caveGenerator.SampleDensity(worldPos));

                    // Carves out paths
                    if (hasPaths)
                    {
                        float carveStrength = GetChunkPathCarveStrength(worldPos);
                        if (carveStrength > 0f)
                            density = Mathf.Lerp(density, 0f, carveStrength);
                    }

                    // Flattens the plaza at the map center
                    if (centerCutOffRadius > 0f)
                        density = GetCenterFlattenDensity(worldPos, density);

                    // Seals the outer walls of the whole map only - uses world position so inner chunk borders stay open
                    if (sealEdges && IsOnMapBoundary(worldPos))
                        density = 0f;

                    _densities[x, y, z] = density;
                }
            }
        }
    }
    
    // True for voxels on the outside faces of the full map (not the borders between chunks)
    private bool IsOnMapBoundary(Vector3 worldPos)
    {
        int wx = Mathf.RoundToInt(worldPos.x);
        int wy = Mathf.RoundToInt(worldPos.y);
        int wz = Mathf.RoundToInt(worldPos.z);

        return wx <= 0 || wy <= 0 || wz <= 0
               || wx >= xSize * chunkXSize
               || wy >= ySize
               || wz >= zSize * chunkZSize;
    }

    // Keeps only the segments whose (grown) bounding box touches this chunk
    private void CollectChunkSegments(Vector3 chunkOrigin)
    {
        _chunkSegments.Clear();
        if (_globalPathSamples == null) return;
    
        Vector3 min = chunkOrigin;
        Vector3 max = chunkOrigin + new Vector3(xSize, ySize, zSize);
    
        foreach (List<PathPoint> path in _globalPathSamples)
        {
            if (path == null) continue;
    
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = path[i].position;
                Vector3 b = path[i + 1].position;
    
                // A segment can't carve further than the carve radius/height from itself
                if (Mathf.Max(a.x, b.x) + pathRadius < min.x || Mathf.Min(a.x, b.x) - pathRadius > max.x) continue;
                if (Mathf.Max(a.y, b.y) + pathHeight < min.y || Mathf.Min(a.y, b.y) - pathHeight > max.y) continue;
                if (Mathf.Max(a.z, b.z) + pathRadius < min.z || Mathf.Min(a.z, b.z) - pathRadius > max.z) continue;
    
                _chunkSegments.Add(new PathSegment { a = path[i], b = path[i + 1] });
            }
        }
    }

    private float GetChunkPathCarveStrength(Vector3 worldPoint)
    {
        float strongest = 0f;
    
        for (int i = 0; i < _chunkSegments.Count; i++)
        {
            PathSegment seg = _chunkSegments[i];
            float s = GetSegmentCarveStrength(worldPoint, seg.a, seg.b, pathRadius, pathHeight);
            if (s > strongest) strongest = s;
        }
    
        return strongest;
    }

    // Still used for player-edit protection, which checks whole paths
    private float GetPathCarveStrengthForSinglePath(Vector3 worldPoint, List<PathPoint> path, float baseRadius, float baseHeight)
    {
        float best = 0f;
    
        for (int i = 0; i < path.Count - 1; i++)
        {
            float s = GetSegmentCarveStrength(worldPoint, path[i], path[i + 1], baseRadius, baseHeight);
            if (s > best) best = s;
        }
    
        return best;
    }

    // Same math as before for one segment, but the square root only runs for points inside the shape
    private float GetSegmentCarveStrength(Vector3 worldPoint, PathPoint a, PathPoint b, float baseRadius, float baseHeight)
    {
        Vector3 ab = b.position - a.position;
        float segT = ab.sqrMagnitude > 0.0001f
            ? Mathf.Clamp01(Vector3.Dot(worldPoint - a.position, ab) / ab.sqrMagnitude)
            : 0f;
    
        Vector3 closest = a.position + segT * ab;
        float pathT = Mathf.Lerp(a.t, b.t, segT);
    
        float taper = Mathf.Clamp01(Mathf.Min(pathT, 1f - pathT) / pathEndTaperFraction);
        if (taper <= 0f) return 0f;
    
        float effectiveRadius = baseRadius * taper;
        float effectiveHeight = Mathf.Max(baseHeight * taper, 0.001f);
        if (effectiveRadius <= 0.0001f) return 0f;
    
        float dx = worldPoint.x - closest.x;
        float dy = worldPoint.y - closest.y;
        float dz = worldPoint.z - closest.z;
    
        float normalizedSqr = (dx * dx + dz * dz) / (effectiveRadius * effectiveRadius)
                            + (dy * dy) / (effectiveHeight * effectiveHeight);
        if (normalizedSqr >= 1f) return 0f;
    
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Sqrt(normalizedSqr));
    }
    
    private float GetCenterFlattenDensity(Vector3 worldPoint, float originalDensity)
    {
        float horizontalDist = Vector2.Distance(new Vector2(worldPoint.x, worldPoint.z), new Vector2(_mapCenter.x, _mapCenter.z));
        if (horizontalDist >= centerCutOffRadius) return originalDensity;

        // Fades toward the edge of the radius so the plaza blends into the surrounding terrain instead of a hard cliff
        float edgeBlend = 1f - Mathf.SmoothStep(0f, 1f, horizontalDist / centerCutOffRadius);

        // centerFlattenHeight is measured relative to the paths' height (_mapCenter.y), so 0 = level with the paths
        float targetY = _mapCenter.y + centerFlattenHeight;

        // A small smoothed transition band around targetY so the plaza's surface isn't a razor-thin edge
        float transitionThickness = Mathf.Max(centerCutOffRadius * 0.05f, 0.5f);
        float flatDensity = 1f - Mathf.Clamp01(((worldPoint.y - targetY) + transitionThickness) / (transitionThickness * 2f));

        return Mathf.Lerp(originalDensity, flatDensity, edgeBlend);
    }

    // Same flattened, tapered shape as carving, but sized by pathProtectionRadius - used to block player digging near a path
    public bool IsWithinPathProtection(Vector3 worldPoint)
    {
        foreach (List<PathPoint> path in _globalPathSamples)
        {
            if (path == null) continue;

            // Protection is kept at least as tall as the carve so the walkway's ceiling can't be dug into either
            float protectionHeight = Mathf.Max(pathHeight, pathProtectionRadius * 0.5f);

            if (GetPathCarveStrengthForSinglePath(worldPoint, path, pathProtectionRadius, protectionHeight) > 0f)
                return true;
        }

        return false;
    }
    
    // Blocks player digging/building anywhere within the plaza's protected radius, at any height
    public bool IsWithinCenterProtection(Vector3 worldPoint)
    {
        if (centerProtectionRadius <= 0f) return false;

        float horizontalDist = Vector2.Distance(new Vector2(worldPoint.x, worldPoint.z), new Vector2(_mapCenter.x, _mapCenter.z));
        return horizontalDist < centerProtectionRadius;
    }

    // Combines multiple octaves of 3D Perlin noise into one value
    private float FractalNoise3D(float x, float y, float z)
    {
        float amplitude = 1f;
        float frequency = terrainSettings.noiseScale;
        float total = 0f;
        float maxValue = 0f;

        // Adds noise value from each octave
        for (int i = 0; i < terrainSettings.octaves; i++)
        {
            Vector3 position = (new Vector3(x, y, z) + noiseOffset + _seedOffset) * frequency;
            total += Perlin3D(position.x, position.y, position.z) * amplitude;
            maxValue += amplitude;

            amplitude *= terrainSettings.persistence; // each octave contributes less
            frequency *= terrainSettings.lacunarity; // each octave samples finer detail
        }

        return total / maxValue;
    }

    // Samples three orthogonal planes and gets average
    // Both directions of each pair
    private static float Perlin3D(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float yz = Mathf.PerlinNoise(y, z);
        float xz = Mathf.PerlinNoise(x, z);
        float yx = Mathf.PerlinNoise(y, x);
        float zy = Mathf.PerlinNoise(z, y);
        float zx = Mathf.PerlinNoise(z, x);

        return (xy + yz + xz + yx + zy + zx) / 6f;
    }

    // Marching Cubes
    private void MarchCubes()
    {
        _vertices.Clear();
        _triangles.Clear();
        System.Array.Fill(_edgeVertexIndex, -1);
    
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3Int c = MarchingTable.Corners[i];
                        _cubeCorners[i] = _densities[x + c.x, y + c.y, z + c.z];
                    }
    
                    int configIndex = GetConfigurationIndex(_cubeCorners);
    
                    // Fully inside or fully outside: nothing to draw
                    if (configIndex == 0 || configIndex == 255) continue;
    
                    MarchCube(x, y, z, configIndex);
                }
            }
        }
    }
    
    // Builds up to 5 triangles for a single cube based on its configuration index
    private void MarchCube(int x, int y, int z, int configIndex)
    {
        for (int triangle = 0; triangle < 5; triangle++)
        {
            int e0 = MarchingTable.Triangles[configIndex, triangle * 3];
            if (e0 == -1) return;
    
            int v0 = GetEdgeVertex(x, y, z, e0);
            int v1 = GetEdgeVertex(x, y, z, MarchingTable.Triangles[configIndex, triangle * 3 + 1]);
            int v2 = GetEdgeVertex(x, y, z, MarchingTable.Triangles[configIndex, triangle * 3 + 2]);
    
            // Flips face due to culling
            if (flipNormals)
            {
                _triangles.Add(v2);
                _triangles.Add(v1);
                _triangles.Add(v0);
            }
            else
            {
                _triangles.Add(v0);
                _triangles.Add(v1);
                _triangles.Add(v2);
            }
        }
    }
    
    // Returns the vertex on a cube edge, reusing it if a neighboring cube already created it
    private int GetEdgeVertex(int x, int y, int z, int edge)
    {
        int key = 0;
    
        if (smoothShading)
        {
            Vector3Int start = _edgeStart[edge];
            key = (((x + start.x) * (ySize + 1) + (y + start.y)) * (zSize + 1) + (z + start.z)) * 3 + _edgeAxis[edge];
    
            int existing = _edgeVertexIndex[key];
            if (existing >= 0) return existing;
        }
    
        Vector3 position = new Vector3(x, y, z);
        Vector3 edgeStart = position + MarchingTable.Edges[edge, 0];
        Vector3 edgeEnd = position + MarchingTable.Edges[edge, 1];
        Vector3 point;
    
        if (interpolate)
        {
            float a = _cubeCorners[_edgeCorners[edge, 0]];
            float b = _cubeCorners[_edgeCorners[edge, 1]];
    
            float t = Mathf.Approximately(b, a) ? 0.5f : (terrainSettings.isoLevel - a) / (b - a);
            point = Vector3.Lerp(edgeStart, edgeEnd, Mathf.Clamp01(t));
        }
        else
        {
            point = (edgeStart + edgeEnd) * 0.5f;
        }
    
        _vertices.Add(point);
        int index = _vertices.Count - 1;
    
        if (smoothShading)
            _edgeVertexIndex[key] = index;
    
        return index;
    }
    
    // Determines which configuration to use out of the 256 options in the triangulation table
    private int GetConfigurationIndex(float[] cubeCorners)
    {
        // if corner density is above the height threshold, it is considered Active
        int configIndex = 0;

        for (int i = 0; i < 8; i++)
        {
            if (cubeCorners[i] > terrainSettings.isoLevel)
            {
                // 1 << i  : bit shift
                // 1 byte : 0000 0000 : 8 bits
                // Max number : 255 - Used for triangulation table
                // |=  : bitwise, turns on the bit corresponding to that corner
                configIndex |= 1 << i;
            }
        }

        // Returns ints from 0 to 255
        return configIndex;
    }

    // Precomputes which two corner indices each of the 12 cube edges connects for interpolation
    private static void BuildEdgeCornerLookup()
    {
        if (_edgeCorners != null) return;

        _edgeCorners = new int[12, 2];
        _edgeStart = new Vector3Int[12];
        _edgeAxis = new int[12];

        for (int e = 0; e < 12; e++)
        {
            Vector3 edgeA = MarchingTable.Edges[e, 0];
            Vector3 edgeB = MarchingTable.Edges[e, 1];

            // Every edge in your table starts at its lower corner, so (start + axis) identifies it uniquely
            _edgeStart[e] = Vector3Int.RoundToInt(edgeA);
            _edgeAxis[e] = !Mathf.Approximately(edgeA.x, edgeB.x) ? 0
                : !Mathf.Approximately(edgeA.y, edgeB.y) ? 1 : 2;

            for (int end = 0; end < 2; end++)
            {
                Vector3 offset = MarchingTable.Edges[e, end];

                for (int c = 0; c < 8; c++)
                {
                    if (((Vector3)MarchingTable.Corners[c] - offset).sqrMagnitude < 0.0001f)
                    {
                        _edgeCorners[e, end] = c;
                        break;
                    }
                }
            }
        }
    }

    // Mesh
    // Pushes the built vertex/triangle data into the Mesh and recalculates normals/bounds
    private void SetMesh(Mesh mesh, Vector3 chunkOrigin)
    {
        mesh.Clear();
        mesh.SetVertices(_vertices);
        mesh.SetTriangles(_triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        ApplyTerrainColors(mesh, chunkOrigin);
    }
    
    // Writes each vertex's layer weights into its vertex color: R = layer 0, G = layer 1, B = layer 2 (slopes), A = path
    private void ApplyTerrainColors(Mesh mesh, Vector3 chunkOrigin)
    {
        mesh.GetNormals(_normals);
        CollectTextureSegments(chunkOrigin);
        _colors.Clear();
    
        float patchThreshold = 1f - layer1Coverage;
    
        for (int i = 0; i < _vertices.Count; i++)
        {
            Vector3 world = chunkOrigin + _vertices[i];
    
            // Slope: 0 on flat ground, 1 on steep walls. Cave ceilings count as steep.
            float angle = Mathf.Acos(Mathf.Clamp(_normals[i].y, -1f, 1f)) * Mathf.Rad2Deg;
            float slope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(slopeStartAngle, slopeEndAngle, angle));
    
            // Noise patches decide how flat ground splits between layer 0 and layer 1
            float noise = Mathf.PerlinNoise(world.x * patchNoiseScale + _pathNoiseSeed, world.z * patchNoiseScale - _pathNoiseSeed);
            float patch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(patchThreshold - patchBlend, patchThreshold + patchBlend, noise));
    
            float flat = 1f - slope;
            float path = GetPathTextureWeight(world);
            float keep = 1f - path; // The path texture replaces whatever is underneath
    
            _colors.Add(new Color32(
                ToByte(flat * (1f - patch) * keep),
                ToByte(flat * patch * keep),
                ToByte(slope * keep),
                ToByte(path)));
        }
    
        mesh.SetColors(_colors);
    }
    
    private static byte ToByte(float value)
    {
        return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
    }

    // Only the path segments that can reach this chunk, so most vertices do no path work
    private void CollectTextureSegments(Vector3 chunkOrigin)
    {
        _textureSegments.Clear();
        if (_globalPathSamples == null) return;
    
        float radius = pathRadius * pathTextureWidth;
        Vector3 min = chunkOrigin;
        Vector3 max = chunkOrigin + new Vector3(xSize, ySize, zSize);
    
        foreach (List<PathPoint> path in _globalPathSamples)
        {
            if (path == null) continue;
    
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = path[i].position;
                Vector3 b = path[i + 1].position;
    
                if (Mathf.Max(a.x, b.x) + radius < min.x || Mathf.Min(a.x, b.x) - radius > max.x) continue;
                if (Mathf.Max(a.y, b.y) + pathTextureHeight < min.y || Mathf.Min(a.y, b.y) - pathTextureHeight > max.y) continue;
                if (Mathf.Max(a.z, b.z) + radius < min.z || Mathf.Min(a.z, b.z) - radius > max.z) continue;
    
                _textureSegments.Add(new PathSegment { a = path[i], b = path[i + 1] });
            }
        }
    }

    // 0 = not on a path, 1 = on the path. Follows the carved width, including the tapered ends.
    private float GetPathTextureWeight(Vector3 worldPos)
    {
        float best = 0f;
        float baseRadius = pathRadius * pathTextureWidth;
        Vector2 p = new Vector2(worldPos.x, worldPos.z);
    
        for (int i = 0; i < _textureSegments.Count; i++)
        {
            PathSegment seg = _textureSegments[i];
    
            Vector2 a = new Vector2(seg.a.position.x, seg.a.position.z);
            Vector2 b = new Vector2(seg.b.position.x, seg.b.position.z);
            Vector2 ab = b - a;
    
            float segT = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            float horizontal = (p - (a + ab * segT)).magnitude;
    
            // Same taper as the carving, so the texture narrows with the path
            float pathT = Mathf.Lerp(seg.a.t, seg.b.t, segT);
            float taper = Mathf.Clamp01(Mathf.Min(pathT, 1f - pathT) / pathEndTaperFraction);
            float radius = baseRadius * taper;
            if (radius <= 0.01f) continue;
    
            float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius - pathTextureBlend, radius, horizontal));
            if (weight <= best) continue;
    
            // Fades out on walls that rise well above or below the path
            float pathY = Mathf.Lerp(seg.a.position.y, seg.b.position.y, segT);
            float dy = Mathf.Abs(worldPos.y - pathY);
            weight *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(pathTextureHeight * 0.6f, pathTextureHeight, dy));
    
            best = Mathf.Max(best, weight);
        }
    
        return best;
    }
    
    private void OnDrawGizmos()
    {
        float worldXSize = xSize * chunkXSize;
        float worldZSize = zSize * chunkZSize;

        // Map Center
        float centerY = Application.isPlaying ? _mapCenter.y : ySize * 0.5f;
        Vector3 origin = transform.position;
        Vector3 center = origin + new Vector3(worldXSize * 0.5f, centerY, worldZSize * 0.5f);

        // Draws how big entire map is
        if (showMapBounds)
        {
            Gizmos.color = Color.yellow;
            Vector3 boundsCenter = origin + new Vector3(worldXSize, ySize, worldZSize) * 0.5f;
            Vector3 boundsSize = new Vector3(worldXSize, ySize, worldZSize);
            Gizmos.DrawWireCube(boundsCenter, boundsSize);
        }

        // Draws every chunk
        if (showChunkGrid)
        {
            Gizmos.color = Color.cyan;
            for (int x = 0; x < chunkXSize; x++)
            {
                for (int z = 0; z < chunkZSize; z++)
                {
                    Vector3 chunkOrigin = origin + new Vector3(x * xSize, 0, z * zSize);
                    Vector3 chunkCenter = chunkOrigin + new Vector3(xSize, ySize, zSize) * 0.5f;
                    Vector3 chunkSize = new Vector3(xSize, ySize, zSize);
                    Gizmos.DrawWireCube(chunkCenter, chunkSize);
                }
            }
        }

        // Center of map
        if (showMapCenter)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(center, Mathf.Max(worldXSize, worldZSize) * 0.01f);
        }

        // Min and Max path start positions
        if (showPathSpawnRadii)
        {
            DrawHorizontalCircle(center, minPathSpawnDistance, Color.green);
            DrawHorizontalCircle(center, maxPathSpawnDistance, Color.blue);
        }

        // Center radius
        if (showCenterFlatten)
        {
            DrawHorizontalCircle(center, centerCutOffRadius, Color.magenta);
        }
    }

    // Draws a horizontal wire circle at the given world-space center
    private void DrawHorizontalCircle(Vector3 center, float radius, Color color)
    {
        if (radius <= 0f) return;

        Gizmos.color = color;
        const int segments = 64;
        Vector3 prevPoint = center + new Vector3(radius, 0, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }
}