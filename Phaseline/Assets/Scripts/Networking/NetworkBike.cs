using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;

[RequireComponent(typeof(BikeController))]
[RequireComponent(typeof(PredictionRigidbody))]
public class NetworkBiker : NetworkBehaviour
{
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
        _pr = GetComponent<PredictionRigidbody>();
        _controls = new PlayerControls();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (base.IsOwner)
        {
            _controls.Enable();
            TimeManager.OnTick += TimeManager_OnTick;
        }
        TimeManager.OnPostTick += TimeManager_OnPostTick;
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

        BikeInputData data = new BikeInputData
        {
            MoveInput = _controls.Gameplay.Move.ReadValue<Vector2>(),
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

    // 3. Create Snapshot (Server Only)
    private void TimeManager_OnPostTick()
    {
        if (!base.IsServer) return;

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
        transform.position = data.Position;
        transform.rotation = data.Rotation;
        _pr.Rigidbody.linearVelocity = data.Velocity;
        _pr.Rigidbody.angularVelocity = data.AngularVelocity;
    }

    // API for Portals/Teleports
    public void Teleport(Vector3 pos, Quaternion rot)
    {
        // In FishNet V2 PredictionRigidbody, manual moves often require alerting the system 
        // or effectively "snapping" via a Server RPC that forces a Reconcile.
        // For simplicity in a Replicate method, we act directly, but usually, 
        // we want to ensure this happens during the tick processing.
        
        transform.position = pos;
        transform.rotation = rot;
        _pr.Rigidbody.position = pos;
        _pr.Rigidbody.rotation = rot;
        
        // Clear velocity to prevent "shooting" out of the portal with old momentum
        _pr.Rigidbody.linearVelocity = transform.forward * _pr.Rigidbody.linearVelocity.magnitude; 
        
        // Notify Trail System of the gap
        if (TryGetComponent<NetworkTrailMesh>(out var trail))
        {
            trail.NotifyTeleport(pos);
        }
    }
}