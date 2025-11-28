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

    private Rigidbody rb;
    private Vector3 currentSurfaceNormal = Vector3.up;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        var col = GetComponent<Collider>();
        groundRayLength = col.bounds.extents.y + groundOffset;
    }

    public void Move(Vector2 input, bool drifting, bool boosting, bool jumping, float dt)
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
            currentSurfaceNormal = hit.normal;
            bool grounded = hit.distance <= groundRayLength;

            if (grounded && jumping)
            {
                rb.AddForce(currentSurfaceNormal * jumpForce, ForceMode.Impulse);
                return;
            }

            // Magnetic gravity
            rb.AddForce(-currentSurfaceNormal * stickForce, ForceMode.Acceleration);

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

    void StabilizeAndAlign(Vector3 upNormal, float dt)
    {
        Vector3 upError = Vector3.Cross(transform.up, upNormal);
        rb.AddTorque(upError * uprightTorque, ForceMode.Acceleration);

        Quaternion goal = Quaternion.FromToRotation(transform.up, upNormal) * rb.rotation;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, goal, slopeAlignSpeed * dt));

        AlignVisualToGround(dt);
    }

    void DriveAlongSurface(Vector2 input, bool drifting, bool boosting, Vector3 upNormal, float dt)
    {
        Vector3 forwardDir = Vector3.ProjectOnPlane(transform.forward, upNormal).normalized;
        float targetSpeed = maxSpeed * input.y; // Boosting logic can be added here
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

        AlignVisualToGround(dt);
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

    public void AlignVisualToGround(float dt)
    {
        if (!visualModel) return;
        if (Physics.Raycast(transform.position, -transform.up, out RaycastHit hit, groundRayLength * 2f, groundLayer))
        {
            Quaternion slopeRotation = Quaternion.FromToRotation(visualModel.up, hit.normal) * visualModel.rotation;
            visualModel.rotation = Quaternion.Slerp(visualModel.rotation, slopeRotation, dt * 5f);
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

}
