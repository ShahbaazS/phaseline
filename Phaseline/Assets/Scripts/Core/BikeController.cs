using UnityEngine;

public class BikeController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float acceleration = 60f;
    [SerializeField] float maxSpeed = 30f;
    [SerializeField] float turnStrength = 5f;
    [SerializeField] AnimationCurve turnCurve;

    [Header("Adhesion")]
    [SerializeField] float stickForce = 25f;
    [SerializeField] float stickDistanceThreshold = 2f;
    [SerializeField] float uprightTorque = 50f;
    [SerializeField] float slopeAlignSpeed = 10f;

    [Header("Drift")]
    [SerializeField] float driftFactor = 0.7f;
    [SerializeField] float lateralFriction = 0.8f;

    [Header("Jumping")]
    [SerializeField] float jumpForce = 8f;

    [Header("Ground + Gravity")]
    [SerializeField] LayerMask groundLayer;
    [SerializeField] float groundOffset = 0.5f;
    private float groundRayLength;

    [Header("Visual Settings")]
    [SerializeField] private Transform visualModel;
    [SerializeField] private float maxBankingAngle = 90f;
    [SerializeField] private float visualBankingSpeed = 15f;
    [SerializeField] private TrailRenderer driftTrail;
    [SerializeField] private Transform frontWheelTransform;
    [SerializeField] private Transform rearWheelTransform;
    [SerializeField] private float wheelRotSpeed = 1000f;
    [SerializeField] private ParticleSystem sparksEffect;
    private float driftTrailWidth = 0.5f;
    private float driftTrailVelocity = 10f;

    private Rigidbody rb;
    private Vector3 currentSurfaceNormal = Vector3.up;

    // Cache state for visuals to read
    private float _lastTurnInput; 
    private bool _isDrifting;

    private float _externalSpeedMultiplier = 1.0f;

    public void SetSpeedMultiplier(float mult)
    {
        _externalSpeedMultiplier = mult;
    }

    public void Awake()
    {
        rb = GetComponent<Rigidbody>();
        var col = GetComponent<Collider>();
        groundRayLength = col.bounds.extents.y + groundOffset;
        
        if (driftTrail)
        {
            driftTrailWidth = driftTrail.startWidth;
            driftTrail.emitting = false;
        }
    }

    // Safety Reset
    void OnEnable()
    {
        _externalSpeedMultiplier = 1.0f;
    }

    /// <summary>
    /// Pure Physics Simulation. Safe to call multiple times for prediction.
    /// NO VISUALS HERE.
    /// </summary>
    public void Simulate(Vector2 input, bool drifting, bool boosting, bool jumping, float dt)
    {
        _lastTurnInput = input.x;
        _isDrifting = drifting;

        // Raycast out to the stick threshold
        bool nearSurface = Physics.Raycast(
            transform.position,
            -currentSurfaceNormal,
            out RaycastHit hit,
            stickDistanceThreshold,
            groundLayer
        );

        if (nearSurface)
        {
            currentSurfaceNormal = hit.normal;
            bool grounded = hit.distance <= groundRayLength;

            if (grounded && jumping)
            {
                rb.AddForce(currentSurfaceNormal * jumpForce, ForceMode.Impulse);
            }
            else
            {
                // Magnetic gravity
                rb.AddForce(-currentSurfaceNormal * stickForce, ForceMode.Acceleration);
            }

            if (grounded)
            {
                StabilizeAndAlign(currentSurfaceNormal, dt);
                DriveAlongSurface(input, drifting, boosting, currentSurfaceNormal, dt);
            }
        }
        else
        {
            // Airborne
            rb.AddForce(Physics.gravity, ForceMode.Acceleration);
            StabilizeAndAlign(Vector3.up, dt);
        }
    }

    /// <summary>
    /// Updates Visuals (Banking, Wheels, Trails). 
    /// Call this from Update() or LateUpdate(), NEVER inside Replicate().
    /// </summary>
    public void UpdateVisuals(float dt)
    {
        // Handle Visuals
        VisualBank(_lastTurnInput, dt);
        AlignVisualToGround(dt);
        UpdateWheelRotation(dt);
        DriftTrailEffect(_isDrifting);
        Sparks();
    }

    void StabilizeAndAlign(Vector3 upNormal, float dt)
    {
        Vector3 upError = Vector3.Cross(transform.up, upNormal);
        rb.AddTorque(upError * uprightTorque, ForceMode.Acceleration);

        // Physics rotation for the Rigidbody (Game Logic)
        Quaternion goal = Quaternion.FromToRotation(transform.up, upNormal) * rb.rotation;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, goal, slopeAlignSpeed * dt));
    }

    void DriveAlongSurface(Vector2 input, bool drifting, bool boosting, Vector3 upNormal, float dt)
    {
        Vector3 forwardDir = Vector3.ProjectOnPlane(transform.forward, upNormal).normalized;
        float targetSpeed = maxSpeed * input.y * _externalSpeedMultiplier;
        Vector3 forwardVel = forwardDir * targetSpeed;
        
        Vector3 deltaVel = forwardVel - Vector3.Project(rb.linearVelocity, forwardDir);
        rb.AddForce(deltaVel * acceleration, ForceMode.Acceleration);

        Vector3 rightDir = Vector3.Cross(upNormal, forwardDir).normalized;
        Vector3 lateralVel = Vector3.Project(rb.linearVelocity, rightDir);
        float friction = drifting ? driftFactor : lateralFriction;
        rb.AddForce(-lateralVel * friction, ForceMode.VelocityChange);

        if (Mathf.Abs(input.x) > 0.01f)
        {
            float speedPct = Mathf.Clamp01(rb.linearVelocity.magnitude / maxSpeed);
            float turnAmt = input.x * turnStrength * turnCurve.Evaluate(speedPct) * dt;
            rb.MoveRotation(rb.rotation * Quaternion.AngleAxis(turnAmt, upNormal));
        }
    }

    // --- Visuals (No Physics Forces Here) ---

    public void AlignVisualToGround(float dt)
    {
        if (!visualModel) return;
        // Visual-only raycast
        if (Physics.Raycast(transform.position, -transform.up, out RaycastHit hit, groundRayLength * 2f, groundLayer))
        {
            Quaternion slopeRotation = Quaternion.FromToRotation(visualModel.up, hit.normal) * visualModel.rotation;
            visualModel.rotation = Quaternion.Slerp(visualModel.rotation, slopeRotation, dt * 5f);
        }
    }

    void VisualBank(float turnInput, float dt)
    {
        if (!visualModel || !rb) return;
        float currentVelocityOffset = rb.linearVelocity.magnitude / maxSpeed;
        float targetBank = -turnInput * maxBankingAngle * currentVelocityOffset;

        Quaternion targetRotation = Quaternion.Euler(visualModel.localEulerAngles.x, visualModel.localEulerAngles.y, targetBank);
        visualModel.localRotation = Quaternion.Slerp(visualModel.localRotation, targetRotation, dt * visualBankingSpeed);
    }

    void UpdateWheelRotation(float dt)
    {
        float currentVelocityOffset = rb.linearVelocity.magnitude / maxSpeed;
        float wheelRotation = currentVelocityOffset * wheelRotSpeed * dt;
        Vector3 right = Vector3.Cross(transform.up, transform.forward).normalized;

        if (frontWheelTransform != null) frontWheelTransform.Rotate(right, wheelRotation, Space.Self);
        if (rearWheelTransform != null) rearWheelTransform.Rotate(right, wheelRotation, Space.Self);
    }

    void DriftTrailEffect(bool driftInput)
    {
        if (!driftTrail) return;
        if (driftInput && rb.linearVelocity.magnitude > driftTrailVelocity)
        {
            float currentVelocityOffset = rb.linearVelocity.magnitude / maxSpeed;
            driftTrail.startWidth = Mathf.Lerp(driftTrailWidth, driftTrailWidth * 2f, currentVelocityOffset);
            driftTrail.emitting = true;
        }
        else
        {
            driftTrail.startWidth = driftTrailWidth;
            driftTrail.emitting = false;
        }
    }

    void Sparks()
    {
        if (!sparksEffect) return;
        if (rb.linearVelocity.magnitude > 5f && Physics.Raycast(transform.position, -transform.up, groundRayLength * 1.2f, groundLayer))
        {
            if (!sparksEffect.isPlaying) sparksEffect.Play();
        }
        else
        {
            if (sparksEffect.isPlaying) sparksEffect.Stop();
        }
    }

    void OnCollisionStay(Collision col)
    {
        Vector3 sum = Vector3.zero;
        foreach (var c in col.contacts) sum += c.normal;
        if (col.contactCount > 0) currentSurfaceNormal = (sum / col.contactCount).normalized;
    }

    void OnCollisionExit(Collision col)
    {
        currentSurfaceNormal = Vector3.up;
    }
}