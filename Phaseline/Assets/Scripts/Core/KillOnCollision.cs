using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class KillOnCollision : MonoBehaviour
{
    [Header("What kills us")]
    public LayerMask wallMask;          // include Wall layer
    public LayerMask bikeMask;          // include Bike layer
    public float minRelativeSpeed = 0f; // set >0 if you only want kills at speed (e.g., 2.5f)

    private Damageable dmg;
    private SpawnProtection sp;

    void Awake()
    {
        dmg = GetComponent<Damageable>();
        if (!dmg) dmg = gameObject.AddComponent<Damageable>();
        sp  = GetComponent<SpawnProtection>();
    }

    void OnCollisionEnter(Collision c)
    {
        if (sp && sp.IsProtected) return;

        // Wall?
        if (IsInMask(c.collider.gameObject.layer, wallMask))
        {
            if (PassesSpeedGate(c)) dmg.Die();
            return;
        }

        // Other bike? (kill both by default)
        if (IsInMask(c.collider.gameObject.layer, bikeMask))
        {
            if (PassesSpeedGate(c))
            {
                dmg.Die();

                // Optionally kill the other rider too
                var otherRoot = c.collider.attachedRigidbody ? c.collider.attachedRigidbody.transform : c.collider.transform;
                if (otherRoot && otherRoot.TryGetComponent<SpawnProtection>(out var osp) && osp.IsProtected) return;
                if (otherRoot && otherRoot.TryGetComponent<Damageable>(out var odmg)) odmg.Die();
            }
        }
    }

    bool PassesSpeedGate(Collision c)
    {
        if (minRelativeSpeed <= 0f) return true;
        return c.relativeVelocity.magnitude >= minRelativeSpeed;
    }

    static bool IsInMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;
}
