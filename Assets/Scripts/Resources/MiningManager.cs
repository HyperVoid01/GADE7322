using UnityEngine;

public class MiningManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private LayerMask resourceLayer;
    [SerializeField] private GameObject pickaxeObject;

    [Header("Mining")]
    [SerializeField] private float mineRange = 6f;
    [SerializeField] private int damage = 10;

    [Header("Impact Sparks")]
    [Tooltip("Particle effect played where the pickaxe hits. Its forward (blue) axis is the direction the sparks fly.")]
    [SerializeField] private GameObject sparkPrefab;
    [SerializeField] private float sparkLifetime = 1.5f;
    [Tooltip("Minimum time between spark bursts")]
    [SerializeField] private float impactInterval = 0.1f;

    [Header("Debris")]
    [Tooltip("Debris never collides with anything under this object. Leave empty to use the object TowerBuilder is on (the player).")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private float minThrowSpeed = 3f;
    [SerializeField] private float maxThrowSpeed = 6f;
    [Tooltip("How far each chunk's direction can scatter away from the main direction")]
    [SerializeField, Range(0f, 1.5f)] private float debrisSpread = 0.5f;
    [Tooltip("Extra upward kick so chunks arc instead of flying flat")]
    [SerializeField, Range(0f, 1.5f)] private float debrisUpwardBias = 0.5f;
    [SerializeField] private float debrisTorque = 8f;
    [SerializeField] private float debrisLifetime = 6f;
    [Tooltip("The last part of the lifetime, spent shrinking away")]
    [SerializeField] private float debrisShrinkTime = 0.5f;
    [Tooltip("No new debris spawns while this many are alive")]
    [SerializeField] private int maxActiveDebris = 60;

    public static MiningManager Instance;

    // Built-in "Ignore Raycast" layer: debris still collides with the ground,
    // but mining and building raycasts (which use their own masks) never hit it
    private const int IgnoreRaycastLayer = 2;

    private Pickaxe _pickaxe;
    private Collider[] _playerColliders;
    private float _nextImpactTime;
    private bool _warnedNoCollider;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (pickaxeObject != null)
            _pickaxe = pickaxeObject.GetComponent<Pickaxe>();
    }

    private void Update()
    {
        UpdatePickaxeVisibility();
    }

    // Hidden while the tower shop or upgrade menu is open or a tower is being placed, back as soon as they're done
    private void UpdatePickaxeVisibility()
    {
        if (pickaxeObject == null) return;

        bool shopOpen = TowerDevice.Instance != null && TowerDevice.Instance.IsActive;
        bool building = TowerBuilder.Instance != null && TowerBuilder.Instance.IsBuilding;
        bool upgrading = TowerUpgradeMenu.Instance != null && TowerUpgradeMenu.Instance.IsOpen;
        bool shouldShow = !shopOpen && !building && !upgrading;

        if (pickaxeObject.activeSelf != shouldShow)
            pickaxeObject.SetActive(shouldShow);
    }

    // Called by TerrainEditor whenever a mine action fires
    public void Swing()
    {
        if (_pickaxe != null)
            _pickaxe.Swing();
    }

    public void TryMine(Ray ray)
    {
        if (!Physics.Raycast(ray, out RaycastHit hit, mineRange, resourceLayer))
            return;

        Resource resource = hit.collider.GetComponentInParent<Resource>();
        if (resource == null) return;

        PlayImpact(hit.point, hit.normal);
        resource.TakeDamage(damage, hit.point, hit.normal);
    }

    // Sparks flying out of the surface that was hit. Also called by TerrainEditor when digging terrain.
    public void PlayImpact(Vector3 point, Vector3 normal)
    {
        if (sparkPrefab == null || Time.time < _nextImpactTime) return;

        _nextImpactTime = Time.time + impactInterval;

        Vector3 facing = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;

        GameObject sparks = Instantiate(sparkPrefab, point + facing * 0.02f, Quaternion.LookRotation(facing));
        Destroy(sparks, sparkLifetime);
    }

    // Spawns one chunk and throws it roughly along 'direction'.
    // forceScale multiplies the throw speed (the depleted burst uses a bigger one).
    public void SpawnDebris(GameObject prefab, Vector3 position, Vector3 direction, float forceScale = 1f)
    {
        if (prefab == null || TimedDebris.ActiveCount >= maxActiveDebris) return;

        GameObject debris = Instantiate(prefab, position, Random.rotation);

        foreach (Transform t in debris.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = IgnoreRaycastLayer;

        Rigidbody body = debris.GetComponentInChildren<Rigidbody>();
        if (body == null)
            body = debris.AddComponent<Rigidbody>();

        // Small, fast chunks would otherwise skip straight through the terrain mesh
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;

        IgnorePlayerCollisions(debris);

        Vector3 throwDirection = (direction + Random.insideUnitSphere * debrisSpread + Vector3.up * debrisUpwardBias).normalized;
        float speed = Random.Range(minThrowSpeed, maxThrowSpeed) * forceScale;

        body.AddForce(throwDirection * speed, ForceMode.VelocityChange);
        body.AddTorque(Random.insideUnitSphere * debrisTorque, ForceMode.VelocityChange);

        debris.AddComponent<TimedDebris>().Init(debrisLifetime * Random.Range(0.85f, 1.15f), debrisShrinkTime);
    }

    // Makes every collider on the debris pass straight through the player
    private void IgnorePlayerCollisions(GameObject debris)
    {
        Collider[] debrisColliders = debris.GetComponentsInChildren<Collider>();

        if (debrisColliders.Length == 0 && !_warnedNoCollider)
        {
            _warnedNoCollider = true;
            Debug.LogWarning("Debris prefab has no Collider, so it will fall through the ground. Add one to the prefab.", debris);
        }

        Collider[] playerColliders = GetPlayerColliders();

        foreach (Collider debrisCollider in debrisColliders)
        {
            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider != null)
                    Physics.IgnoreCollision(debrisCollider, playerCollider);
            }
        }
    }

    private Collider[] GetPlayerColliders()
    {
        if (_playerColliders != null) return _playerColliders;

        Transform root = playerRoot != null ? playerRoot
            : TowerBuilder.Instance != null ? TowerBuilder.Instance.transform
            : null;

        // Nothing to ignore yet: don't cache, so it is looked up again on the next spawn
        if (root == null) return System.Array.Empty<Collider>();

        _playerColliders = root.GetComponentsInChildren<Collider>(true);
        return _playerColliders;
    }
}