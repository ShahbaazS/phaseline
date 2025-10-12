using UnityEngine;

[RequireComponent(typeof(BikeController))]
[RequireComponent(typeof(Rigidbody))]
public class BotController : MonoBehaviour
{
    // -------- FSM --------
    public enum BotState { Scout, Evade, Cutoff, Emergency }
    public BotState State { get; private set; } = BotState.Scout;

    // -------- Sensing --------
    [Header("Sensing (set Wall + Trail)")]
    public LayerMask obstacleMask;
    public float forwardProbe = 22f;
    public float sideProbe    = 14f;
    public float diagProbe    = 18f;
    public float hullRadius = 0.4f;
    
    [Header("Emergency Drift")]
    public bool driftDuringEmergency = true;     // master toggle
    [Tooltip("Keep drift engaged for a moment after Emergency to finish the carve.")]
    public float emergencyDriftHangTime = 0.20f; // seconds

    float emergencyDriftT; // internal countdown

    [Header("Decision")]
    [Range(0f,1f)] public float steerAggression = 1.0f;
    public float redecideTime = 0.15f;
    public float dangerDist   = 6.0f;

    [Header("Opponents (optional)")]
    public Transform[] opponents;
    public float cutoffProbe = 22f;

    // -------- Gizmos --------
    [Header("Gizmos")]
    public bool drawGizmosAlways = true;   // show in Game view too (toggle Gizmos in Game view)
    public bool drawDiagonals    = true;
    public bool drawNormals      = false;

    // cached refs/state
    BikeController bike;
    Rigidbody rb;
    Transform tr;
    float thinkT, lastTurnSign;
    int seededBias; // -1/+1 used when distances tie

    // last probe data for gizmos
    float lastF, lastL, lastR, lastDL, lastDR;
    Vector3 fOrigin, fDir, fHitPoint, fHitNormal;
    Vector3 lOrigin, lDir, lHitPoint;
    Vector3 rOrigin, rDir, rHitPoint;
    Vector3 dlOrigin, dlDir, dlHitPoint;
    Vector3 drOrigin, drDir, drHitPoint;

    void Awake()
    {
        bike = GetComponent<BikeController>();
        rb   = GetComponent<Rigidbody>();
        tr   = transform;
        seededBias = Random.value < 0.5f ? -1 : +1;
    }

    void FixedUpdate()
    {
        thinkT -= Time.fixedDeltaTime;
        if (thinkT <= 0f)
        {
            Decide();
            thinkT = redecideTime;
        }
    }

    // -------------------- DECIDE --------------------
    void Decide()
    {
        // Measure once so we can use and draw consistent values
        lastF = SpaceForward(out var fHit, out fOrigin, out fDir, out fHitPoint, out fHitNormal);
        lastL = SpaceSide(-1,  out _, out lOrigin, out lDir, out lHitPoint);
        lastR = SpaceSide(+1,  out _, out rOrigin, out rDir, out rHitPoint);
        if (drawDiagonals)
        {
            lastDL = SpaceDiag(-1, out _, out dlOrigin, out dlDir, out dlHitPoint);
            lastDR = SpaceDiag(+1, out _, out drOrigin, out drDir, out drHitPoint);
        }

        // 1) Emergency
        if (lastF < dangerDist)
        {
            float turn = TurnToMoreSpace();
            if (Mathf.Approximately(turn, 0f) && fHit.collider)
                turn = Vector3.Dot(Right(), fHitNormal) > 0 ? -1f : +1f;

            State = BotState.Emergency;

            // arm/extend the hang timer so drift stays on briefly after leaving Emergency
            if (driftDuringEmergency)
                emergencyDriftT = Mathf.Max(emergencyDriftT, emergencyDriftHangTime);

            Drive(turn, 1f, driftDuringEmergency);
            return;
        }

        // decrement the hang timer outside Emergency
        if (emergencyDriftT > 0f) emergencyDriftT -= Time.fixedDeltaTime;

        // 2) Evade
        if (BlockedAhead() || TightLeft() || TightRight())
        {
            State = BotState.Evade;
            // use drift hang only if it's still ticking (optional “finish the carve”)
            Drive(TurnToMoreSpace(), 1f, driftDuringEmergency && emergencyDriftT > 0f);
            return;
        }

        // 3) Cutoff
        if (TryCutoff(out float cutTurn))
        {
            State = BotState.Cutoff;
            Drive(cutTurn, 1f, driftDuringEmergency && emergencyDriftT > 0f);
            return;
        }

        // 4) Scout
        State = BotState.Scout;
        Drive(ChooseMaxSpaceTurn(), 1f, driftDuringEmergency && emergencyDriftT > 0f);
    }

    void Drive(float turn, float throttle, bool drift = false)
    {
        turn = Mathf.Clamp(turn, -1f, 1f);
        if (Mathf.Abs(turn) > 0.05f) lastTurnSign = Mathf.Sign(turn);
        bike.Move(new Vector2(turn, Mathf.Clamp01(throttle)), drift, false, false);
    }

    // -------------------- PROBES --------------------
    Vector3 Up()    => Vector3.up;
    Vector3 Fwd()   => Vector3.ProjectOnPlane(tr.forward, Up()).normalized;
    Vector3 Right() => Vector3.Cross(Up(), Fwd()).normalized;
    Vector3 RayOrigin() => tr.position + Up()*0.6f;

    bool BlockedAhead() => lastF < 8f;
    bool TightLeft()    => lastL < 4f;
    bool TightRight()   => lastR < 4f;

