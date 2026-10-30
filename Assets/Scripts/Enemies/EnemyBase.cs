using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(SplineAnimate))]
public class EnemyBase : MonoBehaviour, ITargetable
{
    [Header("Health")]
    [SerializeField] protected int currentHealth;
    [SerializeField] protected int maxHealth;
    
    [Header("Damage")]
    [SerializeField] protected int damage;
    [SerializeField] protected float range;
    [SerializeField] protected float fireRate;
    [SerializeField] protected LayerMask towerMask;
    
    [Header("Movement")]
    [SerializeField] protected float moveSpeed;

    [Header("Audio")]
    [Tooltip("1 = normal. Bigger enemies use a higher value for a deeper death sound")]
    [SerializeField] protected float deathSoundSize = 1f;
    
    private Vector3 _pathOffset; // Offset from spline
    private SplineAnimate _animate;
    
    private Tower _currentTarget;
    protected Tower CurrentTarget => _currentTarget;
    
    private bool _isPaused;
    private float _nextHitSoundTime;
    
    public float MoveSpeed => moveSpeed;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    // Fired whenever health changes (and once at spawn). Args: current health, max health
    public event Action<int, int> OnHealthChanged;

    protected virtual void Awake()
    {
        _animate = GetComponent<SplineAnimate>();
    }

    private void Start()
    {
        currentHealth = maxHealth;
        _animate.MaxSpeed = moveSpeed;

        // Health bars subscribe in OnEnable, so they're already listening by the time Start runs
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        StartCoroutine(CheckForTowers());
        StartCoroutine(AttackCycle());
        
        _animate.Play();
        _isPaused = false;
    }

    // Deals damage to current tower target
    private IEnumerator AttackCycle()
    {
        while (true)
        {
            if (_currentTarget)
            {
                _animate.Pause();
                _isPaused = true;
                
                //_currentTarget.TakeDamage(damage);
                Attack();
            }
            else
            {
                _animate.Play();
                _isPaused = false;
            }
            
            yield return new WaitForSeconds(fireRate);
        }
    }

    public virtual void Attack()
    {
        if (SfxPlayer.Instance != null)
            SfxPlayer.Instance.PlayBite(transform.position);

        _currentTarget.TakeDamage(damage);
    }

    // Searches in a radius for towers and sets closest one as target
    private IEnumerator CheckForTowers()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            Collider[] hits = Physics.OverlapSphere(transform.position, range, towerMask);

            if (hits.Length == 0)
            {
                _currentTarget = null;
                continue;
            }

            Tower closestTower = null;
            float closestDistance = float.MaxValue;

            foreach (Collider hit in hits)
            {
                Tower tower = hit.GetComponentInParent<Tower>();
                if (tower == null) continue; 
                
                float distance = Vector3.Distance(transform.position, hit.transform.position);
                if (distance < closestDistance)
                {
                    closestTower = tower;
                    closestDistance = distance;
                }
            }

            _currentTarget = closestTower;
        }
    }
    
    // Predicts enemy position after certain time
    public Vector3 GetPredictedPosition(float timeAhead)
    {
        // Does not predict if enemy is stationary
        if (_isPaused || _animate == null || _animate.Container == null)
            return transform.position;
        
        Spline spline = _animate.Container.Spline;
        if (spline == null)
            return transform.position;

        float splineLength = spline.GetLength();
        if (splineLength <= 0f)
            return transform.position;
        
        float currentDistance = _animate.NormalizedTime * splineLength;
        
        float futureDistance = currentDistance + moveSpeed * timeAhead;
        
        float futureNormalizedTime = Mathf.Clamp01(futureDistance / splineLength);
        
        Vector3 localPos = spline.EvaluatePosition(futureNormalizedTime);
        return _animate.Container.transform.TransformPoint(localPos);
    }

    public bool TakeDamage(int damageAmount)
    {
        if (currentHealth == 0)
            return true;
        
        currentHealth = Mathf.Clamp(currentHealth - damageAmount, 0, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth == 0)
        {
            // Played from SfxPlayer's pooled voices, so it isn't cut off by Destroy below
            if (SfxPlayer.Instance != null)
                SfxPlayer.Instance.PlayEnemyDeath(transform.position, deathSoundSize);

            EnemySpawner.Instance.RemoveEnemy(gameObject);
            Destroy(gameObject);
            return true;
        }

        // Throttled: the laser damages several times a second
        if (damageAmount > 0 && SfxPlayer.Instance != null && Time.time >= _nextHitSoundTime)
        {
            _nextHitSoundTime = Time.time + 0.2f;
            SfxPlayer.Instance.PlayEnemyHit(transform.position);
        }
        
        return false;
    }

    public void Heal(int healAmount)
    {
        currentHealth = Mathf.Clamp(currentHealth + healAmount, 0, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}