using FishNet.Object.Prediction;
using UnityEngine;

public struct BikeInputData : IReplicateData
{
    public Vector2 Move;
    public bool Drift;
    public bool Boost;
    public bool Jump;

    // --- FishNet 4.6 Interface Requirement ---
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

    // --- FishNet 4.6 Interface Requirement ---
    private uint _tick;
    public void Dispose() { }
    public uint GetTick() => _tick;
    public void SetTick(uint value) => _tick = value;
    
    public BikeReconcileData(Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel)
    {
        Position = pos;
        Rotation = rot;
        Velocity = vel;
        AngularVelocity = angVel;
        _tick = 0;
    }
}