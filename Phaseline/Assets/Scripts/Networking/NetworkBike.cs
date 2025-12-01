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
        // Pass PredictionRigidbody the RB to manage
        _pr.Initialize(GetComponent<Rigidbody>());
        _controls = new PlayerControls();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        // 1. Setup Input & Camera for Owner
        if (base.Owner.IsLocalClient)
        {
            _controls.Enable();
            
            var cam = Camera.main?.GetComponent<FollowCamera>();
            if (cam != null) cam.target = transform;
        }

        // 2. Physics Setup
        // Enable physics simulation if we are the Owner (Prediction) or the Server (Authority)
        // Observers will have kinematic rigidbodies and just snap to position via Reconcile
        bool isPhysicsActive = base.Owner.IsLocalClient || base.IsServerInitialized;
        _pr.Rigidbody.isKinematic = !isPhysicsActive;

        // 3. Subscriptions
        if (base.IsServerInitialized || base.IsClientInitialized)
        {
            TimeManager.OnTick += TimeManager_OnTick;
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
    
    // VISUALS LOOP
    private void Update()
    {
        // Smoothly interpolate visuals every frame based on the physics state
        // We pass Time.deltaTime here because this is for interpolation, not physics steps
        _controller.UpdateVisuals(Time.deltaTime);
    }

    // 1. Gather Input (Runs on Client and Server)
    private void TimeManager_OnTick()
    {
        if (base.IsOwner)
        {
            // Read Inputs
            Vector2 rawInput = _controls.Gameplay.Move.ReadValue<Vector2>();
            float throttle = Mathf.Clamp01(rawInput.y);
            throttle = Mathf.Max(throttle, minThrottle);

            BikeInputData data = new BikeInputData
            {
                MoveInput = new Vector2(rawInput.x, throttle),
                Drift = _controls.Gameplay.Drift.IsPressed(),
                Boost = _controls.Gameplay.Boost.IsPressed(),
                Jump = _controls.Gameplay.Jump.WasPressedThisFrame() 
            };

            // Send to server / Run locally
            Replicate(data);
        }
        else if (base.IsServerInitialized)
        {
            // Server calls Replicate with default/empty data, 
            // FishNet automatically fills it with the client's packet
            Replicate(default);
        }
    }

    // 2. Run Physics Logic (Prediction)
    [Replicate]
    private void Replicate(BikeInputData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        // Use FishNet's TickDelta for consistent physics steps
        float dt = (float)base.TimeManager.TickDelta;
        
        // Call the PURE physics method
        _controller.Simulate(data.MoveInput, data.Drift, data.Boost, data.Jump, dt);
    }

    // 3. Create State Snapshot (Server Only)
    private void TimeManager_OnPostTick()
    {
        if (base.IsServerInitialized)
        {
            CreateReconcile();
        }
    }

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

    // 4. Correct Mistakes (Client Only)
    [Reconcile]
    private void Reconcile(BikeReconcileData data, Channel channel = Channel.Unreliable)
    {
        // Snap Transform
        transform.position = data.Position;
        transform.rotation = data.Rotation;
        
        // Snap Physics
        _pr.Rigidbody.linearVelocity = data.Velocity;
        _pr.Rigidbody.angularVelocity = data.AngularVelocity;
    }
    
    // Server-side entry point for Portals and Spawns
    public void Teleport(Vector3 pos, Quaternion rot)
    {
        // 1. Move on Server immediately (so physics/checks work)
        TeleportInternal(pos, rot);

        // 2. Tell all Clients (especially the Owner) to force this position
        // BufferLast = true ensures that if a client joins later, they get the latest position.
        ObserversTeleport(pos, rot);
    }

    // Logic shared by Server and Client
    private void TeleportInternal(Vector3 pos, Quaternion rot)
    {
        // Handle trail cutting (Visuals)
        if (TryGetComponent<NetworkTrailMesh>(out var trail)) trail.NotifyTeleportStart();

        // Physically move transform and Rigidbody
        transform.position = pos;
        transform.rotation = rot;
        
        // CRITICAL: Reset Physics State
        // If we don't zero velocity, the bike might "launch" out of the spawn point
        _pr.Rigidbody.position = pos;
        _pr.Rigidbody.rotation = rot;
        _pr.Rigidbody.linearVelocity = Vector3.zero; 
        _pr.Rigidbody.angularVelocity = Vector3.zero;

        if (TryGetComponent<FishNet.Component.Transforming.NetworkTransform>(out var nt))
        {
            nt.Teleport(); 
        }

        // Resume trail (Visuals)
        if (trail) trail.NotifyTeleportEnd(pos);
    }

    [ObserversRpc(BufferLast = true)]
    private void ObserversTeleport(Vector3 pos, Quaternion rot)
    {
        // Clients run this to snap to the new point
        TeleportInternal(pos, rot);
        
        // CRITICAL: Clear Prediction Buffer (Client Owner Only)
        // This prevents the client from replaying "old" inputs that happened before death
        if (IsOwner)
        {
            // If using FishNet 4.6+ Prediction V2, we may need to acknowledge the reset
            // Usually setting the RB state is enough, but ensuring controls don't jitter is good.
             _pr.Rigidbody.linearVelocity = Vector3.zero;
        }
    }
}