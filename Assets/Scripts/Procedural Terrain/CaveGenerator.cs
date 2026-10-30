using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

// Inspector settings for the cellular automata caves, shown on MarchingCubes
[Serializable]
public class CaveSettings
{
    public bool enabled = true;

    [Header("Grid")]
    [Min(1)] public int horizontalCellSize = 2; // voxels per CA cell on x/z - 2 is 4x cheaper than 1 and still smooth
    [Min(1)] public int verticalCellSize = 1;   // voxels per CA cell on y - kept at 1 because the map is only ySize tall

    [Header("Automata")]
    [Range(0f, 1f)] public float initialRockChance = 0.5f; // starting fill - lower = bigger caves. Very sensitive: 0.45 hollows out most of the band, 0.55 leaves almost nothing
    [Range(0, 26)] public int rockThreshold = 13;          // more rock neighbours than this -> rock, fewer -> air, equal -> unchanged
    [Range(0, 10)] public int iterations = 5;              // smoothing passes of the automata rule

    [Header("Cleanup")]
    public bool removeSmallPockets = true;
    [Min(1)] public int minPocketCells = 60;  // air regions smaller than this get filled back in
    [Range(0, 3)] public int smoothingPasses = 1; // box blurs before sampling, softens the blocky CA edges

    [Header("Placement")]
    public int minCaveY = 4;              // lowest voxel y a cave can reach - keep above minDigLayer
    public float surfaceMargin = 4f;      // solid voxels kept between a cave and the surface above it
    public float pathClearance = 7f;      // horizontal distance from paths where caves are blocked
    public float pathClearanceDepth = 6f; // how far below a path caves are blocked
    public float centerClearance = 14f;   // horizontal distance from the map center where caves are blocked

    [Header("Blending")]
    [Range(0f, 1f)] public float strength = 1f; // 0 = caves have no effect, 1 = fully carved
}

// Generates a world-sized cave field with 3D cellular automata, then samples it smoothly so marching cubes gets rounded walls.
// Runs once over the whole map (not per chunk) so caves line up across chunk borders.
public class CaveGenerator
{
    private readonly CaveSettings _settings;

    private int _gx, _gy, _gz; // grid dimensions in cells
    private float _cellH, _cellV; // cell size in voxels
    private float[] _openness; // 1 = open cave, 0 = rock, per cell after smoothing

    public bool HasCaves => _openness != null;

    public CaveGenerator(CaveSettings settings)
    {
        _settings = settings;
    }

    // surfaceHeightAt: returns the surface y for a world x/z column
    // protectedPoints: points (path samples) that caves must stay away from
    public void Generate(int seed, Vector3Int worldSize, Func<float, float, float> surfaceHeightAt, List<Vector3> protectedPoints, Vector3 mapCenter)
    {
        _cellH = _settings.horizontalCellSize;
        _cellV = _settings.verticalCellSize;

        // +1 so the last cell sits on (or past) the far edge of the map
        _gx = Mathf.CeilToInt(worldSize.x / _cellH) + 1;
        _gy = Mathf.CeilToInt(worldSize.y / _cellV) + 1;
        _gz = Mathf.CeilToInt(worldSize.z / _cellH) + 1;

        int count = _gx * _gy * _gz;
        bool[] allowed = BuildAllowedMask(surfaceHeightAt, protectedPoints, mapCenter);

        // Random fill - cells outside the allowed area always start (and stay) as rock
        Random rng = new Random(seed ^ 0x5F3759DF);
        float[] rock = new float[count];
        for (int i = 0; i < count; i++)
            rock[i] = !allowed[i] || rng.NextDouble() < _settings.initialRockChance ? 1f : 0f;

        RunAutomata(rock, allowed);

        if (_settings.removeSmallPockets)
            RemoveSmallPockets(rock);

        // Flip to openness so blurring and sampling read naturally (1 = cave)
        _openness = new float[count];
        for (int i = 0; i < count; i++)
            _openness[i] = 1f - rock[i];

        float[] temp = new float[count];
        for (int pass = 0; pass < _settings.smoothingPasses; pass++)
        {
            BoxSum3(_openness, temp, 0f); // out of bounds counts as rock
            for (int i = 0; i < count; i++)
                _openness[i] = temp[i] / 27f;
        }
    }

