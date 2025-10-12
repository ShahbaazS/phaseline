using UnityEngine;

[DisallowMultipleComponent]
public class Damageable : MonoBehaviour
{
    public bool IsDead { get; private set; }

    public void Die()
    {
        if (IsDead) return;
        IsDead = true;
        // Delegate respawn to the spawn manager
        SpawnManager.Instance?.HandleDeath(this);
    }

    public void Revive()
    {
        IsDead = false;
    }
}
