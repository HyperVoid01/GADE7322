using UnityEngine;
using UnityEngine.Rendering;

public class GodRaySpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MarchingCubes marchingCubes;
    [SerializeField] private Material godRayMaterial;
    [SerializeField] private Light sunLight;
    [SerializeField] private LayerMask terrainLayer = ~0;

    [Header("Spawn Area")]
    [SerializeField] private int rayCount = 25;
    [SerializeField] private Vector3 areaCenter = new Vector3(192f, 0f, 192f);
    [SerializeField] private Vector2 areaSize = new Vector2(384f, 384f);
    [SerializeField] private float surfaceHeight = 40f; // top of every shaft; keep it above what the camera normally sees

    [Header("Shaft Size")]
    [SerializeField] private Vector2 widthRange = new Vector2(3f, 8f);
    [SerializeField] private float groundOvershoot = 3f; // how far the shaft sinks into the terrain (match _SoftIntersection)
    [SerializeField] private float maxRayDistance = 200f;

    [Header("Seed")]
    [SerializeField] private bool randomSeed = true;
    [SerializeField] private int seed = 0;

    private void Awake()
    {
        // Fallback if the reference wasn't assigned in the Inspector
        if (marchingCubes == null)
        {
            marchingCubes = FindFirstObjectByType<MarchingCubes>();
        }

        if (marchingCubes == null)
        {
            Debug.LogWarning("GodRaySpawner: No MarchingCubes found in the scene.");
            return;
        }

        // Subscribe in Awake so the event isn't missed if MarchingCubes fires in its Start
        marchingCubes.OnTerrainGenerated += Spawn;
    }

    private void OnDestroy()
    {
        if (marchingCubes != null)
        {
            marchingCubes.OnTerrainGenerated -= Spawn;
        }
    }

    [ContextMenu("Respawn God Rays")]
    private void Respawn()
    {
        Spawn();
    }

    private void Spawn()
    {
        if (godRayMaterial == null || sunLight == null)
        {
            Debug.LogWarning("GodRaySpawner: Assign the material and the sun light.");
            return;
        }

        // Clear any previous shafts
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }

        // Make sure the freshly created chunk colliders are registered before raycasting
        Physics.SyncTransforms();

        if (randomSeed) seed = Random.Range(int.MinValue, int.MaxValue);
        System.Random prng = new System.Random(seed);

        Vector3 axis = GetShaftAxis();
        int spawned = 0;
        int attempts = 0;

        // Retry missed raycasts so you still get rayCount shafts
        while (spawned < rayCount && attempts < rayCount * 4)
        {
            attempts++;

            float x = areaCenter.x + ((float)prng.NextDouble() - 0.5f) * areaSize.x;
            float z = areaCenter.z + ((float)prng.NextDouble() - 0.5f) * areaSize.y;
            Vector3 top = new Vector3(x, surfaceHeight, z);

            // Cast down along the shaft's direction (opposite the axis that points at the sun)
            if (!Physics.Raycast(top, -axis, out RaycastHit hit, maxRayDistance, terrainLayer, QueryTriggerInteraction.Ignore))
                continue;

            float length = hit.distance + groundOvershoot;
            float width = Mathf.Lerp(widthRange.x, widthRange.y, (float)prng.NextDouble());

            // Pivot is the quad's centre, halfway along the shaft
            Vector3 center = top - axis * (length * 0.5f);

            CreateShaft(spawned, center, width, length);
            spawned++;
        }

        if (spawned == 0)
        {
            Debug.LogWarning("GodRaySpawner: No shafts hit the terrain. Check terrainLayer, surfaceHeight and areaCenter/areaSize.");
        }
    }

    // Must match the axis maths in GodRay.shader
    private Vector3 GetShaftAxis()
    {
        Vector3 toLight = -sunLight.transform.forward;
        float slant = godRayMaterial.GetFloat("_Slant");

        return new Vector3(toLight.x * slant, Mathf.Max(toLight.y, 0.3f), toLight.z * slant).normalized;
    }

    private void CreateShaft(int index, Vector3 center, float width, float length)
    {
        GameObject ray = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ray.name = $"GodRay_{index}";
        Destroy(ray.GetComponent<MeshCollider>()); // Shafts shouldn't block raycasts (mining, tower placement)
        ray.transform.SetParent(transform, false);

        ray.transform.position = center;
        ray.transform.localScale = new Vector3(width, length, 1f);

        MeshRenderer meshRenderer = ray.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = godRayMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        // The shader moves vertices, so enlarge the bounds to stop shafts popping out of view
        meshRenderer.bounds = new Bounds(center, Vector3.one * (length + width * 2f));
    }
}