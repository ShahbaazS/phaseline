using UnityEngine;
using UnityEngine.AI;

public class Portal : MonoBehaviour
{
    public Transform exit;
    public bool keepForward = true;

    void OnTriggerEnter(Collider other)
    {
        if (!exit) return;
        var root = other.attachedRigidbody ? other.attachedRigidbody.transform : other.transform;
        if (!root || !root.CompareTag("Player")) return;

        // 1) Pause trail so we don't draw a mega segment across space
        if (root.TryGetComponent<TrailMesh>(out var trail)) trail.PauseTrail();

        // 2) Teleport
        // 4.6 API: Teleport via the PredictionRigidbody wrapper
        // This informs the prediction system to "reset" history from this new point
        var pr = other.GetComponent<FishNet.Object.Prediction.PredictionRigidbody>();
        
        if (pr != null)
        {
            // Teleport sets position and clears velocity history to prevent "ghost" momentum
            pr.Teleport(exit.position, exit.rotation); 
        }

        // 3) Resume trail at the exit (starts clean at new position)
        if (trail) trail.ResumeTrailAt(exit.position);

        // 4) Sync NavMesh agent for bots
        if (root.TryGetComponent<NavMeshAgent>(out var ag))
        {
            ag.Warp(root.position);
            // re-issue destination if using BotPathfinder
            var pf = root.GetComponent<BotPathfinder>();
            if (pf && pf.target) ag.SetDestination(pf.target.position);
        }
    }
}