    // Returns a density value to combine with the terrain using Mathf.Min - 1 = untouched, 0 = fully carved
    public float SampleDensity(Vector3 worldPos)
    {
        if (_openness == null) return 1f;

        float open = SampleOpenness(worldPos);
        return 1f - open * _settings.strength;
    }

    // Trilinear sample of the cave field at a world position
    private float SampleOpenness(Vector3 worldPos)
    {
        float fx = Mathf.Clamp(worldPos.x / _cellH, 0f, _gx - 1.001f);
        float fy = Mathf.Clamp(worldPos.y / _cellV, 0f, _gy - 1.001f);
        float fz = Mathf.Clamp(worldPos.z / _cellH, 0f, _gz - 1.001f);

        int x0 = (int)fx, y0 = (int)fy, z0 = (int)fz;
        float tx = fx - x0, ty = fy - y0, tz = fz - z0;

        float c000 = _openness[Index(x0, y0, z0)];
        float c100 = _openness[Index(x0 + 1, y0, z0)];
        float c010 = _openness[Index(x0, y0 + 1, z0)];
        float c110 = _openness[Index(x0 + 1, y0 + 1, z0)];
        float c001 = _openness[Index(x0, y0, z0 + 1)];
        float c101 = _openness[Index(x0 + 1, y0, z0 + 1)];
        float c011 = _openness[Index(x0, y0 + 1, z0 + 1)];
        float c111 = _openness[Index(x0 + 1, y0 + 1, z0 + 1)];

        float x00 = Mathf.Lerp(c000, c100, tx);
        float x10 = Mathf.Lerp(c010, c110, tx);
        float x01 = Mathf.Lerp(c001, c101, tx);
        float x11 = Mathf.Lerp(c011, c111, tx);

        float y0v = Mathf.Lerp(x00, x10, ty);
        float y1v = Mathf.Lerp(x01, x11, ty);

        return Mathf.Lerp(y0v, y1v, tz);
    }

    private int Index(int x, int y, int z) => x + _gx * (y + _gy * z);

    // Marks which cells are allowed to become cave: above the floor, below the surface margin, away from map edges, paths and the center
    private bool[] BuildAllowedMask(Func<float, float, float> surfaceHeightAt, List<Vector3> protectedPoints, Vector3 mapCenter)
    {
        bool[] allowed = new bool[_gx * _gy * _gz];

        for (int z = 0; z < _gz; z++)
        {
            for (int x = 0; x < _gx; x++)
            {
                // Keeps a ring of rock around the map edge so caves never open into the sealed walls
                if (x <= 1 || z <= 1 || x >= _gx - 2 || z >= _gz - 2) continue;

                float worldX = x * _cellH;
                float worldZ = z * _cellH;

                float centerDist = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(mapCenter.x, mapCenter.z));
                if (centerDist < _settings.centerClearance) continue;

                float ceiling = surfaceHeightAt(worldX, worldZ) - _settings.surfaceMargin;

                for (int y = 0; y < _gy; y++)
                {
                    float worldY = y * _cellV;
                    if (worldY < _settings.minCaveY || worldY > ceiling) continue;

                    allowed[Index(x, y, z)] = true;
                }
            }
        }

