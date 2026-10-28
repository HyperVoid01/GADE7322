using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class TowerProjectile : MonoBehaviour
{
    public int damage;

    [Header("Splash (set by the tower that fired it)")]
    public float splashRadius;      // 0 = no splash
    public int splashDamage;
    public LayerMask splashMask;
    public GameObject splashEffect; // Optional

    private bool _hasHit; // Several trigger events can land in one frame before Destroy takes effect

    private void Start()
    {
        Destroy(gameObject, 5f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_hasHit || !other.CompareTag("Enemy")) return;

        EnemyBase enemy = other.GetComponentInParent<EnemyBase>();
        if (enemy == null) return;

        _hasHit = true;
        enemy.TakeDamage(damage);

        if (splashRadius > 0f)
            Splash(enemy);

        Destroy(gameObject);
    }

    // Damages every other enemy in the radius once
    private void Splash(EnemyBase directHit)
    {
        Vector3 center = transform.position;

        if (splashEffect != null)
            Instantiate(splashEffect, center, Quaternion.identity);

        HashSet<EnemyBase> damaged = new HashSet<EnemyBase> { directHit };

        foreach (Collider hit in Physics.OverlapSphere(center, splashRadius, splashMask))
        {
            EnemyBase enemy = hit.GetComponentInParent<EnemyBase>();
            if (enemy == null || !damaged.Add(enemy)) continue;

            enemy.TakeDamage(splashDamage);
        }
    }
}
