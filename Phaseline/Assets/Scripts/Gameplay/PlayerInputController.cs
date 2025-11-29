using UnityEngine;

[RequireComponent(typeof(BikeController))]
public class PlayerInputController : MonoBehaviour
{
    [Header("Movement")]
    [Range(0f, 1f)] public float minThrottle = 0.6f;
    public float steerDeadzone = 0.05f;

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

        float throttle = Mathf.Clamp01(move.y);
        throttle = Mathf.Max(throttle, minThrottle);

        float steer = Mathf.Abs(move.x) < steerDeadzone ? 0f : move.x;

        bool drift = controls.Gameplay.Drift.IsPressed();
        bool boost = controls.Gameplay.Boost.IsPressed();
        bool jump  = controls.Gameplay.Jump.IsPressed();

        // Passed Time.fixedDeltaTime to match new signature
        bike.Move(new Vector2(steer, throttle), drift, boost, jump, Time.fixedDeltaTime);
    }
}