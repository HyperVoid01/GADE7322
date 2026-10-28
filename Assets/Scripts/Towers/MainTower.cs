using UnityEngine;

// Central tower. Losing it ends the game, and its health is shown on the HUD.
public class MainTower : Tower
{
    // Health upgrades raise max health, so the slider is refreshed on every change
    protected override void OnHealthChanged()
    {
        if (HUDManager.Instance != null)
            HUDManager.Instance.UpdateHealthSlider(currentHealth, maxHealth);
    }

    protected override void Die()
    {
        GameManager.Instance.EndGame();
    }
}
