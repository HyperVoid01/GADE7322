using UnityEngine;

public class CameraShake : MonoBehaviour
{
    [SerializeField] private float frequency = 25f;

    public static CameraShake Instance;

    private Vector3 _basePosition;
    private float _magnitude;
    private float _duration;
    private float _timeLeft;
    private bool _isShaking;

    private void Awake()
    {
        Instance = this;
        _basePosition = transform.localPosition;
    }

    public void Shake(float magnitude, float duration)
    {
        // A new shake only replaces the current one if it's stronger than what's left of it
        float currentStrength = _isShaking ? _magnitude * (_timeLeft / _duration) : 0f;
        if (magnitude < currentStrength) return;

        _magnitude = magnitude;
        _duration = Mathf.Max(duration, 0.01f);
        _timeLeft = _duration;
        _isShaking = true;
    }

    // Shakes less the further the camera is from the point
    public void ShakeAt(Vector3 worldPoint, float magnitude, float duration, float maxDistance)
    {
        float falloff = 1f - Mathf.Clamp01(Vector3.Distance(transform.position, worldPoint) / maxDistance);
        if (falloff <= 0f) return;

        Shake(magnitude * falloff, duration);
    }

    private void LateUpdate()
    {
        if (!_isShaking) return;

        _timeLeft -= Time.deltaTime;

        if (_timeLeft <= 0f)
        {
            transform.localPosition = _basePosition;
            _isShaking = false;
            return;
        }

        float fade = _timeLeft / _duration;
        float strength = _magnitude * fade * fade; // Eases out

        float t = Time.time * frequency;
        Vector3 offset = new Vector3(
            Mathf.PerlinNoise(t, 0f) - 0.5f,
            Mathf.PerlinNoise(0f, t) - 0.5f,
            Mathf.PerlinNoise(t, t) - 0.5f) * (2f * strength);

        transform.localPosition = _basePosition + offset;
    }
}