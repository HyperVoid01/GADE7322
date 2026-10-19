using UnityEngine;

public class EnemyProjectile : MonoBehaviour
{
    [SerializeField] private float hitDistance = 0.1f;
    [SerializeField] private float lifetime = 5f;

    private Tower _target;
    private Vector3 _targetPos;
    private int _damage;
    private float _speed;

    public void Initialize(Tower target, int damage, float speed)
    {
        _target = target;
        _targetPos = target.transform.position; // Towers don't move, so cache once
        _damage = damage;
        _speed = speed;

        transform.LookAt(_targetPos);
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        // Tower was destroyed while the projectile was in flight
        if (_target == null)
        {
            Destroy(gameObject);
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, _targetPos, _speed * Time.deltaTime);

        if ((transform.position - _targetPos).sqrMagnitude <= hitDistance * hitDistance)
        {
            _target.TakeDamage(_damage);
            Destroy(gameObject);
        }
    }
}