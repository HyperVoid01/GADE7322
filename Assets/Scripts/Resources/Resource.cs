using System.Collections;
using UnityEngine;

public class Resource : MonoBehaviour, ITargetable
{
    [SerializeField] private ResourceType resourceType;
    [SerializeField] private int resourceAmount = 100;

    [Header("Hit Feedback")]
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float flashDuration = 0.25f;

    [Header("Debris")]
    [Tooltip("Small chunk model thrown out while mining. Needs a Collider. A Rigidbody is added if missing.")]
    [SerializeField] private GameObject debrisPrefab;
    [Tooltip("How many chunks fly out on each hit (min, max)")]
    [SerializeField] private Vector2Int hitDebrisCount = new Vector2Int(1, 2);
    [Tooltip("Minimum time between debris spawns while mining, so rapid hits don't flood the scene")]
    [SerializeField] private float hitDebrisInterval = 0.12f;
    [Tooltip("How many chunks burst out when the resource is used up (min, max)")]
    [SerializeField] private Vector2Int depletedDebrisCount = new Vector2Int(8, 14);
    [Tooltip("Multiplier on the throw speed for the final burst")]
    [SerializeField] private float depletedForceScale = 1.25f;

    private int _currentAmount;
    private bool _depleted;
    private float _nextDebrisTime;

    private Renderer[] _renderers;
    private Color[] _baseColors;
    private int[] _colorIds; // Which shader color property each renderer uses, -1 if none
    private MaterialPropertyBlock _block;
    private Coroutine _flashRoutine;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    private void Awake()
    {
        CacheRenderers();
    }

    private void Start()
    {
        _currentAmount = resourceAmount;
    }

    // Finds every mesh in this object and its children and remembers its original color
    private void CacheRenderers()
    {
        _block = new MaterialPropertyBlock();

        Renderer[] all = GetComponentsInChildren<Renderer>();
        System.Collections.Generic.List<Renderer> meshes = new System.Collections.Generic.List<Renderer>();

        foreach (Renderer r in all)
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                meshes.Add(r);
        }

        _renderers = meshes.ToArray();
        _baseColors = new Color[_renderers.Length];
        _colorIds = new int[_renderers.Length];

        for (int i = 0; i < _renderers.Length; i++)
        {
            Material mat = _renderers[i].sharedMaterial;

            if (mat != null && mat.HasProperty(BaseColorId))
            {
                _colorIds[i] = BaseColorId;
                _baseColors[i] = mat.GetColor(BaseColorId);
            }
            else if (mat != null && mat.HasProperty(ColorId))
            {
                _colorIds[i] = ColorId;
                _baseColors[i] = mat.GetColor(ColorId);
            }
            else
            {
                _colorIds[i] = -1; // Shader has no color property we can tint
            }
        }
    }

    // Used by anything that doesn't know where it hit: debris comes from the middle of the model
    public bool TakeDamage(int damage)
    {
        return TakeDamage(damage, GetBounds().center, Vector3.up);
    }

    // Used by mining: hitPoint/hitNormal say where the pickaxe landed and which way the surface faces
    public bool TakeDamage(int damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        // Several hits can land in the same frame before Destroy takes effect
        if (_depleted) return true;

        int previousAmount = _currentAmount;
        _currentAmount = Mathf.Clamp(_currentAmount - damage, 0, resourceAmount);

        int amountMined = previousAmount - _currentAmount;
        ResourceManager.Instance.AddResource(resourceType, amountMined);

        if (_currentAmount <= 0)
        {
            _depleted = true;
            SpawnDepletedBurst();
            Destroy(gameObject);
            return true;
        }

        if (amountMined > 0)
        {
            Flash();
            SpawnHitDebris(hitPoint, hitNormal);
        }

        return false;
    }

    // A few chunks thrown out of the spot that was hit
    private void SpawnHitDebris(Vector3 point, Vector3 normal)
    {
        if (debrisPrefab == null || MiningManager.Instance == null) return;
        if (Time.time < _nextDebrisTime) return;

        _nextDebrisTime = Time.time + hitDebrisInterval;

        int count = Random.Range(hitDebrisCount.x, hitDebrisCount.y + 1);
        for (int i = 0; i < count; i++)
            MiningManager.Instance.SpawnDebris(debrisPrefab, point + normal * 0.05f, normal);
    }

    // The model is about to vanish: throw a pile of chunks outward from inside it
    private void SpawnDepletedBurst()
    {
        if (debrisPrefab == null || MiningManager.Instance == null) return;

        Bounds bounds = GetBounds();
        int count = Random.Range(depletedDebrisCount.x, depletedDebrisCount.y + 1);

        for (int i = 0; i < count; i++)
        {
            // Start somewhere inside the model, and throw each chunk away from its middle
            Vector3 point = bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents * 0.6f);
            Vector3 outward = point - bounds.center;
            if (outward.sqrMagnitude < 0.0001f)
                outward = Random.onUnitSphere;

            MiningManager.Instance.SpawnDebris(debrisPrefab, point, outward.normalized, depletedForceScale);
        }
    }

    // World-space bounds around every mesh in this resource
    private Bounds GetBounds()
    {
        if (_renderers == null || _renderers.Length == 0)
            return new Bounds(transform.position, Vector3.zero);

        Bounds bounds = _renderers[0].bounds;
        for (int i = 1; i < _renderers.Length; i++)
            bounds.Encapsulate(_renderers[i].bounds);

        return bounds;
    }

    // Restarts the red flash from full strength
    private void Flash()
    {
        if (_renderers.Length == 0) return;

        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);

        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        for (float t = 0f; t < flashDuration; t += Time.deltaTime)
        {
            SetTint(1f - t / flashDuration);
            yield return null;
        }

        ClearTint();
        _flashRoutine = null;
    }

    // amount: 0 = original color, 1 = fully hitColor
    private void SetTint(float amount)
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_colorIds[i] < 0) continue;

            _renderers[i].GetPropertyBlock(_block);
            _block.SetColor(_colorIds[i], Color.Lerp(_baseColors[i], hitColor, amount));
            _renderers[i].SetPropertyBlock(_block);
        }
    }

    // Removes the override so the materials go back to exactly how they were
    private void ClearTint()
    {
        foreach (Renderer r in _renderers)
            r.SetPropertyBlock(null);
    }

    public void Heal(int healAmount)
    {
        throw new System.NotImplementedException();
    }
}

public enum ResourceType
{
    IronOre,
    CopperOre,
    Pearl,
    Wood,
    Gold
}