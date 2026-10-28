using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class Lazer : Tower
{
    [Header("Lazer Settings")]
    [SerializeField] private LineRenderer beam;
    [SerializeField] private Vector3 targetOffset = Vector3.up * 0.5f; // Aim at the enemy's middle instead of its pivot

    [Header("Overcharge (unlocked by upgrades)")]
    [Tooltip("Seconds locked onto one enemy to reach full overcharge")]
    [SerializeField] private float overchargeRampTime = 3f;
    [Tooltip("Beam width multiplier at full overcharge, so the player can see it building up")]
    [SerializeField] private float overchargeWidthScale = 2f;

    private float _maxOvercharge;       // 0.5 = +50% damage at full charge. 0 until upgraded
    private float _chargeTime;          // How long the beam has been on the current enemy
    private EnemyBase _chargedEnemy;
    private float _baseStartWidth;
    private float _baseEndWidth;

    protected override void Awake()
    {
        base.Awake();

        // A level 1 TowerModel can bring its own beam, otherwise use the one on this object
        if (currentModel != null && currentModel.beam != null)
            beam = currentModel.beam;

        if (beam == null)
            beam = GetComponent<LineRenderer>();

        SetupBeam(beam);
    }

    private void SetupBeam(LineRenderer lineRenderer)
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.enabled = false;

        _baseStartWidth = lineRenderer.startWidth;
        _baseEndWidth = lineRenderer.endWidth;
    }

    // ---------------- Upgrades ----------------

    protected override void ResetUniqueStats()
    {
        _maxOvercharge = 0f;
    }

    protected override bool ApplyUniqueUpgrade(UpgradeStat stat, float value)
    {
        if (stat != UpgradeStat.LazerOvercharge) return false;

        _maxOvercharge = value / 100f;
        return true;
    }

    // A new level model can bring a new beam (that's the lazer's "projectile")
    protected override void OnVisualsChanged(bool modelChanged, bool projectileChanged)
    {
        if (!modelChanged || currentModel.beam == null) return;

        // The root beam would otherwise stay drawn with its last positions
        if (beam != null) beam.enabled = false;

        beam = currentModel.beam;
        SetupBeam(beam);
    }

    // ---------------- Beam ----------------

    // 0 to 1, how charged the beam currently is
    private float ChargePercent => overchargeRampTime > 0f ? Mathf.Clamp01(_chargeTime / overchargeRampTime) : 1f;

    // Damage tick: called every fireRate seconds while a target exists
    protected override void Fire()
    {
        if (CurrentEnemy == null) return;

        float multiplier = 1f + _maxOvercharge * ChargePercent;
        CurrentEnemy.TakeDamage(Mathf.RoundToInt(damage * multiplier));
    }

    // The beam is drawn every frame so it stays locked on
    private void LateUpdate()
    {
        EnemyBase target = CurrentEnemy;

        // Charge builds while the beam stays on the same enemy and resets when it switches
        if (target != _chargedEnemy)
        {
            _chargedEnemy = target;
            _chargeTime = 0f;
        }
        else if (target != null)
        {
            _chargeTime += Time.deltaTime;
        }

        if (target == null)
        {
            if (beam.enabled) beam.enabled = false;
            return;
        }

        if (!beam.enabled) beam.enabled = true;

        beam.SetPosition(0, projectileSpawnPoint.position);
        beam.SetPosition(1, target.transform.position + targetOffset);

        // Visibly thicken the beam as it overcharges (only once the upgrade is bought)
        float widthScale = _maxOvercharge > 0f ? Mathf.Lerp(1f, overchargeWidthScale, ChargePercent) : 1f;
        beam.startWidth = _baseStartWidth * widthScale;
        beam.endWidth = _baseEndWidth * widthScale;
    }

    private void OnDisable()
    {
        if (beam != null) beam.enabled = false;
    }
}
