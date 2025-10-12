using UnityEngine;

[RequireComponent(typeof(BikeController))]
public class PlayerInputController : MonoBehaviour
{
    [Header("Movement")]
    [Range(0f, 1f)] public float minThrottle = 0.6f;  // always move forward at least this much
    public float steerDeadzone = 0.05f;                // optional: reduce tiny steering noise

    private PlayerControls controls;
    private BikeController bike;

    void Awake()
    {
        bike = GetComponent<BikeController>();
        controls = new PlayerControls();
    }

    void OnEnable()  => controls.Enable();
    void OnDisable() => controls.Disable();

    void FixedUpdate()
    {
        Vector2 move = controls.Gameplay.Move.ReadValue<Vector2>();

        // Ignore reverse: clamp Y to [0..1], then enforce min forward
        float throttle = Mathf.Clamp01(move.y);
        throttle = Mathf.Max(throttle, minThrottle);

        // Optional: soften tiny steering noise when not touching the stick/keys
        float steer = Mathf.Abs(move.x) < steerDeadzone ? 0f : move.x;

        bool drift = controls.Gameplay.Drift.IsPressed();
        bool boost = controls.Gameplay.Boost.IsPressed();
        bool jump  = controls.Gameplay.Jump.IsPressed();

        bike.Move(new Vector2(steer, throttle), drift, boost, jump);
    }

}
