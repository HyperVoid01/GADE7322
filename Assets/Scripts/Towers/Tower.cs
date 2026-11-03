using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("Upgrades")]
    [SerializeField] protected TowerUpgradeData upgradeData;
    [Tooltip("The Level 1 model already in this prefab (needs a TowerModel component). Required for model swaps")]
    [SerializeField] protected TowerModel currentModel;
    [Tooltip("Parent for spawned level models. Defaults to the current model's parent, or this transform")]
    [SerializeField] protected Transform modelRoot;

    [Header("Level Up Transition")]
    [Tooltip("Seconds the old model spends charging up (glowing and swelling) before it is swapped")]
    [SerializeField] private float glowUpTime = 0.6f;
    [Tooltip("Seconds the new model takes to pop in and cool down from full glow")]
    [SerializeField] private float glowDownTime = 0.8f;
    [SerializeField, ColorUsage(false, true)] private Color glowColor = new Color(0.4f, 1.5f, 1.7f, 1f);
    [Tooltip("Emission strength at full glow. Above 1 catches bloom if the URP Volume has it")]
    [SerializeField] private float glowIntensity = 3f;
    [Tooltip("How much the old model swells while charging up (1 = no swell)")]
    [SerializeField] private float swellScale = 1.12f;
    [Tooltip("Scale the new model starts at before springing up to full size")]
    [SerializeField, Range(0.1f, 1f)] private float popInScale = 0.6f;
    [Tooltip("Played the moment the models swap. Leave empty for no particles")]
    [SerializeField] private TowerUpgradeBurst upgradeBurstPrefab;

    [Header("Hit Feedback")]
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float flashDuration = 0.25f;

    private Renderer[] _flashRenderers;
    private Color[] _flashBaseColors;
    private int[] _flashColorIds; // Which shader color property each renderer uses, -1 if none
    private MaterialPropertyBlock _flashBlock;
    private Coroutine _flashRoutine;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private EnemyBase _currentEnemy;
    protected EnemyBase CurrentEnemy => _currentEnemy;

    // Base stats from the inspector. Upgrades are always re-applied on top of these,
    // so recalculating any number of times gives the same result.
    private int _baseMaxHealth;
    private int _baseDamage;
    private float _baseRange;
    private float _baseFireRate;
    private float _baseProjectileSpeed;

    private int[] _trackLevels;
    private int _level = 1;
    private float _nextHitSoundTime;

    // Level visuals waiting to be shown. Buying several levels at once plays each transition in turn.
    private readonly Queue<TowerLevelVisual> _pendingVisuals = new Queue<TowerLevelVisual>();
    private Coroutine _transitionRoutine;
    private TowerGlow _activeGlow;
    private Transform _animatedModel;
    private Vector3 _animatedModelScale;

    public bool IsTransitioning => _transitionRoutine != null;

    // Fired after any upgrade is bought, so open UI can refresh
    public event Action<Tower> OnUpgraded;

    // Read-only info for the upgrade menu
    public TowerUpgradeData UpgradeData => upgradeData;
    public string DisplayName => upgradeData != null && !string.IsNullOrEmpty(upgradeData.towerName) ? upgradeData.towerName : name;
    public int Level => _level;
    public int MaxLevel => 1 + (upgradeData != null && upgradeData.levelVisuals != null ? upgradeData.levelVisuals.Length : 0);
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public int Damage => damage;
    public float Range => range;
    public float ShotsPerSecond => fireRate > 0f ? 1f / fireRate : 0f;
    public bool CanBeUpgraded => upgradeData != null && upgradeData.tracks != null && upgradeData.tracks.Length > 0;

    protected virtual void Awake()
    {
        if (modelRoot == null)
            modelRoot = currentModel != null ? currentModel.transform.parent : transform;

        if (currentModel != null)
            BindModel(currentModel);

        CacheBaseStats();
        _trackLevels = new int[CanBeUpgraded ? upgradeData.tracks.Length : 0];
        RecalculateStats();

        CacheFlashRenderers();
    }

    protected virtual void Start()
    {
        currentHealth = maxHealth;
        OnHealthChanged();

        StartCoroutine(CheckForEnemies());
        StartCoroutine(FollowTarget());
        StartCoroutine(Shoot());
    }

    // ---------------- Targeting & shooting ----------------

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

            _currentEnemy = closest.GetComponentInParent<EnemyBase>();
        }
    }

    // Moves barrel to aim at target
    private IEnumerator FollowTarget()
    {
        while (true)
        {
            if (_currentEnemy && turretYaw != null)
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

    // Fires on a cooldown. fireRate is read every loop, so fire rate upgrades apply straight away.
    private IEnumerator Shoot()
    {
        while (true)
        {
            if (_currentEnemy)
                Fire();

            yield return new WaitForSeconds(fireRate);
        }
    }

    // Spawns projectile and launches it at enemy
    protected virtual void Fire()
    {
        if (projectile == null || projectileSpawnPoint == null) return;

        Vector3 impactPos = PredictImpactPosition(_currentEnemy);

        GameObject newProjectile = Instantiate(projectile, projectileSpawnPoint.position, projectileSpawnPoint.rotation);

        TowerProjectile towerProjectile = newProjectile.GetComponent<TowerProjectile>();
        if (towerProjectile != null)
        {
            towerProjectile.damage = damage;
            ConfigureProjectile(towerProjectile);
        }

        Rigidbody projectileRb = newProjectile.GetComponent<Rigidbody>();
        Vector3 fireDirection = (impactPos - projectileSpawnPoint.position).normalized;
        projectileRb.linearVelocity = fireDirection * projectileSpeed;

        PlayFireSound();
    }

    // Child towers override this to play their own procedural shot sound. Silent by default.
    protected virtual void PlayFireSound() { }

    // Lets child towers add extra behaviour to a projectile (e.g. Bubble Gun splash)
    protected virtual void ConfigureProjectile(TowerProjectile towerProjectile) { }

    // Predicts enemy's future position
    private Vector3 PredictImpactPosition(EnemyBase enemy)
    {
        Vector3 predictedPos = enemy.transform.position;
        if (projectileSpawnPoint == null || projectileSpeed <= 0f) return predictedPos;

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

    // ---------------- Health ----------------

    public virtual bool TakeDamage(int damageAmount)
    {
        if (currentHealth == 0)
            return true;

        currentHealth = Mathf.Clamp(currentHealth - damageAmount, 0, maxHealth);
        OnHealthChanged();

        if (currentHealth == 0)
        {
            Die();
            return true;
        }

        if (damageAmount > 0)
        {
            Flash();
            PlayHitSound();
        }

        return false;
    }

    public void Heal(int healAmount)
    {
        currentHealth = Mathf.Clamp(currentHealth + healAmount, 0, maxHealth);
        OnHealthChanged();
    }

    protected virtual void Die()
    {
        if (SfxPlayer.Instance != null)
            SfxPlayer.Instance.PlayTowerDestroyed(transform.position);

        Destroy(gameObject);
    }

    // Hits are throttled so several enemies attacking at once don't stack into noise.
    // The main tower also sounds an alarm that's heard anywhere on the map.
    private void PlayHitSound()
    {
        if (SfxPlayer.Instance == null) return;

        if (this is MainTower)
            SfxPlayer.Instance.PlayMainTowerAlarm();

        if (Time.time < _nextHitSoundTime) return;
        _nextHitSoundTime = Time.time + 0.15f;

        SfxPlayer.Instance.PlayTowerHit(transform.position);
    }

    // Main tower uses this to update the HUD
    protected virtual void OnHealthChanged() { }

    // ---------------- Hit flash ----------------

    // Finds every mesh on the tower and remembers its original color.
    // Re-run after a level up, since the old model's renderers are destroyed.
    private void CacheFlashRenderers()
    {
        if (_flashBlock == null)
            _flashBlock = new MaterialPropertyBlock();

        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        System.Collections.Generic.List<Renderer> meshes = new System.Collections.Generic.List<Renderer>();

        // Only meshes: skips the lazer beam (LineRenderer) and particle effects
        foreach (Renderer r in all)
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                meshes.Add(r);
        }

        _flashRenderers = meshes.ToArray();
        _flashBaseColors = new Color[_flashRenderers.Length];
        _flashColorIds = new int[_flashRenderers.Length];

        for (int i = 0; i < _flashRenderers.Length; i++)
        {
            Material mat = _flashRenderers[i].sharedMaterial;

            if (mat != null && mat.HasProperty(BaseColorId))
            {
                _flashColorIds[i] = BaseColorId;
                _flashBaseColors[i] = mat.GetColor(BaseColorId);
            }
            else if (mat != null && mat.HasProperty(ColorId))
            {
                _flashColorIds[i] = ColorId;
                _flashBaseColors[i] = mat.GetColor(ColorId);
            }
            else
            {
                _flashColorIds[i] = -1; // Shader has no color property we can tint
            }
        }
    }

    // Restarts the red flash from full strength
    private void Flash()
    {
        if (_flashRenderers == null || _flashRenderers.Length == 0 || !isActiveAndEnabled) return;

        // The level-up glow owns the model's colour while it plays
        if (IsTransitioning) return;

        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);

        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        for (float t = 0f; t < flashDuration; t += Time.deltaTime)
        {
            SetTint(1f - t / flashDuration);
            yield return null;
        }

        ClearTint();
        _flashRoutine = null;
    }

    // amount: 0 = original color, 1 = fully hitColor
    private void SetTint(float amount)
    {
        for (int i = 0; i < _flashRenderers.Length; i++)
        {
            Renderer r = _flashRenderers[i];
            if (r == null || _flashColorIds[i] < 0) continue; // Destroyed by a model swap

            r.GetPropertyBlock(_flashBlock);
            _flashBlock.SetColor(_flashColorIds[i], Color.Lerp(_flashBaseColors[i], hitColor, amount));
            r.SetPropertyBlock(_flashBlock);
        }
    }

    // Removes the override so the materials go back to exactly how they were
    private void ClearTint()
    {
        foreach (Renderer r in _flashRenderers)
        {
            if (r != null) r.SetPropertyBlock(null);
        }
    }

    // ---------------- Upgrades ----------------

    public int GetTrackLevel(int trackIndex) => _trackLevels[trackIndex];

    public int TotalUpgrades
    {
        get
        {
            int total = 0;
            foreach (int level in _trackLevels) total += level;
            return total;
        }
    }

    public bool IsTrackMaxed(int trackIndex) =>
        _trackLevels[trackIndex] >= upgradeData.tracks[trackIndex].MaxLevel;

    // Costs for the next level of a track, or null if it's maxed
    public ResourceManager.ResourceCost[] GetNextCost(int trackIndex)
    {
        if (IsTrackMaxed(trackIndex)) return null;
        return upgradeData.tracks[trackIndex].levels[_trackLevels[trackIndex]].costs;
    }

    public bool TryUpgrade(int trackIndex)
    {
        if (!CanBeUpgraded || trackIndex < 0 || trackIndex >= _trackLevels.Length) return false;
        if (IsTrackMaxed(trackIndex)) return false;

        ResourceManager.ResourceCost[] costs = GetNextCost(trackIndex);
        if (costs != null)
        {
            if (!ResourceManager.Instance.HasEnoughResources(costs)) return false;
            ResourceManager.Instance.SpendResources(costs);
        }

        _trackLevels[trackIndex]++;

        int previousLevel = _level;
        RecalculateStats();
        UpdateLevel();

        // A bigger fanfare when the tower's model levels up
        if (SfxPlayer.Instance != null)
        {
            if (_level > previousLevel)
                SfxPlayer.Instance.PlayLevelUp();
            else
                SfxPlayer.Instance.PlayUpgrade(TotalUpgrades);
        }

        OnUpgraded?.Invoke(this);
        return true;
    }

    private void CacheBaseStats()
    {
        _baseMaxHealth = maxHealth;
        _baseDamage = damage;
        _baseRange = range;
        _baseFireRate = fireRate;
        _baseProjectileSpeed = projectileSpeed;

        CacheUniqueBaseStats();
    }

    // Puts every stat back to its inspector value, then applies each track's current level on top
    private void RecalculateStats()
    {
        int previousMaxHealth = maxHealth;

        maxHealth = _baseMaxHealth;
        damage = _baseDamage;
        range = _baseRange;
        fireRate = _baseFireRate;
        projectileSpeed = _baseProjectileSpeed;
        ResetUniqueStats();

        for (int i = 0; i < _trackLevels.Length; i++)
        {
            int level = _trackLevels[i];
            if (level <= 0) continue;

            UpgradeTrack track = upgradeData.tracks[i];
            ApplyUpgrade(track.stat, track.levels[level - 1].value);
        }

        // A health upgrade also heals by the amount gained
        int gained = maxHealth - previousMaxHealth;
        if (gained > 0)
            currentHealth += gained;

        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        OnHealthChanged();
    }

    // Common stats are handled here. Anything else is passed to the child tower.
    private void ApplyUpgrade(UpgradeStat stat, float value)
    {
        switch (stat)
        {
            case UpgradeStat.MaxHealth:
                maxHealth += Mathf.RoundToInt(value);
                break;

            case UpgradeStat.Damage:
                damage += Mathf.RoundToInt(value);
                break;

            case UpgradeStat.Range:
                range += value;
                break;

            case UpgradeStat.FireRate:
                // fireRate is seconds between shots, so 25% faster = divide by 1.25
                fireRate = _baseFireRate / (1f + value / 100f);
                break;

            case UpgradeStat.ProjectileSpeed:
                projectileSpeed += value;
                break;

            default:
                if (!ApplyUniqueUpgrade(stat, value))
                    Debug.LogWarning($"{name} has a '{stat}' upgrade but doesn't know how to use it", this);
                break;
        }
    }

    // Child towers override these for their own special stats
    protected virtual void CacheUniqueBaseStats() { }
    protected virtual void ResetUniqueStats() { }
    protected virtual bool ApplyUniqueUpgrade(UpgradeStat stat, float value) => false;

    // Works out the tower level from total upgrades bought and swaps visuals when it goes up
    private void UpdateLevel()
    {
        if (upgradeData.levelVisuals == null) return;

        int total = TotalUpgrades;

        // Level 1 is the prefab's own model, levelVisuals[0] is level 2, and so on
        while (_level - 1 < upgradeData.levelVisuals.Length &&
               total >= upgradeData.levelVisuals[_level - 1].upgradesRequired)
        {
            QueueLevelVisual(upgradeData.levelVisuals[_level - 1]);
            _level++;
        }
    }

    // ---------------- Level up transition ----------------

    // Model swaps play the glow transition. Anything that can't animate (no new model, or the tower is
    // inactive) is applied straight away, unless a transition is already running, so the order stays correct.
    private void QueueLevelVisual(TowerLevelVisual visual)
    {
        bool canAnimate = visual.modelPrefab != null && currentModel != null && isActiveAndEnabled;

        if (!canAnimate && !IsTransitioning)
        {
            ApplyLevelVisual(visual);
            return;
        }

        _pendingVisuals.Enqueue(visual);

        if (!IsTransitioning)
            _transitionRoutine = StartCoroutine(TransitionRoutine());
    }

    private IEnumerator TransitionRoutine()
    {
        // Clear any red hit flash so it doesn't fight the glow
        StopFlash();

        while (_pendingVisuals.Count > 0)
        {
            TowerLevelVisual visual = _pendingVisuals.Peek();

            // Projectile-only levels have nothing to animate
            if (visual.modelPrefab == null || currentModel == null)
            {
                ApplyLevelVisual(_pendingVisuals.Dequeue());
                continue;
            }

            // 1. Charge up: the old model glows brighter and swells, trembling harder near the end
            BeginGlow(currentModel);

            for (float t = 0f; t < glowUpTime; t += Time.deltaTime)
            {
                float k = t / glowUpTime;
                float eased = k * k; // Slow start, fast finish

                _activeGlow.SetAmount(eased);

                float tremble = Mathf.Sin(t * 45f) * 0.015f * eased;
                _animatedModel.localScale = _animatedModelScale * (Mathf.Lerp(1f, swellScale, eased) + tremble);

                yield return null;
            }

            // 2. Peak: particles go off and the models swap while hidden by the flash
            if (upgradeBurstPrefab != null)
                TowerUpgradeBurst.Spawn(upgradeBurstPrefab, GetModelBounds(currentModel), glowColor);

            EndGlow();
            ApplyLevelVisual(_pendingVisuals.Dequeue());

            // 3. Pop in: the new model starts small and fully lit, springs past full size, then cools down
            BeginGlow(currentModel);
            _activeGlow.SetAmount(1f);
            _animatedModel.localScale = _animatedModelScale * popInScale;

            for (float t = 0f; t < glowDownTime; t += Time.deltaTime)
            {
                float k = t / glowDownTime;

                float fade = 1f - k;
                _activeGlow.SetAmount(fade * fade); // Fast cool-down, long soft tail

                _animatedModel.localScale = _animatedModelScale * Mathf.LerpUnclamped(popInScale, 1f, EaseOutBack(k));

                yield return null;
            }

            EndGlow();
        }

        _transitionRoutine = null;
    }

    private void BeginGlow(TowerModel model)
    {
        _animatedModel = model.transform;
        _animatedModelScale = _animatedModel.localScale;
        _activeGlow = TowerGlow.Apply(model.gameObject, glowColor, glowIntensity);
    }

    // Original materials and scale go back exactly as they were
    private void EndGlow()
    {
        if (_activeGlow != null)
        {
            _activeGlow.Remove();
            _activeGlow = null;
        }

        if (_animatedModel != null)
            _animatedModel.localScale = _animatedModelScale;

        _animatedModel = null;
    }

    // Overshoots past 1 and settles back, like a spring
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    // World-space box around the model's meshes, used to size the upgrade burst
    private Bounds GetModelBounds(TowerModel model)
    {
        Bounds bounds = new Bounds(model.transform.position, Vector3.zero);
        bool found = false;

        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return bounds;
    }

    private void StopFlash()
    {
        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
            _flashRoutine = null;
        }

        if (_flashRenderers != null)
            ClearTint();
    }

    private void ApplyLevelVisual(TowerLevelVisual visual)
    {
        bool modelChanged = false;
        bool projectileChanged = false;

        if (visual.modelPrefab != null)
        {
            if (currentModel == null)
            {
                Debug.LogWarning($"{name} can't swap models: assign its Level 1 model (with a TowerModel component) to 'Current Model'", this);
            }
            else
            {
                // Keep the turret facing the same way so it doesn't snap
                Quaternion yawRotation = turretYaw != null ? turretYaw.rotation : Quaternion.identity;
                bool hadYaw = turretYaw != null;

                OnModelSwapping();

                // Destroy only happens at the end of the frame, so detach the old model first
                // or its renderers would still be found when the flash renderers are re-cached
                currentModel.transform.SetParent(null, true);
                Destroy(currentModel.gameObject);

                currentModel = Instantiate(visual.modelPrefab, modelRoot, false);
                BindModel(currentModel);

                if (hadYaw && turretYaw != null)
                    turretYaw.rotation = yawRotation;

                if (_flashRoutine != null)
                {
                    StopCoroutine(_flashRoutine);
                    _flashRoutine = null;
                }

                CacheFlashRenderers();
                modelChanged = true;
            }
        }

        if (visual.projectilePrefab != null && visual.projectilePrefab != projectile)
        {
            projectile = visual.projectilePrefab;
            projectileChanged = true;
        }

        if (modelChanged || projectileChanged)
            OnVisualsChanged(modelChanged, projectileChanged);
    }

    private void BindModel(TowerModel model)
    {
        if (model.turretYaw != null) turretYaw = model.turretYaw;
        if (model.projectileSpawnPoint != null) projectileSpawnPoint = model.projectileSpawnPoint;

        barrel = model.barrel;
        useBarrel = model.useBarrel && model.barrel != null;
    }

    // Called just before the old model is destroyed (Harpoon uses it to rescue its harpoon)
    protected virtual void OnModelSwapping() { }

    // Called after a level up swapped the model and/or projectile
    protected virtual void OnVisualsChanged(bool modelChanged, bool projectileChanged) { }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}