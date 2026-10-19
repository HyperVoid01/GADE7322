using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class Lazer : Tower
{
    [Header("Lazer Settings")]
    [SerializeField] private LineRenderer beam;
    [SerializeField] private Vector3 targetOffset = Vector3.up * 0.5f; // Aim at the enemy's middle instead of its pivot

    private void Awake()
    {
        if (beam == null)
            beam = GetComponent<LineRenderer>();

        beam.useWorldSpace = true;
        beam.positionCount = 2;
        beam.enabled = false;
    }

    // Damage tick: called every fireRate seconds while a target exists
    protected override void Fire()
    {
        if (CurrentEnemy == null) return;

        CurrentEnemy.TakeDamage(damage);
    }

    // The beam is drawn every frame so it stays locked on
    private void LateUpdate()
    {
        EnemyBase target = CurrentEnemy;

        if (target == null)
        {
            if (beam.enabled) beam.enabled = false;
            return;
        }

        if (!beam.enabled) beam.enabled = true;

        beam.SetPosition(0, projectileSpawnPoint.position);
        beam.SetPosition(1, target.transform.position + targetOffset);
    }

    private void OnDisable()
    {
        if (beam != null) beam.enabled = false;
    }
}