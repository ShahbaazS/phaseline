using FishNet.Object;
using UnityEngine;

public class TrailSegment : NetworkBehaviour
{
    [Tooltip("How long this segment stays active")]
    public float lifetime = 5f;

    public override void OnStartServer()
    {
        base.OnStartServer();
        // Destroy self after lifetime (Server handles destruction)
        Invoke(nameof(Despawn), lifetime);
    }

    void Despawn()
    {
        if (base.IsServer) base.Despawn();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!base.IsServer) return;

        // Ensure we don't kill the person who laid the trail (if overlapping initially)
        // or check collision logic layers.
        
        if (other.TryGetComponent<Damageable>(out var hp))
        {
            // Optional: check ownership via base.OwnerId vs other NetworkObject
            // if (base.OwnerId == other.GetComponent<NetworkObject>().OwnerId) return;
            
            hp.TakeDamage(100);
        }
    }
}