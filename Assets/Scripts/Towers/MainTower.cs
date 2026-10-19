using UnityEngine;

// Central tower
public class MainTower : Tower
{
    public override bool TakeDamage(int damageAmount)
    {
        if (currentHealth == 0)
            return true;
        
        currentHealth = Mathf.Clamp(currentHealth - damageAmount, 0, maxHealth);
        HUDManager.Instance.UpdateHealthSlider(currentHealth, maxHealth);

        if (currentHealth == 0)
        {
            GameManager.Instance.EndGame();
            return true;
        }
        
        return false;
    }
}
