public interface ITargetable
{
    // Returns true if enemy is dead
    public bool TakeDamage(int damage);

    public void Heal(int healAmount);
}
