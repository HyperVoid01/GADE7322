using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TowerBuilder : MonoBehaviour
{
    [Header("Build Settings")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private LayerMask terrainLayer;
    [SerializeField] private float buildRange = 20f;

    [Header("Spacing")]
    [SerializeField] private LayerMask towerLayer;       // Layer your towers' colliders are on
    [SerializeField] private float minTowerSpacing = 2f;

    [Header("PlaceHolders & Objects")]
    [SerializeField] private GameObject placeHolder;
    [SerializeField] private GameObject towerDevice;
    [Tooltip("Leave empty to use every renderer on the placeholder")]
    [SerializeField] private Renderer[] placeholderRenderers;
    [SerializeField] private Material validMaterial;
    [SerializeField] private Material invalidMaterial;

    [SerializeField] private float terrainEditorDelay = 1f;

    [Header("Drop Settings")]
    [Tooltip("How far above the top of the map the tower starts")]
    [SerializeField] private float skyHeight = 20f;
    [SerializeField] private float initialFallSpeed = 5f;
    [SerializeField] private float fallAcceleration = 40f;

    [Header("Tunnel Settings")]
    [Tooltip("Radius of the shaft dug above the landing spot. Should be at least the tower's half-width.")]
    [SerializeField] private float shaftRadius = 2.5f;
    [Tooltip("The shaft starts this far above the landing point, so the ground under the tower stays intact")]
    [SerializeField] private float shaftBottomOffset = 1f;

    [Header("Landing Shake")]
    [SerializeField] private float shakeMagnitude = 0.35f;
    [SerializeField] private float shakeDuration = 0.5f;
    [Tooltip("Players further than this from the landing feel no shake")]
    [SerializeField] private float shakeMaxDistance = 40f;

    public static TowerBuilder Instance;
    public bool IsBuilding { get; private set; }

    private TowerData _selectedTower;
    private int _buildStartFrame;
    private bool? _placeholderValid;

    // Spots where a tower is still falling, so two towers can't be queued on the same spot
    private readonly List<Vector3> _pendingLandings = new List<Vector3>();

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this); // This lives on the player, so never destroy the whole GameObject
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (placeholderRenderers == null || placeholderRenderers.Length == 0)
            placeholderRenderers = placeHolder.GetComponentsInChildren<Renderer>(true);

        placeHolder.SetActive(false);
        TowerDevice.Instance.deviceObject = towerDevice;
        towerDevice.SetActive(false);
        TowerDevice.Instance.playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        if (!IsBuilding) return;

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Mouse1))
        {
            CancelBuild();
            return;
        }

        Aim();
    }

    public void EnterBuildMode(TowerData tower)
    {
        _selectedTower = tower;
        _buildStartFrame = Time.frameCount; // So the click on the buy button doesn't also place the tower
        IsBuilding = true;
        placeHolder.SetActive(true);
        TerrainEditor.Instance.enabled = false;
    }

    // Moves the placeholder to where the player is looking
    private void Aim()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit, buildRange, terrainLayer))
        {
            placeHolder.SetActive(false);
            return;
        }

        placeHolder.SetActive(true);
        placeHolder.transform.position = hit.point;

        bool valid = IsValidPlacement(hit.point);
        SetPlaceholderValid(valid);

        if (Input.GetKeyDown(KeyCode.Mouse0) && Time.frameCount != _buildStartFrame)
        {
            if (valid)
                TryBuild(hit.point);
            else if (SfxPlayer.Instance != null)
                SfxPlayer.Instance.PlayDenied();
        }
    }

    private bool IsValidPlacement(Vector3 point)
    {
        // Not on paths, not near the center, and not on top of another tower
        if (MarchingCubes.Instance.IsWithinPathProtection(point) ||
            MarchingCubes.Instance.IsWithinCenterProtection(point) ||
            Physics.CheckSphere(point, minTowerSpacing, towerLayer))
            return false;

        // ...or on top of a tower that is still on its way down
        float sqrSpacing = minTowerSpacing * minTowerSpacing;
        foreach (Vector3 landing in _pendingLandings)
        {
            if ((landing - point).sqrMagnitude < sqrSpacing)
                return false;
        }

        return true;
    }

    private void TryBuild(Vector3 position)
    {
        // Re-check in case resources changed while placing
        if (!ResourceManager.Instance.HasEnoughResources(_selectedTower.costs))
        {
            if (SfxPlayer.Instance != null)
                SfxPlayer.Instance.PlayDenied();

            CancelBuild();
            return;
        }

        ResourceManager.Instance.SpendResources(_selectedTower.costs);

        if (SfxPlayer.Instance != null)
            SfxPlayer.Instance.PlayPurchase();

        GameObject prefab = _selectedTower.prefab;
        EndBuild();
        StartCoroutine(DropTower(prefab, position));
    }

    // Digs a tunnel to the landing spot, then drops the tower from the sky
    private IEnumerator DropTower(GameObject prefab, Vector3 landing)
    {
        _pendingLandings.Add(landing);

        // Removes any terrain above the landing spot (skips protected areas)
        MarchingCubes.Instance.CarveShaft(landing, shaftRadius, shaftBottomOffset);

        float mapTop = MarchingCubes.Instance.WorldSize.y;
        float startY = Mathf.Max(mapTop, landing.y) + skyHeight;

        GameObject tower = Instantiate(prefab, new Vector3(landing.x, startY, landing.z), Quaternion.identity);

        // Keep the tower inert until it lands. Start() doesn't run on a disabled component,
        // so the tower won't shoot or acquire targets until it's enabled below.
        Tower logic = tower.GetComponent<Tower>();
        if (logic != null) logic.enabled = false;

        Collider[] colliders = tower.GetComponentsInChildren<Collider>();
        foreach (Collider c in colliders) c.enabled = false;

        float speed = initialFallSpeed;

        while (tower != null && tower.transform.position.y > landing.y)
        {
            speed += fallAcceleration * Time.deltaTime;
            float newY = Mathf.Max(landing.y, tower.transform.position.y - speed * Time.deltaTime);
            tower.transform.position = new Vector3(landing.x, newY, landing.z);
            yield return null;
        }

        _pendingLandings.Remove(landing);

        if (tower == null) yield break;

        tower.transform.position = landing;

        foreach (Collider c in colliders)
        {
            if (c != null) c.enabled = true;
        }

        if (logic != null) logic.enabled = true;

        if (CameraShake.Instance != null)
            CameraShake.Instance.ShakeAt(landing, shakeMagnitude, shakeDuration, shakeMaxDistance);

        if (SfxPlayer.Instance != null)
            SfxPlayer.Instance.PlayTowerLand(landing);
    }

    private void CancelBuild()
    {
        EndBuild(); // Nothing was spent, so nothing to refund
    }

    private void EndBuild()
    {
        IsBuilding = false;
        _selectedTower = null;
        _placeholderValid = null;
        placeHolder.SetActive(false);
        StartCoroutine(EnableTerrainEditorAfterDelay());
    }

    private IEnumerator EnableTerrainEditorAfterDelay()
    {
        yield return new WaitForSeconds(terrainEditorDelay);
        TerrainEditor.Instance.enabled = true;
    }

    // Tints the placeholder so the player can see if the spot is valid
    private void SetPlaceholderValid(bool valid)
    {
        if (_placeholderValid == valid || validMaterial == null || invalidMaterial == null) return;

        _placeholderValid = valid;
        Material mat = valid ? validMaterial : invalidMaterial;
        foreach (Renderer r in placeholderRenderers)
            r.sharedMaterial = mat;
    }
}