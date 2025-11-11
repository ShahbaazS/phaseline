using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(BikeController))]
[RequireComponent(typeof(NavMeshAgent))]
public class BotPathfinder : MonoBehaviour
{
    [Header("Goal")]
    public Transform target;
    Transform transientTarget;
    [Tooltip("Repath interval (seconds).")]
    public float replanEvery = 0.25f;

    [Header("Steer")]
    public float turnGain = 1.4f;     // increase if under-steering
    public bool drawPathGizmos = true;
    public bool verboseLogs = false;   // turn on to debug

    [Header("Path Tracking Tuning")]
    [Tooltip("Base steering gain. Raise if the bot under-steers.")]
    public float steerGainBase = 1.2f;

    [Tooltip("Extra steering gain per unit of speed (linearVelocity magnitude).")]
    public float steerGainPerSpeed = 0.10f;

    [Tooltip("Degrees: when heading error exceeds this, start braking a bit.")]
    public float brakeAngleDeg = 35f;

    [Tooltip("Degrees: when heading error exceeds this, auto-enable drift to carve.")]
    public float driftAngleDeg = 55f;

    [Tooltip("Clamp on final steer command (0..1).")]
    public float steerClamp = 1.0f;

    [Tooltip("Reduce lookahead to pull harder to corner. Try 0.8–1.2m.")]
    public float arriveDist = 1.0f; // (override your old if larger)

    BikeController bike;
    NavMeshAgent agent;
    float replanT;

    // optional: remember last valid up to project onto ramps
    Vector3 surfaceUp = Vector3.up;

    void Awake()
    {
        bike = GetComponent<BikeController>();
        agent = GetComponent<NavMeshAgent>();

        // We use agent for planning only; physics drives the bike
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.autoTraverseOffMeshLink = false;

        // Make “Portal” cheap if present
        int portalArea = NavMesh.GetAreaFromName("Portal");
        if (portalArea >= 0) agent.SetAreaCost(portalArea, 0.1f);
    }

    void OnEnable()
    {
        // Snap the agent onto the NavMesh
        WarpToNavmesh(transform.position);
        if (target) agent.SetDestination(target.position);
        replanT = 0f;
    }

    public void SetTarget(Transform t)
    {
        target = t;
        if (target)
        {
            if (!agent.Warp(transform.position)) WarpToNavmesh(transform.position);
            agent.SetDestination(target.position);
        }
    }

