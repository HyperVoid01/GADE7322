using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Harpoon : Tower
{
    [Header("Harpoon Settings")]
    [SerializeField] private int maxEnemiesHit = 3; // Enemies hit before returning
    [SerializeField] private float chainRange = 6f; // How far it searches for the next enemy
    [SerializeField] private float returnSpeed = 15f; // Speed while returning to the tower
    [SerializeField] private float hitDistance = 0.3f; // How close counts as a hit

    private Transform _head; // The harpoon itself, resting at projectileSpawnPoint
    private bool _isActive;

    private void Awake()
    {
        // The harpoon exists permanently and rests at the spawn point
        _head = Instantiate(projectile, projectileSpawnPoint).transform;
        _head.localPosition = Vector3.zero;
        _head.localRotation = Quaternion.identity;
    }

    protected override void Fire()
    {
        // Can't throw again until the harpoon is back
        if (_isActive || CurrentEnemy == null) return;

        StartCoroutine(HarpoonRoutine(CurrentEnemy));
    }

    private IEnumerator HarpoonRoutine(EnemyBase firstTarget)
    {
        _isActive = true;
        _head.SetParent(null);

        HashSet<EnemyBase> hitEnemies = new HashSet<EnemyBase>();
        EnemyBase target = firstTarget;

        while (hitEnemies.Count < maxEnemiesHit)
        {
            // Target died or was never found, look for a new one from the harpoon's position
            if (target == null)
                target = FindNextTarget(_head.position, hitEnemies);

            // Out of enemies
            if (target == null) break;

            // Fly toward the enemy, tracking its live position since enemies move
            while (target != null &&
                   (target.transform.position - _head.position).sqrMagnitude > hitDistance * hitDistance)
            {
                MoveHead(target.transform.position, projectileSpeed);
                yield return null;
            }

            // Enemy was killed by something else mid-flight, retarget
            if (target == null) continue;

            hitEnemies.Add(target);
            target.TakeDamage(damage);

            target = FindNextTarget(_head.position, hitEnemies);
        }

        // Return to the resting position (which follows the barrel as it rotates)
        while ((_head.position - projectileSpawnPoint.position).sqrMagnitude > 0.0001f)
        {
            MoveHead(projectileSpawnPoint.position, returnSpeed);
            yield return null;
        }

        _head.SetParent(projectileSpawnPoint);
        _head.localPosition = Vector3.zero;
        _head.localRotation = Quaternion.identity;

        _isActive = false;
    }

    // Closest enemy to 'from' within chainRange that hasn't been hit yet
    private EnemyBase FindNextTarget(Vector3 from, HashSet<EnemyBase> alreadyHit)
    {
        Collider[] hits = Physics.OverlapSphere(from, chainRange, enemyMask);

        EnemyBase best = null;
        float bestDistance = float.MaxValue;

        foreach (Collider hit in hits)
        {
            EnemyBase enemy = hit.GetComponentInParent<EnemyBase>();
            if (enemy == null || alreadyHit.Contains(enemy)) continue;

            float distance = (enemy.transform.position - from).sqrMagnitude;
            if (distance < bestDistance)
            {
                best = enemy;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void MoveHead(Vector3 destination, float speed)
    {
        Vector3 direction = destination - _head.position;
        if (direction.sqrMagnitude > 0.0001f)
            _head.rotation = Quaternion.LookRotation(direction);

        _head.position = Vector3.MoveTowards(_head.position, destination, speed * Time.deltaTime);
    }

    // The harpoon is unparented while flying, so clean it up with the tower
    private void OnDestroy()
    {
        if (_head != null)
            Destroy(_head.gameObject);
    }
}