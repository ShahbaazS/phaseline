using FishNet;
using UnityEngine;
using UnityEngine.AI;

public class Portal : MonoBehaviour
{
    public Transform exitPoint;

    private void OnTriggerEnter(Collider other)
    {
        if (!InstanceFinder.IsServerStarted) return;

        // CASE 1: Player (Prediction)
        if (other.TryGetComponent(out NetworkBike biker))
        {
            biker.Teleport(exitPoint.position, exitPoint.rotation);
        }
        // CASE 2: Bot (NavMesh)
        else if (other.TryGetComponent(out NavMeshAgent agent))
        {
            // 1. Clear Trail
            if (other.TryGetComponent(out NetworkTrailMesh trail))
            {
                trail.ObserverResetTrail();
            }

            // 2. Warp NavMesh (Critical! Do not just set transform.position)
            agent.Warp(exitPoint.position);
            
            // 3. Sync Rotation
            other.transform.rotation = exitPoint.rotation;
            
            // Note: NetworkTransform will automatically detect this large jump 
            // and teleport the visuals on clients if "Enable Teleport" is checked.
        }
    }
}