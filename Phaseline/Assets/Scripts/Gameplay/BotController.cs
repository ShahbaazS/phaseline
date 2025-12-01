using System.IO;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(BikeController))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Damageable))]
public class BotController : MonoBehaviour
{
    // -------- FSM --------
    public enum BotState { Scout, Evade, Cutoff, Emergency, Pathfind }
    public BotState State { get; private set; } = BotState.Scout;

    [Header("Pathfinding Priorities")]
    [Tooltip("Lower value = Higher Priority. 1.0 = Real Distance. 0.5 =appears 2x closer.")]
    public float powerUpWeight = 0.7f; // Power-ups appear 30% closer than they really are
    public float portalWeight = 1.0f;
    public float goalScanRange = 100f;

    // --- Pathfinding ---
    BotPathfinder path;
    [SerializeField] float pathHangTime = 0.35f;
    float pathHangT = 0f;
    Transform currentGoal; // cache to avoid re-calling SetTarget every frame
    
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

    [Header("Attack Targeting")]
    public float attackRange = 40f;
    public float attackStickiness = 0.6f;     // seconds to keep chasing even if score dips
    public float attackLeadSeconds = 0.7f;    // how far ahead to lead
    public float maxLeadDistance = 12f;       // clamp lead distance
    public LayerMask losMask = ~0;            // walls/trails for LOS checks

    float attackHangT = 0f;
    Transform currentOpponent;                // cached best opponent across frames
    Vector3 lastLead;                         // last chosen lead (for continuity)

    [Header("Powerups")]
    public float powerUpScanRange = 60f;

    // -------- Gizmos --------
    [Header("Gizmos")]
    public bool drawGizmosAlways = true;   // show in Game view too (toggle Gizmos in Game view)
    public bool drawDiagonals    = true;
    public bool drawNormals      = false;

    // cached refs/state
    BikeController bike;
    Damageable hp;
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
        hp = GetComponent<Damageable>();
        rb   = GetComponent<Rigidbody>();
        tr = transform;
        path = GetComponent<BotPathfinder>();
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

    // VISUALS LOOP
    private void Update()
    {
        // Smoothly interpolate visuals every frame based on the physics state
        // We pass Time.deltaTime here because this is for interpolation, not physics steps
        bike.UpdateVisuals(Time.deltaTime);
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

        // 1) Emergency override
        if (lastF < dangerDist)
        {
            float turn = TurnToMoreSpace();
            if (Mathf.Approximately(turn, 0f) && fHit.collider)
                turn = Vector3.Dot(Right(), fHitNormal) > 0 ? -1f : +1f;

            State = BotState.Emergency;

            if (driftDuringEmergency)
                emergencyDriftT = Mathf.Max(emergencyDriftT, emergencyDriftHangTime);

            // HARD escape; disable path steering this frame
            if (path && path.enabled) path.enabled = false;

            Drive(turn, 1f, driftDuringEmergency);
            return;
        }

        // drift hang after emergency
        if (emergencyDriftT > 0f) emergencyDriftT -= Time.fixedDeltaTime;

        // 2) Pathfinding decision
        bool pickedAttack = TryPickBestOpponent(out var opp, out var leadPoint);

        Transform goal = FindNearestGoal();                 // portals/powerups (your existing method)
        bool hasGoal = goal != null;

        // choose which path to use this frame
        if (pickedAttack)
        {
            State = BotState.Pathfind;
            if (path && !path.enabled) path.enabled = true;
            if (path) path.SetTargetPosition(leadPoint);    // position goal (lead point)
            return; // IMPORTANT: don't also run manual Drive this frame
        }
        else if (hasGoal)
        {
            State = BotState.Pathfind;
            if (path && !path.enabled) path.enabled = true;
            if (path) path.SetTarget(goal);                 // transform goal
            return;
        }
        else
        {
            if (path && path.enabled) path.enabled = false; // give control back to reactive FSM
        }

        // 3) Evade when tight
        if (BlockedAhead() || TightLeft() || TightRight())
        {
            State = BotState.Evade;
            Drive(TurnToMoreSpace(), 1f, driftDuringEmergency && emergencyDriftT > 0f);
            return;
        }

        // 4) Cutoff opportunity
        if (TryCutoff(out float cutTurn))
        {
            State = BotState.Cutoff;
            Drive(cutTurn, 1f, driftDuringEmergency && emergencyDriftT > 0f);
            return;
        }

        // 5) Scout default
        State = BotState.Scout;
        Drive(ChooseMaxSpaceTurn(), 1f, driftDuringEmergency && emergencyDriftT > 0f);
    }

