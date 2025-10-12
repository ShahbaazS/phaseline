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
    [SerializeField] Vector3 gravity = new Vector3(0, -25f, 0);
    private float groundRayLength;

    [Header("Visual Settings")]
    [SerializeField] private Transform visualModel;
    [SerializeField] private Transform orientation;
    [SerializeField] private float maxBankingAngle = 90f;
    [SerializeField] private float visualBankingSpeed = 15f;
    [SerializeField] private TrailRenderer driftTrail;
    [SerializeField] private Transform frontWheelTransform;
    [SerializeField] private Transform rearWheelTransform;
    [SerializeField] private float wheelRotSpeed = 10000f;
    [SerializeField] private ParticleSystem sparksEffect;
    private float driftTrailWidth = 0.5f, driftTrailVelocity = 10f;

    [Header("Audio Settings")]
    [SerializeField] private AudioSource engineAudioSource;
    [SerializeField] private AudioSource driftAudioSource;
    [SerializeField] private AudioSource boostAudioSource;
    [Range(0f, 1f)][SerializeField] private float minPitch;
    [Range(1f, 5f)][SerializeField] private float maxPitch;

    private Rigidbody rb;
    private Vector3 currentSurfaceNormal = Vector3.up;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        var col = GetComponent<Collider>();
        groundRayLength = col.bounds.extents.y + groundOffset;

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.useGravity = false;

        driftTrail.startWidth = driftTrailWidth;
        driftTrail.emitting = false;

        driftAudioSource.mute = true;
        boostAudioSource.mute = true;
    }

    public void Move(Vector2 input, bool drifting, bool boosting, bool jumping)
    {
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
            // Always update the normal if we're within stick range
            currentSurfaceNormal = hit.normal;

            // Decide grounded vs just “hovering”
            bool grounded = hit.distance <= groundRayLength;

            // Jump has priority: impulse and bail out
            if (grounded && jumping)
            {
                rb.AddForce(currentSurfaceNormal * jumpForce, ForceMode.Impulse);
                return;
            }

            // Magnetic gravity toward the surface
            rb.AddForce(-currentSurfaceNormal * stickForce, ForceMode.Acceleration);

            if (grounded)
            {
                // Only when actually grounded do we do normal drive & drift
                StabilizeAndAlign(currentSurfaceNormal);
                DriveAlongSurface(input, drifting, boosting, currentSurfaceNormal);
            }
            else
            {
                // In “near but not grounded,” you might still want a little air control…
                //AirControl(input);
            }
        }
        else
        {
            // Fully airborne to world-down gravity and re-upright
            rb.AddForce(Physics.gravity, ForceMode.Acceleration);
            StabilizeAndAlign(Vector3.up);
            //AirControl(input);
        }
    }

    void StabilizeAndAlign(Vector3 upNormal) {
        // Right‐yourself so you don’t flop off
        Vector3 upError = Vector3.Cross(transform.up, upNormal);
        rb.AddTorque(upError * uprightTorque, ForceMode.Acceleration);

        // Align your up‐axis to the surface normal
        Quaternion goal = Quaternion.FromToRotation(transform.up, upNormal) * rb.rotation;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, goal, slopeAlignSpeed * Time.fixedDeltaTime));
    }

    void DriveAlongSurface(Vector2 input, bool drifting, bool boosting, Vector3 upNormal) {
        // Forward acceleration
        Vector3 forwardDir = Vector3.ProjectOnPlane(transform.forward, upNormal).normalized;
        float targetSpeed = maxSpeed * input.y;
        Vector3 forwardVel = forwardDir * targetSpeed;
        // only accelerate the component of velocity in forward direction
        Vector3 deltaVel = forwardVel - Vector3.Project(rb.linearVelocity, forwardDir);
        rb.AddForce(deltaVel * acceleration, ForceMode.Acceleration);

        // Lateral/friction
        Vector3 rightDir = Vector3.Cross(upNormal, forwardDir).normalized;
        Vector3 lateralVel = Vector3.Project(rb.linearVelocity, rightDir);
        float friction = drifting ? driftFactor : lateralFriction;
        rb.AddForce(-lateralVel * friction, ForceMode.VelocityChange);

        // Turning
        if (Mathf.Abs(input.x) > 0.01f)
        {
            float speedPct = Mathf.Clamp01(rb.linearVelocity.magnitude / maxSpeed);
            float turnAmt  = input.x
                           * turnStrength
                           * turnCurve.Evaluate(speedPct)
                           * Time.fixedDeltaTime;

            rb.MoveRotation(rb.rotation * Quaternion.AngleAxis(turnAmt, upNormal));
        }
    }

    // Whenever we’re touching something, average its contact normals.
    void OnCollisionStay(Collision col)
    {
        Vector3 sum = Vector3.zero;
        foreach (var c in col.contacts) sum += c.normal;
        currentSurfaceNormal = (sum / col.contactCount).normalized;
    }

    // If we leave all surfaces, default back to world-down.
    void OnCollisionExit(Collision col)
    {
        currentSurfaceNormal = Vector3.up;
    }

    public void AlignVisualToGround()
    {
        if (!visualModel) return;

        if (Physics.Raycast(transform.position, -transform.up, out RaycastHit hit, groundRayLength * 2f, groundLayer))
        {
            Quaternion slopeRotation = Quaternion.FromToRotation(visualModel.up, hit.normal) * visualModel.rotation;
            visualModel.rotation = Quaternion.Slerp(visualModel.rotation, slopeRotation, Time.deltaTime * 5f);
        }
    }

    public void VisualBank(float turnInput)
    {
        if (!visualModel || !rb) return;

        float currentVelocityOffset = rb.linearVelocity.magnitude / maxSpeed;
        float targetBank = -turnInput * maxBankingAngle * currentVelocityOffset;

        Quaternion targetRotation = Quaternion.Euler(visualModel.localRotation.x, visualModel.localRotation.y, targetBank);
        visualModel.localRotation = Quaternion.Slerp(
            visualModel.localRotation,
            targetRotation,
            Time.deltaTime * visualBankingSpeed
        );
    }

    public void DriftTrailEffect(bool driftInput)
    {
        if (driftInput && rb.linearVelocity.magnitude > driftTrailVelocity)
        {
            float currentVelocityOffset = rb.linearVelocity.magnitude / maxSpeed;
            driftTrail.startWidth = Mathf.Lerp(driftTrailWidth, driftTrailWidth * 2f, currentVelocityOffset);
            driftTrail.emitting = true;

            if (driftAudioSource && !driftAudioSource.isPlaying)
            {
                driftAudioSource.mute = false;
                driftAudioSource.Play();
            }
        }
        else
        {
            driftTrail.startWidth = driftTrailWidth;
            driftTrail.emitting = false;

            if (driftAudioSource && driftAudioSource.isPlaying)
            {
                driftAudioSource.mute = true;
                driftAudioSource.Stop();
            }
        }
    }

    public void UpdateWheelRotation()
    {
        float currentVelocityOffset = rb.linearVelocity.magnitude / maxSpeed;
        float wheelRotation = currentVelocityOffset * wheelRotSpeed * Time.deltaTime;
        Vector3 right = Vector3.Cross(transform.up, transform.forward).normalized;

        if (frontWheelTransform != null) frontWheelTransform.Rotate(right, wheelRotation, Space.Self);
        if (rearWheelTransform != null) rearWheelTransform.Rotate(right, wheelRotation, Space.Self);
    }

    public void EngineSound()
    {
        if (!engineAudioSource) return;

        float speedFactor = rb.linearVelocity.magnitude / maxSpeed;
        engineAudioSource.pitch = Mathf.Lerp(minPitch, maxPitch, speedFactor);
    }

    public void Sparks()
    {
        if (!sparksEffect) return;

        if (rb.linearVelocity.magnitude < 5f)
        {
            if (sparksEffect.isPlaying)
                sparksEffect.Stop();
        }
        else
        {
            if (!sparksEffect.isPlaying)
                sparksEffect.Play();
        }
    }

}
