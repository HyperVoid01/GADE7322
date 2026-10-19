using System.Collections;
using UnityEngine;

// Parent class for all towers
public class Tower : MonoBehaviour, ITargetable
{
    [Header("Health")]
    [SerializeField] protected int currentHealth;
    [SerializeField] protected int maxHealth;

    [Header("Damage")]
    [SerializeField] protected int damage;
    [SerializeField] protected float range;
    [SerializeField] protected float fireRate;
    [SerializeField] protected LayerMask enemyMask;

    [Header("Projectile Settings")]
    [SerializeField] protected GameObject projectile;
    [SerializeField] protected Transform projectileSpawnPoint;
    [SerializeField] protected float projectileSpeed;

    [Header("Targeting & Rotation")]
    [SerializeField] protected Transform turretYaw;
    [SerializeField] protected bool useBarrel = true;
    [SerializeField] protected Transform barrel;
    [SerializeField] protected float rotationSpeed = 5f;

    [Header("Prediction")]
    [SerializeField] private int predictionIterations = 3;

    private EnemyBase _currentEnemy;
    protected EnemyBase CurrentEnemy => _currentEnemy;

    private void Start()
    {
        currentHealth = maxHealth;

        StartCoroutine(CheckForEnemies());
        StartCoroutine(FollowTarget());
        StartCoroutine(Shoot());
    }

    // Searches a radius around the tower and chooses the closest enemy
    private IEnumerator CheckForEnemies()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            Collider[] hits = Physics.OverlapSphere(transform.position, range, enemyMask);

            if (hits.Length == 0)
            {
                _currentEnemy = null;
                continue;
            }

            Collider closest = hits[0];
            float closestDistance = Vector3.Distance(transform.position, closest.transform.position);

            foreach (Collider hit in hits)
            {
                float distance = Vector3.Distance(transform.position, hit.transform.position);
                if (distance < closestDistance)
                {
                    closest = hit;
                    closestDistance = distance;
                }
            }

            _currentEnemy = closest.GetComponent<EnemyBase>();
        }
    }
    
    // Moves barrel to aim at target
    private IEnumerator FollowTarget()
    {
        while (true)
        {
            if (_currentEnemy)
            {
                Vector3 targetPos = PredictImpactPosition(_currentEnemy);

                // Rotates base
                Vector3 yawDirection = targetPos - turretYaw.position;
                yawDirection.y = 0f;

                if (yawDirection.sqrMagnitude > 0.0001f)
                {
                    Quaternion yawRotation = Quaternion.LookRotation(yawDirection);
                    turretYaw.rotation = Quaternion.Slerp(turretYaw.rotation, yawRotation, rotationSpeed * Time.deltaTime);
                }

                // Rotates barrel up and down (only for towers that have one)
                if (useBarrel && barrel != null)
                {
                    Vector3 direction = targetPos - barrel.position;
                    Vector3 localDirection = turretYaw.InverseTransformDirection(direction);

                    float horizontalDistance = new Vector2(localDirection.x, localDirection.z).magnitude;
                    float pitchAngle = -Mathf.Atan2(localDirection.y, horizontalDistance) * Mathf.Rad2Deg;

                    Quaternion pitchRotation = Quaternion.Euler(pitchAngle, 0f, 0f);
                    barrel.localRotation = Quaternion.Slerp(barrel.localRotation, pitchRotation, rotationSpeed * Time.deltaTime);
                }
            }

            yield return null;
        }
    }

    // Spawns projectile and launches it at enemy
    private IEnumerator Shoot()
    {
        while (true)
        {
            if (_currentEnemy)
                Fire();

            yield return new WaitForSeconds(fireRate);
        }
    }
    
    protected virtual void Fire()
    {
        Vector3 impactPos = PredictImpactPosition(_currentEnemy);

        GameObject newProjectile = Instantiate(projectile, projectileSpawnPoint.position, projectileSpawnPoint.rotation);

        Rigidbody projectileRb = newProjectile.GetComponent<Rigidbody>();
        newProjectile.GetComponent<TowerProjectile>().damage = damage;

        Vector3 fireDirection = (impactPos - projectileSpawnPoint.position).normalized;
        projectileRb.linearVelocity = fireDirection * projectileSpeed;
    }
    
    // Predicts enemy's future position
    private Vector3 PredictImpactPosition(EnemyBase enemy)
    {
        Vector3 predictedPos = enemy.transform.position;
        
        // Velocity = distance / time, therefore: time = distance / velocity
        float timeToTarget = Vector3.Distance(projectileSpawnPoint.position, predictedPos) / projectileSpeed;
        
        // Re-iterate last value for more accuracy
        for (int i = 0; i < predictionIterations; i++)
        {
            predictedPos = enemy.GetPredictedPosition(timeToTarget);
            timeToTarget = Vector3.Distance(projectileSpawnPoint.position, predictedPos) / projectileSpeed;
        }

        return predictedPos;
    }

    public virtual bool TakeDamage(int damageAmount)
    {
        if (currentHealth == 0)
            return true;

        currentHealth = Mathf.Clamp(currentHealth - damageAmount, 0, maxHealth);

        if (currentHealth == 0)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }

    public void Heal(int healAmount)
    {
        currentHealth = Mathf.Clamp(currentHealth + healAmount, 0, maxHealth);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}