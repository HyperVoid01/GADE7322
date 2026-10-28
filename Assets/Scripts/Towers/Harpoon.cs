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
    private bool _headNeedsRebuild; // Projectile changed mid-throw, swap it once it's back

    private int _baseMaxEnemiesHit;
    private float _baseChainRange;

    protected override void Awake()
    {
        base.Awake();

        // The harpoon exists permanently and rests at the spawn point
        SpawnHead();
    }

    // ---------------- Upgrades ----------------

    protected override void CacheUniqueBaseStats()
    {
        _baseMaxEnemiesHit = maxEnemiesHit;
        _baseChainRange = chainRange;
    }

    protected override void ResetUniqueStats()
    {
        maxEnemiesHit = _baseMaxEnemiesHit;
        chainRange = _baseChainRange;
    }

    protected override bool ApplyUniqueUpgrade(UpgradeStat stat, float value)
    {
        switch (stat)
        {
            case UpgradeStat.HarpoonTargets:
                maxEnemiesHit += Mathf.RoundToInt(value);
                return true;

            case UpgradeStat.HarpoonChainRange:
                chainRange += value;
                return true;
        }

        return false;
    }

    // The resting harpoon is a child of the old model's spawn point, so unparent it before that gets destroyed
    protected override void OnModelSwapping()
    {
        if (_head != null && !_isActive)
            _head.SetParent(null, true);
    }

    protected override void OnVisualsChanged(bool modelChanged, bool projectileChanged)
    {
        if (projectileChanged)
            _headNeedsRebuild = true;

        // Mid-throw: the return routine already flies back to the (new) spawn point and handles the rest
        if (_isActive) return;

        if (_headNeedsRebuild)
            SpawnHead();
        else
            DockHead();
    }

    // ---------------- Harpoon ----------------

    private void SpawnHead()
    {
        if (_head != null)
            Destroy(_head.gameObject);

        _head = Instantiate(projectile, projectileSpawnPoint).transform;
        DockHead();
        _headNeedsRebuild = false;
    }

    private void DockHead()
    {
        _head.SetParent(projectileSpawnPoint);
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

        // Return to the resting position (which follows the barrel as it rotates).
        // projectileSpawnPoint is re-read every frame, so a level up mid-throw sends it to the new model.
        while ((_head.position - projectileSpawnPoint.position).sqrMagnitude > 0.0001f)
        {
            MoveHead(projectileSpawnPoint.position, returnSpeed);
            yield return null;
        }

        if (_headNeedsRebuild)
            SpawnHead();
        else
            DockHead();

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
