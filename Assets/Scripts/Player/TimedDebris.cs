using System.Collections;
using UnityEngine;

// Removes thrown debris after a while, shrinking it away instead of popping out of existence
public class TimedDebris : MonoBehaviour
{
    // How many debris objects currently exist, used by MiningManager to cap them
    public static int ActiveCount { get; private set; }

    private float _lifetime = 5f;
    private float _shrinkTime = 0.5f;

    // Keeps the count correct when Enter Play Mode skips domain reloads
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveCount = 0;
    }

    public void Init(float lifetime, float shrinkTime)
    {
        _lifetime = Mathf.Max(lifetime, 0.1f);
        _shrinkTime = Mathf.Clamp(shrinkTime, 0f, _lifetime);
    }

    private void OnEnable()
    {
        ActiveCount++;
    }

    private void OnDisable()
    {
        ActiveCount--;
    }

    private void Start()
    {
        StartCoroutine(LifetimeRoutine());
    }

    private IEnumerator LifetimeRoutine()
    {
        yield return new WaitForSeconds(_lifetime - _shrinkTime);

        Vector3 startScale = transform.localScale;

        for (float t = 0f; t < _shrinkTime; t += Time.deltaTime)
        {
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t / _shrinkTime);
            yield return null;
        }

        Destroy(gameObject);
    }
}