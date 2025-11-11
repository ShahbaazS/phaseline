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
        root.position = exit.position;
        if (keepForward) root.rotation = exit.rotation;

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
