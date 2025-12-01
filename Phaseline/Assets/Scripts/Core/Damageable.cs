using UnityEngine;

[DisallowMultipleComponent]
public class Damageable : MonoBehaviour
{
    public bool IsDead { get; private set; }
    
    // New: Check for external invulnerability sources
    public bool IsInvulnerable { get; set; } 

    public void Die()
    {
        if (IsDead || IsInvulnerable) return; // Shield check
        
        IsDead = true;
        SpawnManager.Instance?.HandleDeath(this);
    }

    public void Revive()
    {
        IsDead = false;
        IsInvulnerable = false;
    }

    public void TakeDamage(int amount)
    {
        if (amount > 0) Die();
    }
}