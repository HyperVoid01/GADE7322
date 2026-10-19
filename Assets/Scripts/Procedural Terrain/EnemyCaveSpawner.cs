using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

// Places a cave model at the start of every enemy path, with its opening facing down the path,
// and flattens the terrain around it
public class EnemyCaveSpawner : MonoBehaviour
{
    [Header("Cave")]
    [SerializeField] private GameObject cavePrefab;
    [Tooltip("Distance from the very end of the path, in world units. Positive = along the path toward the map center. " +
             "Negative = backwards past the end of the path, away from the map center. " +
             "0 sits right at the end, where the tunnel has tapered shut.")]
    [SerializeField] private float distanceFromPathEnd = 6f;
    [Tooltip("Degrees added around Y. Use 180 if your model's opening faces backwards.")]
    [SerializeField] private float yawOffset = 0f;
    [SerializeField] private float heightOffset = 0f;

    [Header("Clearing")]
    [Tooltip("Keep this on when the distance is negative, otherwise the cave sits buried in solid rock")]
    [SerializeField] private bool flattenArea = true;
    [Tooltip("Horizontal radius of the flattened area around the cave")]
    [SerializeField] private float clearingRadius = 10f;
    [Tooltip("Height of the flat surface relative to the path's height. 0 = level with the path.")]
    [SerializeField] private float clearingHeightOffset = 0f;
    [Tooltip("Moves the middle of the clearing along the path, away from the cave. " +
             "Positive = toward the map center, which leaves more rock behind the cave.")]
    [SerializeField] private float clearingForwardOffset = 0f;

    [Header("Floor Snap")]
    [Tooltip("Drops the cave onto the terrain surface found by a downward raycast")]
    [SerializeField] private bool snapToFloor = true;
    [SerializeField] private LayerMask terrainLayer;
    [SerializeField] private float floorSearchDistance = 6f;

    private readonly List<Transform> _caves = new List<Transform>();
    private bool _spawned;

    public IReadOnlyList<Transform> Caves => _caves;

    private void Start()
    {
        // Listen for the terrain finishing, and also try right away in case it already finished
        // before this script's Start ran
        MarchingCubes.Instance.OnTerrainGenerated += SpawnCaves;
        SpawnCaves();
    }

    private void OnDestroy()
    {
        if (MarchingCubes.Instance != null)
            MarchingCubes.Instance.OnTerrainGenerated -= SpawnCaves;
    }

    private void SpawnCaves()
    {
        if (_spawned || cavePrefab == null) return;

        List<SplineContainer> containers = MarchingCubes.Instance.GetPathSplineContainers();
        if (containers == null || containers.Count == 0) return; // Paths aren't generated yet

        _spawned = true;

        for (int i = 0; i < containers.Count; i++)
            SpawnCave(containers[i], i);
    }

    private void SpawnCave(SplineContainer container, int index)
    {
        if (container == null) return;

        float length = container.Spline.GetLength();
        if (length <= 0f) return;

        // The spline only exists between t = 0 and t = 1, so a negative distance can't be a spline position.
        // Instead it starts at the very end of the path and moves backwards in a straight line below.
        float t = distanceFromPathEnd > 0f ? Mathf.Clamp01(distanceFromPathEnd / length) : 0f;

        Vector3 position = GetWorldPosition(container, t);

        // The spline runs from the far end toward the map center, so "ahead" is out of the cave mouth
        Vector3 ahead = GetWorldPosition(container, Mathf.Clamp01(t + 0.02f));
        Vector3 forward = ahead - position;
        forward.y = 0f; // Keep the cave upright
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

        // Negative distance: slide backwards past the end of the path
        if (distanceFromPathEnd < 0f)
            position += forward * distanceFromPathEnd;

        // Keep the cave on the map even when the path ends near the edge
        MarchingCubes terrain = MarchingCubes.Instance;
        position.x = Mathf.Clamp(position.x, 1f, terrain.MapWidth - 1f);
        position.z = Mathf.Clamp(position.z, 1f, terrain.MapDepth - 1f);

        // Flatten first, so the floor raycast below sees the finished terrain
        if (flattenArea)
        {
            float surfaceY = position.y + clearingHeightOffset;
            Vector3 clearingCenter = position + forward * clearingForwardOffset;

            terrain.FlattenArea(clearingCenter, clearingRadius, surfaceY);
            Physics.SyncTransforms();

            position.y = surfaceY;
        }

        if (snapToFloor)
        {
            Vector3 origin = position + Vector3.up * 0.1f; // Starts just above the floor, in open air
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, floorSearchDistance, terrainLayer))
                position.y = hit.point.y;
        }

        position.y += heightOffset;

        Quaternion rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, yawOffset, 0f);

        GameObject cave = Instantiate(cavePrefab, position, rotation, transform);
        cave.name = $"EnemyCave_{index}";
        _caves.Add(cave.transform);
    }

    private static Vector3 GetWorldPosition(SplineContainer container, float t)
    {
        Vector3 local = container.Spline.EvaluatePosition(t);
        return container.transform.TransformPoint(local);
    }
}