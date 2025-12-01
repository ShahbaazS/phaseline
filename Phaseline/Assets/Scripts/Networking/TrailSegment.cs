using FishNet.Object;
using UnityEngine;

public class TrailSegment : NetworkBehaviour
{
    [Tooltip("If true, you cannot die by hitting your own trail.")]
    public bool ignoreSelf = false;

    private void OnTriggerEnter(Collider other)
    {
        // Server only logic
        if (!base.IsServerInitialized) return;

        // 1. Look for Damageable on the object or its parents (Safer)
        var hp = other.GetComponentInParent<Damageable>();
        
        if (hp != null)
        {
            // 2. Check Ownership (Prevent self-kill ONLY if ignoreSelf is true)
            if (ignoreSelf)
            {
                var otherNetObj = other.GetComponentInParent<NetworkObject>();
                // If both are networked and have the same owner, ignore the collision
                if (otherNetObj != null && base.OwnerId == otherNetObj.OwnerId) 
                {
                    return;
                }
            }
            
            // 3. Deal Damage
            Debug.Log($"[TrailSegment] Hit {other.name}, causing death.");
            hp.TakeDamage(100);
        }
    }
}