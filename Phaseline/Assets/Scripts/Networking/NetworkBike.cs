using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;

[RequireComponent(typeof(BikeController))]
public class NetworkBike : NetworkBehaviour
{
    [Header("Input Settings")]
    [Range(0f, 1f)] public float minThrottle = 0.6f;
    private BikeController _controller;
    private PredictionRigidbody _pr;
    private PlayerControls _controls;

    // Structs for FishNet 4.6 Prediction V2
    public struct BikeInputData : IReplicateData
    {
        public Vector2 MoveInput;
        public bool Drift;
        public bool Jump;
        public bool Boost;
        
        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    public struct BikeReconcileData : IReconcileData
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;

        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    private void Awake()
    {
        _controller = GetComponent<BikeController>();
        _pr = new PredictionRigidbody();
        _pr.Initialize(GetComponent<Rigidbody>());
        _controls = new PlayerControls();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        // Check if this bike belongs to the local player
        if (base.Owner.IsLocalClient)
        {
            // Enable Inputs
            _controls.Enable();
            TimeManager.OnTick += TimeManager_OnTick;

            // --- CAMERA ASSIGNMENT ---
            // Find the main camera and set its target to this bike
            var cam = Camera.main?.GetComponent<FollowCamera>();
            if (cam != null)
            {
                cam.target = transform;
            }
            else
            {
                Debug.LogWarning("[NetworkBike] FollowCamera not found on MainCamera!");
            }
        }

        // Server-side logic for prediction
        if (base.IsServerInitialized)
        {
            TimeManager.OnPostTick += TimeManager_OnPostTick;
        }
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (_controls != null) _controls.Disable();
        TimeManager.OnTick -= TimeManager_OnTick;
        TimeManager.OnPostTick -= TimeManager_OnPostTick;
    }

    // 1. Gather Input (Client Only)
    private void TimeManager_OnTick()
    {
        if (!base.IsOwner) return;

        // Read Raw Input
        Vector2 rawInput = _controls.Gameplay.Move.ReadValue<Vector2>();

        // Apply Auto-Throttle Logic
        // Ignore 'S' (reverse) by clamping 0..1, then ensure we never go below minThrottle
        float throttle = Mathf.Clamp01(rawInput.y);
        throttle = Mathf.Max(throttle, minThrottle);

        // Create the Input Data
        BikeInputData data = new BikeInputData
        {
            // Pass the processed throttle (x = steer, y = throttle)
            MoveInput = new Vector2(rawInput.x, throttle),
            Drift = _controls.Gameplay.Drift.IsPressed(),
            Boost = _controls.Gameplay.Boost.IsPressed(),
            Jump = _controls.Gameplay.Jump.WasPressedThisFrame() 
        };

        Replicate(data);
    }
    // 2. Run Logic (Client & Server)
    [Replicate]
    private void Replicate(BikeInputData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        // Use TimeManager.TickDelta (fixed time step for prediction)
        float dt = (float)base.TimeManager.TickDelta;
        _controller.Move(data.MoveInput, data.Drift, data.Boost, data.Jump, dt);
    }

    // 3. Trigger Reconcile Creation (Server Only)
    private void TimeManager_OnPostTick()
    {
        // Only the server needs to create reconciliation data to send to clients
        if (base.IsServerInitialized)
        {
            CreateReconcile();
        }
    }

    // 4. Create Snapshot (Override - Parameterless)
    // FIX: Removed arguments to match FishNet V2 base method signature
    public override void CreateReconcile()
    {
        BikeReconcileData data = new BikeReconcileData
        {
            Position = transform.position,
            Rotation = transform.rotation,
            Velocity = _pr.Rigidbody.linearVelocity,
            AngularVelocity = _pr.Rigidbody.angularVelocity
        };

        Reconcile(data);
    }

    // 5. Correct Mistakes (Client Only)
    [Reconcile]
    private void Reconcile(BikeReconcileData data, Channel channel = Channel.Unreliable)
    {
        transform.position = data.Position;
        transform.rotation = data.Rotation;
        _pr.Rigidbody.linearVelocity = data.Velocity;
        _pr.Rigidbody.angularVelocity = data.AngularVelocity;
    }

    // API for Portals/Teleports
    public void Teleport(Vector3 pos, Quaternion rot)
    {
        // 1. Cap the old trail at the CURRENT position (Before Move)
        if (TryGetComponent<NetworkTrailMesh>(out var trail))
        {
            trail.NotifyTeleportStart();
        }

        // 2. Perform the Move
        transform.position = pos;
        transform.rotation = rot;
        _pr.Rigidbody.position = pos;
        _pr.Rigidbody.rotation = rot;
        _pr.Rigidbody.linearVelocity = Vector3.zero; 
        _pr.Rigidbody.angularVelocity = Vector3.zero;

        // 3. Start new trail segment at the NEW position (After Move)
        if (trail)
        {
            trail.NotifyTeleportEnd(pos);
        }
    }
}