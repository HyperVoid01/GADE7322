using UnityEngine;

public class TerrainEditor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private LayerMask terrainLayer;

    [Header("Editing")]
    [SerializeField] private float mineRange = 6f;
    [SerializeField] private float editRadius = 2.5f;
    [SerializeField] private float editStrength = 1f;
    [SerializeField] private bool continuousWhileHeld = false;
    [SerializeField] private float editsPerSecond = 6f;
    
    [Header("Keybinds")]
    [SerializeField] private KeyCode mineKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode buildKey = KeyCode.Mouse1;

    private float _nextEditTime;
    
    public static TerrainEditor Instance;

    // No mining while the tower shop or upgrade menu is open, or a tower is being placed.
    // Checked every frame so it can't get out of sync with the enabled toggles in TowerBuilder.
    private static bool IsBlockedByUI =>
        (TowerDevice.Instance != null && TowerDevice.Instance.IsActive) ||
        (TowerBuilder.Instance != null && TowerBuilder.Instance.IsBuilding) ||
        (TowerUpgradeMenu.Instance != null && TowerUpgradeMenu.Instance.IsOpen);

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Update()
    {
        if (IsBlockedByUI) return;

        if (continuousWhileHeld)
        {
            if (Input.GetKey(mineKey) && Time.time >= _nextEditTime) // Mine
            {
                TryEdit(true);
                _nextEditTime = Time.time + 1f / editsPerSecond;
            }
            else if (Input.GetKey(buildKey) && Time.time >= _nextEditTime) // Build
            {
                // TryEdit(false);
                // _nextEditTime = Time.time + 1f / editsPerSecond;
            }
        }
        else
        {
            if (Input.GetKey(mineKey)) 
            {
                TryEdit(true); // Mine
            }
            else if (Input.GetKey(buildKey)) 
            {
                //TryEdit(false); // Build
            }
        }
    }

    private void TryEdit(bool dig)
    {
        // Swing the pickaxe on every mine action. It ignores the call while a swing is already playing,
        // so a tap plays one full swing and holding the button repeats it.
        if (dig && MiningManager.Instance != null)
            MiningManager.Instance.Swing();

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit, mineRange, terrainLayer))
        {
            MiningManager.Instance.TryMine(ray);
            return;
        }

        // Sparks where the pickaxe meets the terrain
        if (dig && MiningManager.Instance != null)
            MiningManager.Instance.PlayImpact(hit.point, hit.normal);
        
        // Adds more resolution to digging/mining
        Vector3 nudge = ray.direction.normalized * 0.1f;
        Vector3 point = dig ? hit.point + nudge : hit.point - nudge;

        MarchingCubes.Instance.EditTerrain(point, editRadius, editStrength, dig);
        
        if (KelpSpawner.Instance != null)
            KelpSpawner.Instance.RemoveKelpInRadius(hit.point, editRadius);
    }
}