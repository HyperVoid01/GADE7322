using UnityEngine;

public class JellyFish : EnemyBase
{
    [Header("Jellyfish Settings")]
    [SerializeField] private EnemyProjectile projectilePrefab;
    [SerializeField] private Transform firePoint;        // Optional, falls back to the jellyfish's position
    [SerializeField] private float projectileSpeed = 8f;

    public override void Attack()
    {
        Tower target = CurrentTarget;
        if (target == null || projectilePrefab == null) return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;

        EnemyProjectile projectile = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        projectile.Initialize(target, damage, projectileSpeed);
    }
}