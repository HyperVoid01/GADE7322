using UnityEngine;

// Draws a ring on the ground showing a tower's range. TowerUpgradeInteractor tells it which tower to show.
// Each point of the ring is raycast down onto the terrain so it follows hills and dips instead of clipping through them.
[RequireComponent(typeof(LineRenderer))]
public class TowerRangeIndicator : MonoBehaviour
{
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private int segments = 96;
    [Tooltip("How far above the ground the ring floats, so it doesn't flicker into the terrain")]
    [SerializeField] private float groundOffset = 0.15f;

    [Header("Ground Probe")]
    [Tooltip("Each ring point is raycast down from this far above the tower's base")]
    [SerializeField] private float probeHeight = 8f;
    [Tooltip("How far below the tower's base the ground can be and still be found")]
    [SerializeField] private float probeDepth = 15f;
    [Tooltip("Re-fits the ring to the ground this often, so it follows mined terrain")]
    [SerializeField] private float refreshInterval = 0.25f;

    [Header("Look")]
    [SerializeField] private float lineWidth = 0.15f;
    [SerializeField] private Color color = new Color(0.3f, 0.85f, 1f, 0.9f);

    private LineRenderer _line;
    private Tower _tower;
    private float _drawnRange = -1f;
    private float _nextRefreshTime;

    private void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.loop = true;
        _line.positionCount = segments;
        _line.startWidth = lineWidth;
        _line.endWidth = lineWidth;
        _line.startColor = color;
        _line.endColor = color;
        _line.enabled = false;
    }

    // Pass null to hide
    public void Show(Tower tower)
    {
        if (tower == null)
        {
            Hide();
            return;
        }

        // Redraw straight away for a new tower or a range upgrade, otherwise on the refresh timer
        bool changed = tower != _tower || !Mathf.Approximately(tower.Range, _drawnRange);

        _tower = tower;
        _line.enabled = true;

        if (changed || Time.time >= _nextRefreshTime)
            Redraw();
    }

    public void Hide()
    {
        _tower = null;
        _drawnRange = -1f;
        _line.enabled = false;
    }

    private void Redraw()
    {
        _nextRefreshTime = Time.time + refreshInterval;
        _drawnRange = _tower.Range;

        Vector3 center = _tower.transform.position;
        float step = 2f * Mathf.PI / segments;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * step;
            Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _drawnRange;

            // Drop the point onto the terrain; if there's no ground (map edge, deep pit) keep it level with the tower
            Vector3 origin = new Vector3(point.x, center.y + probeHeight, point.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeHeight + probeDepth, groundMask, QueryTriggerInteraction.Ignore))
                point.y = hit.point.y;

            _line.SetPosition(i, point + Vector3.up * groundOffset);
        }
    }
}
