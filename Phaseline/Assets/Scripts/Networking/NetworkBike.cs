using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using UnityEngine;

[RequireComponent(typeof(PredictionRigidbody))]
public class NetworkBike : NetworkBehaviour
{
    [SerializeField] private BikeController bikeController; // Your existing logic script
    
    private PredictionRigidbody _pr;
    private PlayerControls _controls;

    private void Awake()
    {
        _pr = GetComponent<PredictionRigidbody>();
        _controls = new PlayerControls();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        // 4.6 API: Subscribe to TimeManager for Tick events
        if (base.TimeManager != null)
        {
            base.TimeManager.OnTick += TimeManager_OnTick;
            base.TimeManager.OnPostTick += TimeManager_OnPostTick;
        }
        
        if (base.Owner.IsLocalClient) _controls.Enable();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (base.TimeManager != null)
        {
            base.TimeManager.OnTick -= TimeManager_OnTick;
            base.TimeManager.OnPostTick -= TimeManager_OnPostTick;
        }
        if (base.Owner.IsLocalClient) _controls.Disable();
    }

    // 1. Gather Input (Tick Start)
    private void TimeManager_OnTick()
    {
        if (base.IsOwner)
        {
            // 4.6 API: Call Replicate with client-authoritative input
            Replicate(BuildInputData());
        }
        else if (IsServerInitialized)
        {
            // Server runs default/empty replicate to keep tick sync if no input arrives
            Replicate(default);
        }
    }

    // 2. Reconcile (Tick End)
    private void TimeManager_OnPostTick()
    {
        // Only the Server sends reconciliation data
        if (IsServerInitialized)
        {
            BikeReconcileData state = new BikeReconcileData(
                transform.position, 
                transform.rotation, 
                _pr.Rigidbody.linearVelocity, // Unity 6 (was velocity)
                _pr.Rigidbody.angularVelocity
            );
            Reconcile(state);
        }
    }

    private BikeInputData BuildInputData()
    {
        return new BikeInputData
        {
            Move = _controls.Gameplay.Move.ReadValue<Vector2>(),
            Drift = _controls.Gameplay.Drift.IsPressed(),
            Boost = _controls.Gameplay.Boost.IsPressed(),
            Jump = _controls.Gameplay.Jump.IsPressed()
        };
    }

    // 3. The Prediction Logic (Runs on both Client & Server)
    [Replicate]
    private void Replicate(BikeInputData input, ReplicateState state = ReplicateState.Invalid)
    {
        float dt = (float)base.TimeManager.TickDelta;

        // IMPORTANT: Modify your BikeController.Move to accept PredictionRigidbody
        // so it applies forces to the PR, not the standard Rigidbody.
        // Alternatively, apply forces here directly:
        
        // Example logic (simplified):
        Vector3 force = transform.forward * input.Move.y * 50f;
        _pr.AddForce(force); // 4.6 API: Use _pr wrapper, not rb directly!
        
        // If you rely on BikeController logic, pass _pr to it:
        // bikeController.Move(input, _pr, dt); 
    }

    [Reconcile]
    private void Reconcile(BikeReconcileData data)
    {
        // 4.6 API: Hard reset the physics state
        transform.position = data.Position;
        transform.rotation = data.Rotation;
        _pr.Velocity(data.Velocity);
        _pr.AngularVelocity(data.AngularVelocity);
    }
}