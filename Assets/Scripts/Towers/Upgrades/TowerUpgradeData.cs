using System;
using UnityEngine;

// Which stat an upgrade track changes. Common stats are handled in Tower, the rest by the tower that owns them.
public enum UpgradeStat
{
    MaxHealth,          // +value max health (current health goes up by the same amount)
    Damage,             // +value damage
    Range,              // +value metres
    FireRate,           // +value % faster (25 = shoots 25% faster)
    ProjectileSpeed,    // +value speed

    HarpoonTargets,     // Harpoon: +value enemies hit per throw
    HarpoonChainRange,  // Harpoon: +value metres the harpoon can jump between enemies
    BubbleSplash,       // Bubble Gun: bubbles burst for splash damage in a radius of value metres
    LazerOvercharge     // Lazer: damage ramps up to +value % while locked onto the same enemy
}

[Serializable]
public class UpgradeLevel
{
    [Tooltip("TOTAL bonus at this level (not the step). e.g. Damage levels 5, 12, 20")]
    public float value;
    public ResourceManager.ResourceCost[] costs;
}

[Serializable]
public class UpgradeTrack
{
    public string displayName;
    [TextArea] public string description;
    public UpgradeStat stat;
    [Tooltip("One entry per level. 3 levels = 3 entries")]
    public UpgradeLevel[] levels = new UpgradeLevel[3];

    public int MaxLevel => levels == null ? 0 : levels.Length;

    // Human readable bonus, e.g. "+25% fire rate"
    public string FormatValue(float value)
    {
        switch (stat)
        {
            case UpgradeStat.MaxHealth:         return $"+{value:0} health";
            case UpgradeStat.Damage:            return $"+{value:0} damage";
            case UpgradeStat.Range:             return $"+{value:0.#}m range";
            case UpgradeStat.FireRate:          return $"+{value:0}% fire rate";
            case UpgradeStat.ProjectileSpeed:   return $"+{value:0} projectile speed";
            case UpgradeStat.HarpoonTargets:    return $"+{value:0} enemies per throw";
            case UpgradeStat.HarpoonChainRange: return $"+{value:0.#}m chain range";
            case UpgradeStat.BubbleSplash:      return $"{value:0.#}m splash";
            case UpgradeStat.LazerOvercharge:   return $"+{value:0}% max overcharge";
            default:                            return value.ToString("0.##");
        }
    }
}

// Visuals for one tower level. Level 1 is always the model already inside the tower prefab.
[Serializable]
public class TowerLevelVisual
{
    [Tooltip("Total upgrades bought (across all tracks) needed to reach this level")]
    public int upgradesRequired = 3;
    [Tooltip("Model prefab with a TowerModel component on its root. Leave empty to keep the current model")]
    public TowerModel modelPrefab;
    [Tooltip("Projectile used from this level. Leave empty to keep the current one")]
    public GameObject projectilePrefab;
}

[CreateAssetMenu(menuName = "Scriptable Objects/Tower Upgrade Data", fileName = "NewTowerUpgradeData")]
public class TowerUpgradeData : ScriptableObject
{
    public string towerName;

    [Header("Upgrade Tracks")]
    public UpgradeTrack[] tracks;

    [Header("Levels 2+ (Level 1 is the prefab's own model)")]
    public TowerLevelVisual[] levelVisuals;
}
