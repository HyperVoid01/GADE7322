using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class TowerProjectile : MonoBehaviour
{
    public int damage;

    private void Start()
    {
        Destroy(gameObject, 5f);
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {
            other.GetComponent<EnemyBase>().TakeDamage(damage);
            Destroy(gameObject);
        }
    }
}
