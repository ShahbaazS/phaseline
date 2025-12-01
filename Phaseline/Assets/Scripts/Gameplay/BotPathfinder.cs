using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(BikeController))]
[RequireComponent(typeof(NavMeshAgent))]
public class BotPathfinder : MonoBehaviour
{
    [Header("Goal")]
    public Transform target;
    Transform transientTarget;
    public float replanEvery = 0.25f;

    [Header("Steer")]
    public float turnGain = 1.4f;     
    public bool drawPathGizmos = true;
    public bool verboseLogs = false;   

    [Header("Path Tracking Tuning")]
    public float steerGainBase = 1.2f;
    public float steerGainPerSpeed = 0.10f;
    public float brakeAngleDeg = 35f;
    public float driftAngleDeg = 55f;
    public float steerClamp = 1.0f;
    public float arriveDist = 1.0f; 

    BikeController bike;
    NavMeshAgent agent;
    float replanT;
    Vector3 surfaceUp = Vector3.up;

    void Awake()
    {
        bike = GetComponent<BikeController>();
        agent = GetComponent<NavMeshAgent>();

        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.autoTraverseOffMeshLink = false;

        int portalArea = NavMesh.GetAreaFromName("Portal");
        if (portalArea >= 0) agent.SetAreaCost(portalArea, 0.1f);
    }

    void OnEnable()
    {
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
        agent.nextPosition = transform.position;
        surfaceUp = TryGetUp();

        replanT -= Time.fixedDeltaTime;
        if (replanT <= 0f && target)
        {
            agent.SetDestination(target.position);
            replanT = replanEvery;
        }

        if (agent.isOnOffMeshLink)
        {
            TraverseLink();
            return;
        }

        if (!target)
        {
            bike.Simulate(new Vector2(0f, 1f), false, false, false, Time.fixedDeltaTime);
            return;
        }

        if (!agent.isOnNavMesh)
        {
            WarpToNavmesh(transform.position);
        }

        if (!agent.hasPath || agent.pathPending)
        {
            bike.Simulate(new Vector2(0f, 1f), false, false, false, Time.fixedDeltaTime);
            return;
        }

        Vector3 up = surfaceUp;
        Vector3 corner = agent.steeringTarget;
        Vector3 to = corner - transform.position;
        to = Vector3.ProjectOnPlane(to, up);

        if (to.sqrMagnitude < arriveDist * arriveDist)
        {
            bike.Simulate(new Vector2(0f, 1f), false, false, false, Time.fixedDeltaTime);
            return;
        }

        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        Vector3 dir = to.normalized;

        float alphaRad = Mathf.Atan2(
            Vector3.Dot(up, Vector3.Cross(fwd, dir)), 
            Mathf.Clamp(Vector3.Dot(fwd, dir), -1f, 1f)
        );
        float alphaDeg = alphaRad * Mathf.Rad2Deg;

        float speed = 0f;
        if (TryGetComponent<Rigidbody>(out var rb)) speed = rb.linearVelocity.magnitude;
        float gain = steerGainBase + steerGainPerSpeed * speed;

        float steerCmd = Mathf.Clamp(alphaRad * gain, -steerClamp, steerClamp);
        bool shouldDrift = Mathf.Abs(alphaDeg) >= driftAngleDeg;
        float throttle = 1f;
        if (Mathf.Abs(alphaDeg) >= brakeAngleDeg)
        {
            throttle = Mathf.Lerp(0.65f, 1f, Mathf.InverseLerp(180f, brakeAngleDeg, Mathf.Abs(alphaDeg)));
        }

        bike.Simulate(new Vector2(steerCmd, throttle), shouldDrift, false, false, Time.fixedDeltaTime);
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

        // CHANGED: Use NetworkTrailMesh
        if (TryGetComponent<NetworkTrailMesh>(out var trail)) trail.NotifyTeleportStart();

        Vector3 end = data.endPos; end.y = transform.position.y;
        transform.position = end;

        // CHANGED: Use NetworkTrailMesh
        if (trail) trail.NotifyTeleportEnd(end);

        agent.CompleteOffMeshLink();
        agent.Warp(transform.position);
        if (target) agent.SetDestination(target.position);
    }

    void WarpToNavmesh(Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 5f, NavMesh.AllAreas))
            agent.Warp(hit.position);
        else
            agent.Warp(pos);
    }

    Vector3 TryGetUp()
    {
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            return hit.normal;
        return Vector3.up;
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