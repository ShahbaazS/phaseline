using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

public class NetworkTrailMesh : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float segmentLength = 0.5f;
    [SerializeField] private float height = 1.2f;
    [SerializeField] private GameObject trailSegmentPrefab; // The TrailSegment.cs prefab
    [SerializeField] private Transform trailEmitter;
    [SerializeField] private Material trailMaterial;

    private struct TrailPoint
    {
        public Vector3 Center;
        public Quaternion Rotation;
        public Vector3 LeftTop, LeftBot, RightTop, RightBot;
        public bool IsJump; // The "Gap" flag
    }

    private LinkedList<TrailPoint> _points = new LinkedList<TrailPoint>();
    private Mesh _mesh;
    private MeshRenderer _mr;
    private MeshFilter _mf;
    private Vector3 _lastPos;

    private void Awake()
    {
        _mesh = new Mesh();
        GameObject meshObj = new GameObject("TrailVisuals");
        _mf = meshObj.AddComponent<MeshFilter>();
        _mr = meshObj.AddComponent<MeshRenderer>();
        _mr.material = trailMaterial;
        _mf.mesh = _mesh;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _lastPos = trailEmitter.position;
    }

    private void FixedUpdate()
    {
        // Generate trail based on movement
        float dist = Vector3.Distance(trailEmitter.position, _lastPos);
        
        if (dist >= segmentLength)
        {
            AddPoint(false);
            
            // If Server, spawn the physical collider
            if (base.IsServer)
            {
                SpawnColliderSegment(_lastPos, trailEmitter.position);
            }

            _lastPos = trailEmitter.position;
        }
    }

    public void NotifyTeleport(Vector3 newPos)
    {
        // 1. Cap off the current line at the old position
        AddPoint(true); // IsJump = true means "Don't connect to next"
        
        // 2. Reset tracking
        _lastPos = newPos;
    }

    private void AddPoint(bool isJump)
    {
        Vector3 pos = trailEmitter.position;
        Quaternion rot = trailEmitter.rotation;
        Vector3 right = rot * Vector3.right * 0.5f; // half width (approx 1m wide)
        Vector3 up = Vector3.up * height;

        TrailPoint p = new TrailPoint
        {
            Center = pos,
            Rotation = rot,
            LeftBot = pos - right,
            RightBot = pos + right,
            LeftTop = pos - right + up,
            RightTop = pos + right + up,
            IsJump = isJump
        };

        _points.AddLast(p);
        if (_points.Count > 200) _points.RemoveFirst();

        RebuildMesh();
    }

    private void SpawnColliderSegment(Vector3 start, Vector3 end)
    {
        Vector3 center = (start + end) * 0.5f + Vector3.up * (height * 0.5f);
        Quaternion rot = Quaternion.LookRotation((end - start).normalized, Vector3.up);
        
        // Instantiate via FishNet
        GameObject go = Instantiate(trailSegmentPrefab, center, rot);
        
        // Set scale length
        float len = Vector3.Distance(start, end);
        go.transform.localScale = new Vector3(1f, height, len); // Adjust width as needed

        Spawn(go, base.Owner); // Give ownership to this biker (optional)
    }

    private void RebuildMesh()
    {
        if (_points.Count < 2) return;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        var node = _points.First;
        while (node.Next != null)
        {
            var p1 = node.Value;
            var p2 = node.Next.Value;

            // If p1 was a jump, we DO NOT connect it to p2
            if (!p1.IsJump)
            {
                int baseIdx = verts.Count;

                // Add vertices
                verts.Add(p1.LeftBot); verts.Add(p1.LeftTop);
                verts.Add(p1.RightBot); verts.Add(p1.RightTop);
                verts.Add(p2.LeftBot); verts.Add(p2.LeftTop);
                verts.Add(p2.RightBot); verts.Add(p2.RightTop);

                // Add Quads (Logic simplified for brevity, similar to original TrailMesh)
                // Left Side
                AddQuad(tris, baseIdx + 0, baseIdx + 1, baseIdx + 5, baseIdx + 4);
                // Right Side
                AddQuad(tris, baseIdx + 3, baseIdx + 2, baseIdx + 6, baseIdx + 7);
                // Top
                AddQuad(tris, baseIdx + 1, baseIdx + 3, baseIdx + 7, baseIdx + 5);
            }

            node = node.Next;
        }

        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateNormals();
    }

    private void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(c); tris.Add(d); tris.Add(a);
    }
}