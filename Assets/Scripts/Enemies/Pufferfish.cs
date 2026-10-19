using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pufferfish : EnemyBase
{
    [Header("Pufferfish Settings")] 
    [SerializeField] private float inflatedSize = 1.5f;      // Scale multiplier when inflated
    [SerializeField] private float changeSpeed = 3f;         // Scale units per second for inflating/deflating
    [SerializeField] private float inflatedDuration = 0.5f;  // Time spent fully inflated

    private Vector3 _baseScale;
    private bool _isInflating;

    protected override void Awake()
    {
        base.Awake();
        _baseScale = transform.localScale;
    }

    public override void Attack()
    {
        // Don't start a new puff while one is still playing
        if (_isInflating) return;

        StartCoroutine(InflateAndDamage());
    }

    private IEnumerator InflateAndDamage()
    {
        _isInflating = true;

        Vector3 inflatedScale = _baseScale * inflatedSize;

        // Inflate
        while (transform.localScale != inflatedScale)
        {
            transform.localScale = Vector3.MoveTowards(
                transform.localScale, inflatedScale, changeSpeed * Time.deltaTime);
            yield return null;
        }

        // Damage every tower in range
        DamageAllTowersInRange();

        // Stay inflated for a moment
        yield return new WaitForSeconds(inflatedDuration);

        // Deflate
        while (transform.localScale != _baseScale)
        {
            transform.localScale = Vector3.MoveTowards(
                transform.localScale, _baseScale, changeSpeed * Time.deltaTime);
            yield return null;
        }

        _isInflating = false;
    }

    private void DamageAllTowersInRange()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, range, towerMask);

        // A tower may have several colliders, so track which ones we've already hit
        HashSet<Tower> damaged = new HashSet<Tower>();

        foreach (Collider hit in hits)
        {
            Tower tower = hit.GetComponentInParent<Tower>();
            if (tower == null || !damaged.Add(tower)) continue;

            tower.TakeDamage(damage);
        }
    }
}