    void Drive(float turn, float throttle, bool drift = false)
    {
        turn = Mathf.Clamp(turn, -1f, 1f);
        if (Mathf.Abs(turn) > 0.05f) lastTurnSign = Mathf.Sign(turn);
        bike.Simulate(new Vector2(turn, Mathf.Clamp01(throttle)), drift, false, false, Time.fixedDeltaTime);
    }

    // -------------------- PROBES --------------------
    Vector3 Up()    => SurfaceUp();
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

    // Gather *all* possible enemy roots (player + other bots). Prefer your SpawnManager, else scene scan.
    IEnumerable<Transform> EnumerateOpponents()
    {
        // If you already keep an opponents[] array, yield those here.
        // Fallback: scan all bikes (tag "Bike") and filter self.
        foreach (var go in GameObject.FindGameObjectsWithTag("Player"))
        {
            if (!go) continue;
            var t = go.transform;
            if (t == transform) continue;

            yield return t;
        }
    }

    Vector3 SurfaceUp()
    {
        if (Physics.Raycast(transform.position + Vector3.up*0.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            return hit.normal;
        return Vector3.up;
    }

    bool HasLOS(Vector3 from, Vector3 to)
    {
        var dir = to - from;
        if (dir.sqrMagnitude < 0.01f) return true;
        return !Physics.Raycast(from, dir.normalized, dir.magnitude, losMask, QueryTriggerInteraction.Ignore);
    }

    static float CrossingMagnitude(Vector3 aDir, Vector3 bDir)
    {
        // how “crossing” the headings are (0 parallel…1 perpendicular/opposed)
        return Mathf.Abs(Vector3.Cross(aDir.normalized, bDir.normalized).y);
    }

    Vector3 PredictLead(Transform tgt, float leadSec, float maxLead, Vector3 up)
    {
        Vector3 pos = tgt.position;
        Vector3 vel = Vector3.zero;
        if (tgt.TryGetComponent<Rigidbody>(out var rb)) vel = rb.linearVelocity;
        vel = Vector3.ProjectOnPlane(vel, up);
        if (vel.sqrMagnitude < 0.01f) return pos;

        var lead = pos + Vector3.ClampMagnitude(vel * leadSec, maxLead);
        return lead;
    }

    // Utility score for any opponent
    float ScoreOpponent(Transform opp, Vector3 up, out Vector3 aimPoint)
    {
        aimPoint = opp.position;
        var to = opp.position - transform.position;
        float dist = to.magnitude;
        if (dist > attackRange) return -1f;          // out of range

        bool los = HasLOS(transform.position, opp.position);
        float losScore = los ? 1f : 0.25f;

        // Heading crossing (prefer intercepts)
        var myF = Vector3.ProjectOnPlane(transform.forward, up);
        var oppF = myF;
        if (opp) oppF = Vector3.ProjectOnPlane(opp.forward, up);
        float cross = CrossingMagnitude(myF, oppF);  // 0..1

        // Distance score (closer is better)
        float dScore = Mathf.Clamp01(1f - (dist / attackRange));

        // Predict lead and use that as aim point
        aimPoint = PredictLead(opp, attackLeadSeconds, maxLeadDistance, up);

        // Blend: weight distance the most, then LOS, then crossing
        float score = dScore * 0.70f + losScore * 0.20f + cross * 0.10f;
        return score; // 0..1
    }

    // Find best opponent among *all* bikes (player + bots), with stickiness
    bool TryPickBestOpponent(out Transform best, out Vector3 leadPoint)
    {
        best = null; leadPoint = Vector3.zero;
        Vector3 up = SurfaceUp();

        float bestScore = -1f; Vector3 bestLead = Vector3.zero;
        foreach (var t in EnumerateOpponents())
        {
            var s = ScoreOpponent(t, up, out var lead);
            if (s > bestScore)
            {
                bestScore = s; best = t; bestLead = lead;
            }
        }

        // stickiness: if currentOpponent exists, prefer it unless a new score is significantly better
        if (currentOpponent && best && best != currentOpponent)
        {
            var curScore = ScoreOpponent(currentOpponent, up, out var curLead);
            // small hysteresis: require +0.1 improvement to retarget
            if (curScore >= 0f && curScore + 0.10f >= bestScore)
            {
                best = currentOpponent; bestLead = curLead; bestScore = curScore;
            }
        }

        if (bestScore >= 0f)
        {
            currentOpponent = best;
            lastLead = bestLead;
            attackHangT = attackStickiness; // refresh stickiness
            leadPoint = bestLead;
            return true;
        }

        // allow short hang after losing everyone
        if (attackHangT > 0f && currentOpponent)
        {
            attackHangT -= Time.fixedDeltaTime;
            // keep last known lead for a moment
            best = currentOpponent; leadPoint = lastLead;
            return true;
        }

        currentOpponent = null;
        return false;
    }


    static float ToLocalX(Vector3 dir, Vector3 fwd, Vector3 up)
    {
        if (dir.sqrMagnitude < 1e-6f) return 0f;
        return Mathf.Clamp((Quaternion.Inverse(Quaternion.LookRotation(fwd, up)) * dir).x, -1f, 1f);
    }

    Transform FindNearestGoal()
    {
        Vector3 p = transform.position;
        Transform best = null; 
        float bestScore = float.MaxValue; 

        // 1. Scan Portals
        foreach (var g in GameObject.FindGameObjectsWithTag("Portal"))
        {
            float d = Vector3.Distance(g.transform.position, p);
            if (d > goalScanRange) continue;

            float score = d * portalWeight; // Base Score
            if (score < bestScore) { bestScore = score; best = g.transform; }
        }

        // 2. Scan PowerUps
        foreach (var g in GameObject.FindGameObjectsWithTag("PowerUp"))
        {
            // Ignore if disabled/collected
            var col = g.GetComponent<Collider>();
            if (!col || !col.enabled) continue;

            float d = Vector3.Distance(g.transform.position, p);
            if (d > goalScanRange) continue;

            // Apply Weight: If powerUpWeight is 0.7, a 20m item counts as 14m score.
            // This makes the bot prioritize it over a 15m Portal.
            float score = d * powerUpWeight; 
            
            if (score < bestScore) { bestScore = score; best = g.transform; }
        }

        return best;
    }

    // ---------------- NEW ABILITIES ----------------

    public void ApplyBoost(float duration, float multiplier)
    {
        StopCoroutine("CoBoost");
        StartCoroutine(CoBoost(duration, multiplier));
    }

    public void ApplyShield(float duration)
    {
        StopCoroutine("CoShield");
        StartCoroutine(CoShield(duration));
    }

    IEnumerator CoBoost(float duration, float multiplier)
    {
        // Bots don't need prediction, so we set the multiplier directly
        bike.SetSpeedMultiplier(multiplier);
        yield return new WaitForSeconds(duration);
        bike.SetSpeedMultiplier(1.0f);
    }

    IEnumerator CoShield(float duration)
    {
        if (hp) hp.IsInvulnerable = true;
        yield return new WaitForSeconds(duration);
        if (hp) hp.IsInvulnerable = false;
    }

    // Ensure state resets on death/respawn
    void OnEnable()
    {
        if (bike) bike.SetSpeedMultiplier(1.0f);
        if (hp) hp.IsInvulnerable = false;
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
