using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class EnemyHealthBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyBase enemy; // found automatically in parents if left empty
    [SerializeField] private Image fillImage; // Image Type must be "Filled"

    [Header("Behaviour")]
    [SerializeField] private bool hideWhenFull = true; // only show the bar once the enemy has been damaged
    [SerializeField] private float smoothSpeed = 10f; // how quickly the bar catches up to the real value
    [SerializeField] private bool faceCamera = true;

    [Header("Colors")]
    [SerializeField] private Color fullColor = new Color(0.2f, 0.9f, 0.3f);
    [SerializeField] private Color emptyColor = new Color(0.9f, 0.15f, 0.15f);

    private CanvasGroup _canvasGroup;
    private Camera _camera;
    private float _targetFill = 1f;
    private float _displayedFill = 1f;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();

        if (enemy == null)
            enemy = GetComponentInParent<EnemyBase>();
    }

    // Subscribing here (not Start) guarantees we're listening before EnemyBase.Start fires its first event
    private void OnEnable()
    {
        if (enemy != null)
            enemy.OnHealthChanged += HandleHealthChanged;
    }

    private void OnDisable()
    {
        if (enemy != null)
            enemy.OnHealthChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(int current, int max)
    {
        _targetFill = max > 0 ? (float)current / max : 0f;

        // First update (at spawn): snap straight to the value instead of animating from full
        if (_canvasGroup.alpha == 0f && _targetFill >= 1f)
            _displayedFill = _targetFill;

        _canvasGroup.alpha = hideWhenFull && _targetFill >= 1f ? 0f : 1f;
    }

    private void Update()
    {
        _displayedFill = Mathf.Lerp(_displayedFill, _targetFill, Time.deltaTime * smoothSpeed);

        if (fillImage != null)
        {
            fillImage.fillAmount = _displayedFill;
            fillImage.color = Color.Lerp(emptyColor, fullColor, _displayedFill);
        }
    }

    // LateUpdate so it runs after the enemy has moved along its spline this frame
    private void LateUpdate()
    {
        if (!faceCamera) return;

        if (_camera == null)
            _camera = Camera.main;
        if (_camera == null) return;

        // Matching the camera's rotation (rather than LookAt) keeps every bar parallel to the screen,
        // so bars at the edge of the view don't look skewed
        transform.rotation = _camera.transform.rotation;
    }
}