using UnityEngine;

public class BubbleGun : Tower
{
    [Header("Bubble Burst (unlocked by upgrades)")]
    [Tooltip("Splash damage dealt to nearby enemies, as a % of the bubble's damage")]
    [Range(0f, 100f)] [SerializeField] private float splashDamagePercent = 50f;
    [Tooltip("Optional effect spawned where a bubble bursts")]
    [SerializeField] private GameObject burstEffect;

    private float _splashRadius; // 0 until the Bubble Burst upgrade is bought

    protected override void ResetUniqueStats()
    {
        _splashRadius = 0f;
    }

    protected override bool ApplyUniqueUpgrade(UpgradeStat stat, float value)
    {
        if (stat != UpgradeStat.BubbleSplash) return false;

        _splashRadius = value;
        return true;
    }

    protected override void ConfigureProjectile(TowerProjectile towerProjectile)
    {
        if (_splashRadius <= 0f) return;

        towerProjectile.splashRadius = _splashRadius;
        towerProjectile.splashDamage = Mathf.Max(1, Mathf.RoundToInt(damage * splashDamagePercent / 100f));
        towerProjectile.splashMask = enemyMask;
        towerProjectile.splashEffect = burstEffect;
    }
}