    float TurnToMoreSpace()
    {
        // Prefer diagonals when forward is blocked (represents the actual turn path)
        if (lastF < 8f && drawDiagonals)
        {
            if (Mathf.Abs(lastDL - lastDR) > 0.25f)
                return (lastDR > lastDL ? +1f : -1f) * steerAggression;
        }

        if (Mathf.Abs(lastL - lastR) > 0.25f)
            return (lastR > lastL ? +1f : -1f) * steerAggression;

        // ties → sticky bias
        float bias = lastTurnSign != 0f ? lastTurnSign : seededBias;
        return bias * (steerAggression * 0.95f);
    }

    float ChooseMaxSpaceTurn()
    {
        if (lastF >= lastL && lastF >= lastR && lastF > 6f) return 0f;
        return (lastR > lastL ? +1f : -1f) * (steerAggression * 0.9f);
    }

    float SpaceForward(out RaycastHit hit, out Vector3 origin, out Vector3 dir, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        origin = RayOrigin(); dir = Fwd();
        bool blocked = Physics.SphereCast(origin, hullRadius, dir, out hit, forwardProbe, obstacleMask, QueryTriggerInteraction.Collide);
        hitPoint  = blocked ? hit.point  : origin + dir * forwardProbe;
        hitNormal = blocked ? hit.normal : Vector3.zero;
        return blocked ? hit.distance : forwardProbe;
    }

    float SpaceSide(int side, out RaycastHit hit, out Vector3 origin, out Vector3 dir, out Vector3 hitPoint)
    {
        origin = RayOrigin() + Right()*side*0.9f; dir = Right()*side;
        bool blocked = Physics.SphereCast(origin, hullRadius, dir, out hit, sideProbe, obstacleMask, QueryTriggerInteraction.Collide);
        hitPoint = blocked ? hit.point : origin + dir * sideProbe;
        return blocked ? hit.distance : sideProbe;
    }

    float SpaceDiag(int side, out RaycastHit hit, out Vector3 origin, out Vector3 dir, out Vector3 hitPoint)
    {
        origin = RayOrigin() + Right()*side*0.35f; dir = (Fwd() + Right()*side).normalized;
        bool blocked = Physics.SphereCast(origin, hullRadius, dir, out hit, diagProbe, obstacleMask, QueryTriggerInteraction.Collide);
        hitPoint = blocked ? hit.point : origin + dir * diagProbe;
        return blocked ? hit.distance : diagProbe;
    }

    bool TryCutoff(out float turn)
    {
        turn = 0f;
        var t = NearestOpponent(); if (!t) return false;

        Vector3 up = Up();
        Vector3 f  = Fwd();
        Vector3 tf = Vector3.ProjectOnPlane(t.forward, up).normalized;
        Vector3 to = Vector3.ProjectOnPlane(t.position - tr.position, up);

        if (to.magnitude > cutoffProbe) return false;
        if (Mathf.Abs(Vector3.Cross(f, tf).y) < 0.25f) return false;

        Vector3 aim = t.position + tf * 6f;
        Vector3 dir = Vector3.ProjectOnPlane(aim - tr.position, up).normalized;
        float localX = ToLocalX(dir, f, up);

        if (localX >= 0 && TightRight()) return false;
        if (localX <  0 && TightLeft())  return false;

        turn = localX * steerAggression;
        return true;
    }

    Transform NearestOpponent()
    {
        if (opponents == null || opponents.Length == 0) return null;
        Transform best = null; float bd = float.MaxValue; Vector3 p = tr.position;
        foreach (var x in opponents) { if (!x) continue; float d = (x.position - p).sqrMagnitude; if (d < bd) { bd = d; best = x; } }
        return best;
    }

    static float ToLocalX(Vector3 dir, Vector3 fwd, Vector3 up)
    {
        if (dir.sqrMagnitude < 1e-6f) return 0f;
        return Mathf.Clamp((Quaternion.Inverse(Quaternion.LookRotation(fwd, up)) * dir).x, -1f, 1f);
    }

    // -------------------- GIZMOS --------------------
    void OnDrawGizmos()
    {
        if (!drawGizmosAlways) return;
        DrawGizmosCommon();
    }
    void OnDrawGizmosSelected()
    {
        if (drawGizmosAlways) return; // already drawn
        DrawGizmosCommon();
    }
    void DrawGizmosCommon()
    {
        // colors by state
        Color c = State switch
        {
            BotState.Emergency => new Color(1,0.2f,0.2f,1),
            BotState.Evade     => new Color(1,0.8f,0.2f,1),
            BotState.Cutoff    => new Color(0.6f,1,0.2f,1),
            _                  => new Color(0.2f,0.8f,1,1) // Scout
        };

        // forward
        Gizmos.color = c;
        Gizmos.DrawLine(fOrigin, fOrigin + fDir * lastF);
        Gizmos.DrawSphere(fHitPoint, 0.08f);
        if (drawNormals && fHitNormal != Vector3.zero)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(fHitPoint, fHitPoint + fHitNormal * 0.8f);
        }

        // sides
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(lOrigin, lOrigin + lDir * lastL);
        Gizmos.DrawSphere(lHitPoint, 0.06f);
        Gizmos.DrawLine(rOrigin, rOrigin + rDir * lastR);
        Gizmos.DrawSphere(rHitPoint, 0.06f);

        // diagonals
        if (drawDiagonals)
        {
            Gizmos.color = new Color(0.8f,0.4f,1f,1f);
            Gizmos.DrawLine(dlOrigin, dlOrigin + dlDir * lastDL);
            Gizmos.DrawSphere(dlHitPoint, 0.06f);
            Gizmos.DrawLine(drOrigin, drOrigin + drDir * lastDR);
            Gizmos.DrawSphere(drHitPoint, 0.06f);
        }
    }
}