        // Blocks caves from undermining paths (and the enemy cave entrances at their ends)
        if (protectedPoints != null)
        {
            float clearance = _settings.pathClearance;
            float clearanceSqr = clearance * clearance;

            foreach (Vector3 p in protectedPoints)
            {
                int minX = Mathf.Max(0, Mathf.FloorToInt((p.x - clearance) / _cellH));
                int maxX = Mathf.Min(_gx - 1, Mathf.CeilToInt((p.x + clearance) / _cellH));
                int minZ = Mathf.Max(0, Mathf.FloorToInt((p.z - clearance) / _cellH));
                int maxZ = Mathf.Min(_gz - 1, Mathf.CeilToInt((p.z + clearance) / _cellH));
                int minY = Mathf.Max(0, Mathf.FloorToInt((p.y - _settings.pathClearanceDepth) / _cellV));

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = x * _cellH - p.x;
                        float dz = z * _cellH - p.z;
                        if (dx * dx + dz * dz > clearanceSqr) continue;

                        for (int y = minY; y < _gy; y++)
                            allowed[Index(x, y, z)] = false;
                    }
                }
            }
        }

        return allowed;
    }

    // Classic CA rule using the 26 surrounding cells. Out of bounds counts as rock so caves close off at the edges.
    private void RunAutomata(float[] rock, bool[] allowed)
    {
        int count = rock.Length;
        float[] sums = new float[count];
        int threshold = _settings.rockThreshold;

        for (int iteration = 0; iteration < _settings.iterations; iteration++)
        {
            BoxSum3(rock, sums, 1f);

            for (int i = 0; i < count; i++)
            {
                if (!allowed[i])
                {
                    rock[i] = 1f;
                    continue;
                }

                int neighbours = Mathf.RoundToInt(sums[i] - rock[i]); // 3x3x3 sum minus the cell itself

                if (neighbours > threshold) rock[i] = 1f;
                else if (neighbours < threshold) rock[i] = 0f;
                // equal: keep current state
            }
        }
    }

    // Sums each cell's 3x3x3 neighbourhood (including itself) in three 1D passes, which is much cheaper than 27 reads per cell.
    // outOfBounds is the value a missing single cell counts as.
    private void BoxSum3(float[] src, float[] dst, float outOfBounds)
    {
        int count = src.Length;
        float[] a = new float[count];
        float[] b = new float[count];

        // X pass - a missing neighbour is one cell
        for (int z = 0; z < _gz; z++)
        for (int y = 0; y < _gy; y++)
        for (int x = 0; x < _gx; x++)
        {
            float left = x > 0 ? src[Index(x - 1, y, z)] : outOfBounds;
            float right = x < _gx - 1 ? src[Index(x + 1, y, z)] : outOfBounds;
            a[Index(x, y, z)] = left + src[Index(x, y, z)] + right;
        }

        // Y pass - a missing neighbour is a row of 3 cells
        float rowOob = outOfBounds * 3f;
        for (int z = 0; z < _gz; z++)
        for (int y = 0; y < _gy; y++)
        for (int x = 0; x < _gx; x++)
        {
            float down = y > 0 ? a[Index(x, y - 1, z)] : rowOob;
            float up = y < _gy - 1 ? a[Index(x, y + 1, z)] : rowOob;
            b[Index(x, y, z)] = down + a[Index(x, y, z)] + up;
        }

        // Z pass - a missing neighbour is a 3x3 slab of 9 cells
        float slabOob = outOfBounds * 9f;
        for (int z = 0; z < _gz; z++)
        for (int y = 0; y < _gy; y++)
        for (int x = 0; x < _gx; x++)
        {
            float back = z > 0 ? b[Index(x, y, z - 1)] : slabOob;
            float front = z < _gz - 1 ? b[Index(x, y, z + 1)] : slabOob;
            dst[Index(x, y, z)] = back + b[Index(x, y, z)] + front;
        }
    }

    // Flood fills every air region (6-connected) and fills in the ones that are too small to be worth exploring
    private void RemoveSmallPockets(float[] rock)
    {
        int count = rock.Length;
        bool[] visited = new bool[count];
        Stack<int> stack = new Stack<int>();
        List<int> region = new List<int>();

        for (int start = 0; start < count; start++)
        {
            if (visited[start] || rock[start] > 0.5f) continue;

            region.Clear();
            stack.Push(start);
            visited[start] = true;

            while (stack.Count > 0)
            {
                int i = stack.Pop();
                region.Add(i);

                int x = i % _gx;
                int y = (i / _gx) % _gy;
                int z = i / (_gx * _gy);

                TryVisit(x - 1, y, z);
                TryVisit(x + 1, y, z);
                TryVisit(x, y - 1, z);
                TryVisit(x, y + 1, z);
                TryVisit(x, y, z - 1);
                TryVisit(x, y, z + 1);
            }

            if (region.Count < _settings.minPocketCells)
            {
                foreach (int i in region)
                    rock[i] = 1f;
            }
        }

        void TryVisit(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= _gx || y >= _gy || z >= _gz) return;

            int n = Index(x, y, z);
            if (visited[n] || rock[n] > 0.5f) return;

            visited[n] = true;
            stack.Push(n);
        }
    }
}