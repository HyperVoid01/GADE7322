using UnityEngine;

// Second half of MarchingCubes: generation-time terrain flattening
public partial class MarchingCubes
{
    // Levels the terrain around a point to a flat surface at surfaceY.
    // Solid below, air above, fading back into the existing terrain toward the edge of the radius.
    // Meant for generation-time use, so it ignores the player-edit protection zones.
    public void FlattenArea(Vector3 center, float radius, float surfaceY)
    {
        if (_chunkComponents == null || radius <= 0f) return;

        // One voxel of margin so chunks that only share an edge voxel with the area are included
        int minCx = Mathf.Max(0, Mathf.FloorToInt((center.x - radius - 1f) / xSize));
        int maxCx = Mathf.Min(chunkXSize - 1, Mathf.FloorToInt((center.x + radius + 1f) / xSize));
        int minCz = Mathf.Max(0, Mathf.FloorToInt((center.z - radius - 1f) / zSize));
        int maxCz = Mathf.Min(chunkZSize - 1, Mathf.FloorToInt((center.z + radius + 1f) / zSize));

        for (int cx = minCx; cx <= maxCx; cx++)
        {
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                Chunk chunk = _chunkComponents[cx, cz];
                if (chunk == null || chunk.densities == null) continue;

                if (FlattenChunk(chunk, center, radius, surfaceY))
                    RegenerateChunk(chunk);
            }
        }
    }

    // Returns true if any voxel changed, so unchanged chunks aren't rebuilt
    private bool FlattenChunk(Chunk chunk, Vector3 center, float radius, float surfaceY)
    {
        Vector3 origin = chunk.transform.position;
        Vector3 local = center - origin;

        int minX = Mathf.Max(0, Mathf.FloorToInt(local.x - radius));
        int maxX = Mathf.Min(xSize, Mathf.CeilToInt(local.x + radius));
        int minZ = Mathf.Max(0, Mathf.FloorToInt(local.z - radius));
        int maxZ = Mathf.Min(zSize, Mathf.CeilToInt(local.z + radius));

        // Protects the floor layers, and the sealed top when edges are sealed
        int minY = minDigLayer + 1;
        int maxY = sealEdges ? ySize - 1 : ySize;

        // Same smoothed band as the center plaza so the surface isn't razor thin.
        // The band is shifted so the surface (where density crosses the iso level) lands exactly on surfaceY.
        float transitionThickness = Mathf.Max(radius * 0.05f, 0.5f);
        float bandSurface = surfaceY - transitionThickness * (1f - 2f * terrainSettings.isoLevel);

        bool changed = false;

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (sealEdges && (x == 0 || x == xSize || z == 0 || z == zSize))
                    continue;

                float dx = origin.x + x - center.x;
                float dz = origin.z + z - center.z;
                float horizontalDist = Mathf.Sqrt(dx * dx + dz * dz);
                if (horizontalDist >= radius) continue;

                // Fades toward the edge of the radius so the clearing blends into the terrain instead of a hard cliff
                float edgeBlend = 1f - Mathf.SmoothStep(0f, 1f, horizontalDist / radius);

                for (int y = minY; y <= maxY; y++)
                {
                    float worldY = origin.y + y;
                    float flatDensity = 1f - Mathf.Clamp01(((worldY - bandSurface) + transitionThickness) / (transitionThickness * 2f));

                    float current = chunk.densities[x, y, z];
                    float updated = Mathf.Lerp(current, flatDensity, edgeBlend);

                    if (!Mathf.Approximately(updated, current))
                    {
                        chunk.densities[x, y, z] = updated;
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }
}