    void FixedUpdate()
    {
        // keep agent in sync with our real position
        agent.nextPosition = transform.position;

        // keep a slope-aware up
        surfaceUp = TryGetUp();

        // replan to moving targets
        replanT -= Time.fixedDeltaTime;
        if (replanT <= 0f && target)
        {
            agent.SetDestination(target.position);
            replanT = replanEvery;
        }

        // OFF-MESH LINK (portal) traversal
        if (agent.isOnOffMeshLink)
        {
            TraverseLink();
            return;
        }

        // No target? Just roll forward.
        if (!target)
        {
            bike.Move(new Vector2(0f, 1f), false, false, false);
            if (verboseLogs) Debug.Log($"[PF] No target on {name}");
            return;
        }

        // Ensure we’re actually on the navmesh; if not, try to snap
        if (!agent.isOnNavMesh)
        {
            if (verboseLogs) Debug.LogWarning($"[PF] Agent not on NavMesh: {name} — attempting warp.");
            WarpToNavmesh(transform.position);
        }

        if (!agent.hasPath || agent.pathPending)
        {
            // No path yet, go straight
            bike.Move(new Vector2(0f, 1f), false, false, false);
            if (verboseLogs) Debug.Log($"[PF] PathPending/NoPath {name}: pending={agent.pathPending} hasPath={agent.hasPath}");
            return;
        }

        // --- Pure-Pursuit style steering toward agent.steeringTarget ---
        Vector3 up = surfaceUp; // from your TryGetUp()
        Vector3 corner = agent.steeringTarget;

        // Desired direction projected into the surface plane
        Vector3 to = corner - transform.position;
        to = Vector3.ProjectOnPlane(to, up);
        if (to.sqrMagnitude < arriveDist * arriveDist)
        {
            // Close enough to this corner; keep moving forward until next tick
            bike.Move(new Vector2(0f, 1f), false, false, false);
            if (verboseLogs) Debug.Log($"[PF] corner reached for {name}");
            return;
        }

        // Current forward in the same plane
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        Vector3 dir = to.normalized;

        // Signed heading error around the surface normal
        float alphaRad = Mathf.Atan2(
            Vector3.Dot(up, Vector3.Cross(fwd, dir)), // signed
            Mathf.Clamp(Vector3.Dot(fwd, dir), -1f, 1f)
        );
        float alphaDeg = alphaRad * Mathf.Rad2Deg;

        // Speed-aware steering gain
        float speed = 0f;
        if (TryGetComponent<Rigidbody>(out var rb)) speed = rb.linearVelocity.magnitude;
        float gain = steerGainBase + steerGainPerSpeed * speed;

        // Map heading error to steer input
        float steerCmd = Mathf.Clamp(alphaRad * gain, -steerClamp, steerClamp);

        // Corner braking & auto-drift
        bool shouldDrift = Mathf.Abs(alphaDeg) >= driftAngleDeg;
        float throttle = 1f;
        if (Mathf.Abs(alphaDeg) >= brakeAngleDeg)
        {
            // ease throttle to help the turn; tweak 0.6–0.85
            throttle = Mathf.Lerp(0.65f, 1f, Mathf.InverseLerp(180f, brakeAngleDeg, Mathf.Abs(alphaDeg)));
        }

        // Finally drive (throttle always forward; steer left/right; drift when sharp)
        bike.Move(new Vector2(steerCmd, throttle), shouldDrift, false, false);

        if (verboseLogs)
        {
            Debug.Log($"[PF] steer={steerCmd:F2} α={alphaDeg:F1}° spd={speed:F1} drift={shouldDrift} thr={throttle:F2} tgt={target?.name}");
        }

    }

    public void SetTargetPosition(Vector3 worldPos)
    {
        if (transientTarget == null)
        {
            var go = new GameObject($"{name}_PF_Waypoint");
            go.hideFlags = HideFlags.HideInHierarchy;
            transientTarget = go.transform;
        }
        transientTarget.position = worldPos;
        SetTarget(transientTarget);
    }


    void TraverseLink()
    {
        var data = agent.currentOffMeshLinkData;

        // pause trail emission (we don't want a mega segment across space)
        if (TryGetComponent<TrailMesh>(out var trail)) trail.PauseTrail();

        // Teleport to link end (keep your ground height, or use endPos.y if baked heights are correct)
        Vector3 end = data.endPos; end.y = transform.position.y;
        transform.position = end;

        // resume trail at exit
        if (trail) trail.ResumeTrailAt(end);

        agent.CompleteOffMeshLink();
        agent.Warp(transform.position);
        if (target) agent.SetDestination(target.position);

        if (verboseLogs) Debug.Log($"[PF] {name} traversed link → {end}");
    }

    // ——— helpers ———

    void WarpToNavmesh(Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 5f, NavMesh.AllAreas))
            agent.Warp(hit.position);
        else
            agent.Warp(pos); // at least keep in sync; baking likely missing
    }

    Vector3 TryGetUp()
    {
        // Prefer your BikeController’s surface up if you expose it
        // If you didn’t yet, this raycast fallback still works on ramps.
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            return hit.normal;
        return Vector3.up;
    }

    static float ToLocalX(Vector3 dir, Vector3 fwd, Vector3 up)
    {
        if (dir.sqrMagnitude < 1e-6f) return 0f;
        return (Quaternion.Inverse(Quaternion.LookRotation(fwd, up)) * dir).x;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawPathGizmos || agent == null || !agent.hasPath) return;
        var cs = agent.path.corners;
        Gizmos.color = Color.magenta;
        for (int i = 0; i < cs.Length - 1; i++) Gizmos.DrawLine(cs[i], cs[i + 1]);
        Gizmos.DrawSphere(agent.steeringTarget, 0.15f);
    }
}
