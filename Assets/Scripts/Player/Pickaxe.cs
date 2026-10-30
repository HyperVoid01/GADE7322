using System.Collections;
using UnityEngine;

// Plays a procedural swing on the pickaxe. No Animator needed.
public class Pickaxe : MonoBehaviour
{
    [Header("Wind Up (pull back)")]
    [SerializeField] private Vector3 windUpRotation = new Vector3(25f, 0f, 0f);
    [SerializeField] private Vector3 windUpOffset = new Vector3(0f, 0.03f, -0.05f);
    [SerializeField] private float windUpTime = 0.1f;

    [Header("Strike (swing forward)")]
    [SerializeField] private Vector3 strikeRotation = new Vector3(-55f, 0f, 0f);
    [SerializeField] private Vector3 strikeOffset = new Vector3(0f, -0.05f, 0.12f);
    [SerializeField] private float strikeTime = 0.08f;

    [Header("Recover")]
    [SerializeField] private float recoverTime = 0.18f;

    private Vector3 _restPosition;
    private Quaternion _restRotation;
    private bool _isSwinging;

    public bool IsSwinging => _isSwinging;

    private void Awake()
    {
        _restPosition = transform.localPosition;
        _restRotation = transform.localRotation;
    }

    // Hiding the pickaxe mid-swing stops the coroutine, so always start from the rest pose
    private void OnEnable()
    {
        _isSwinging = false;
        transform.localPosition = _restPosition;
        transform.localRotation = _restRotation;
    }

    // Safe to call every frame: ignored while a swing is already playing
    public void Swing()
    {
        if (_isSwinging || !gameObject.activeInHierarchy) return;

        if (SfxPlayer.Instance != null)
            SfxPlayer.Instance.PlayPickaxeSwing(transform.position);

        StartCoroutine(SwingRoutine());
    }

    private IEnumerator SwingRoutine()
    {
        _isSwinging = true;

        yield return MoveTo(_restPosition + windUpOffset, _restRotation * Quaternion.Euler(windUpRotation), windUpTime);
        yield return MoveTo(_restPosition + strikeOffset, _restRotation * Quaternion.Euler(strikeRotation), strikeTime);
        yield return MoveTo(_restPosition, _restRotation, recoverTime);

        _isSwinging = false;
    }

    private IEnumerator MoveTo(Vector3 targetPosition, Quaternion targetRotation, float duration)
    {
        duration = Mathf.Max(duration, 0.0001f);

        Vector3 startPosition = transform.localPosition;
        Quaternion startRotation = transform.localRotation;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            transform.localPosition = Vector3.Lerp(startPosition, targetPosition, k);
            transform.localRotation = Quaternion.Slerp(startRotation, targetRotation, k);
            yield return null;
        }

        transform.localPosition = targetPosition;
        transform.localRotation = targetRotation;
    }
}