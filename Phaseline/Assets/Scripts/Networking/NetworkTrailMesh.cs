using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class NetworkTrailMesh : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float segmentLength = 0.5f;
    [SerializeField] private float trailHeight = 1.5f;
    [SerializeField] private float trailWidth = 0.2f;
    [SerializeField] private int maxSegments = 150;

    [Header("References")]
    [SerializeField] private Transform trailEmitter; // The back wheel transform
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private NetworkObject wallSegmentPrefab; // The invisible collider prefab

    // Data Structure for a single point in the trail
    struct TrailPoint
    {
        public Vector3 Center;
        public Quaternion Rotation;
        public bool IsJump; // If true, this point is the start of a new line (gap)
    }

    private LinkedList<TrailPoint> points = new LinkedList<TrailPoint>();
    private Vector3 lastSpawnPosition;
    private Mesh mesh;
    private bool isSpawning = false;

    // Object Pooling for Mesh Generation (Reduces Garbage Collection)
    private List<Vector3> verts = new List<Vector3>();
    private List<int> tris = new List<int>();
    private List<Vector2> uvs = new List<Vector2>();

    private void Awake()
    {
        mesh = new Mesh();
        meshFilter.mesh = mesh;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        // Start spawning only when the network starts
        if (trailEmitter != null)
        {
            lastSpawnPosition = trailEmitter.position;
            isSpawning = true;
            
            // Add initial point
            AddPoint(trailEmitter.position, trailEmitter.rotation, false);
        }
    }

    private void FixedUpdate()
    {
        if (!isSpawning || trailEmitter == null) return;

        // 1. Check distance moved
        if (Vector3.Distance(lastSpawnPosition, trailEmitter.position) > segmentLength)
        {
            // 2. Add Visual Point (Client & Server)
            AddPoint(trailEmitter.position, trailEmitter.rotation, false);
            
            // 3. Spawn Physical Wall (Server Only)
            if (IsServer)
            {
                SpawnWallCollider(lastSpawnPosition, trailEmitter.position);
            }

            lastSpawnPosition = trailEmitter.position;
        }
    }

    // --- Public API called by NetworkBike ---

    public void NotifyTeleport(Vector3 newPosition)
    {
        // 1. Finish the current line at the OLD position
        AddPoint(trailEmitter.position, trailEmitter.rotation, true); 

        // 2. Reset our tracking to the NEW position
        lastSpawnPosition = newPosition;
        
        // The next FixedUpdate will see a large distance, but since we updated
        // lastSpawnPosition, it will start a fresh segment from the new spot.
    }

    public void ResetTrail()
    {
        points.Clear();
        mesh.Clear();
        lastSpawnPosition = trailEmitter.position;
    }

    // --- Internal Logic ---

    private void AddPoint(Vector3 pos, Quaternion rot, bool isJump)
    {
        var p = new TrailPoint
        {
            Center = pos,
            Rotation = rot,
            IsJump = isJump
        };

        points.AddLast(p);

        // Keep list size in check
        if (points.Count > maxSegments)
        {
            points.RemoveFirst();
        }

        UpdateMesh();
    }

    private void UpdateMesh()
    {
        if (points.Count < 2) return;

        verts.Clear();
        tris.Clear();
        uvs.Clear();

        var node = points.First;
        int vertIndex = 0;

        // Iterate through linked list pairs
        while (node != null && node.Next != null)
        {
            TrailPoint pA = node.Value;
            TrailPoint pB = node.Next.Value;

            // CRITICAL: If 'pA' was a jump point, do NOT connect it to 'pB'
            if (pA.IsJump)
            {
                node = node.Next;
                continue;
            }

            // Calculate Quad Vertices
            Vector3 up = Vector3.up * trailHeight;
            
            // We use the rotation to find the "thickness" direction if desired, 
            // but for a Tron wall, a simple vertical sheet usually works best.
            
            Vector3 pA_Bottom = pA.Center;
            Vector3 pA_Top = pA.Center + up;
            Vector3 pB_Bottom = pB.Center;
            Vector3 pB_Top = pB.Center + up;

            verts.Add(pA_Bottom); // 0
            verts.Add(pA_Top);    // 1
            verts.Add(pB_Bottom); // 2
            verts.Add(pB_Top);    // 3

            // UVs
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(0, 1));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));

            // Triangles (Double Sided)
            tris.Add(vertIndex + 0); tris.Add(vertIndex + 1); tris.Add(vertIndex + 2);
            tris.Add(vertIndex + 2); tris.Add(vertIndex + 1); tris.Add(vertIndex + 3);
            
            tris.Add(vertIndex + 0); tris.Add(vertIndex + 2); tris.Add(vertIndex + 1);
            tris.Add(vertIndex + 2); tris.Add(vertIndex + 3); tris.Add(vertIndex + 1);

            vertIndex += 4;
            node = node.Next;
        }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private void SpawnWallCollider(Vector3 start, Vector3 end)
    {
        // FishNet Spawning Logic
        Vector3 center = (start + end) / 2;
        center.y += trailHeight / 2; // Raise center to match height
        
        Quaternion rot = Quaternion.LookRotation(end - start);
        float length = Vector3.Distance(start, end);

        // Retrieve from Pool (Recommended) or Instantiate
        NetworkObject wall = Instantiate(wallSegmentPrefab, center, rot);
        wall.transform.localScale = new Vector3(trailWidth, trailHeight, length);
        
        // Spawn on network
        InstanceFinder.ServerManager.Spawn(wall);
        
        // Set Owner so we don't kill ourselves (Optional, if Wall has logic)
        // wall.GetComponent<TrailCollision>().OwnerId = OwnerId;
    }
}