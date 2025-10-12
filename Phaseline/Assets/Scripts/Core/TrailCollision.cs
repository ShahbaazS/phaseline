using UnityEngine;

public class TrailCollision : MonoBehaviour
{
    // Set by the trail builder when spawning the segment
    public Transform ownerRoot;

    void OnTriggerEnter(Collider other)
    {
        // Find the rider root (prioritize rigidbody root)
        var root = other.attachedRigidbody ? other.attachedRigidbody.transform : other.transform;

        // Ignore the trail owner
        if (ownerRoot && (root == ownerRoot || root.IsChildOf(ownerRoot))) return;

        // Ignore if spawn-protected
        if (root.TryGetComponent<SpawnProtection>(out var prot) && prot.IsProtected) return;

        // Kill if damageable
        if (root.TryGetComponent<Damageable>(out var dmg))
            dmg.Die();
    }
}
