using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Trail: dynamic mesh (visual) + pooled collider segments (trigger).
/// Visuals are separate from colliders. Uses four anchors + an emitter (rear-center).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class TrailMesh : MonoBehaviour
{
    [Header("Trail Settings")]
    [SerializeField] private float segmentLength = 0.5f;
    [SerializeField] private float trailWidth    = 1.4f;
    [SerializeField] private float trailHeight   = 1.2f;

    [Header("Length Limits")]
    [Tooltip("Maximum number of visual mesh segments kept in memory.")]
    [SerializeField] private int maxMeshSegments = 200;
    [Tooltip("Maximum number of collider segments kept alive at once.")]
    [SerializeField] private int maxColliderSegments = 200;

    [Header("Prefabs & References")]
    [Tooltip("Trigger collider prefab on layer Trail with TrailCollision script.")]
    [SerializeField] private GameObject colliderPrefab;
    [Tooltip("Material for the trail mesh.")]
    [SerializeField] private Material trailMaterial;
    [Tooltip("Transform that emits the trail points (rear-center near ground).")]
    [SerializeField] private Transform trailEmitter;
    [Tooltip("Anchor points defining mesh corners on each segment.")]
    [SerializeField] private TrailAnchorPoints trailAnchors;

    [Header("Owner (for TrailCollision self-ignore)")]
    [SerializeField] private Transform ownerRoot;

    // mesh + data
    private Mesh trailMesh;
    private LinkedList<TrailPointData> points = new();

    // pooled collider list
    private readonly List<GameObject> activeColliders = new();

    // list pools to avoid GC
    private ObjectPool<List<Vector3>> vertsPool;
    private ObjectPool<List<int>> trisPool;

    // state
    private Vector3 lastPos;
    public  bool trailActive = true;

    [Serializable]
    private class TrailAnchorPoints
    {
        public Transform leftBottom;
        public Transform leftTop;
        public Transform rightBottom;
        public Transform rightTop;
    }

    private struct TrailPointData
    {
        public Vector3 leftBottom, leftTop, rightBottom, rightTop;
    }

    void Awake()
    {
        if (!ownerRoot) ownerRoot = transform;

        vertsPool = new ObjectPool<List<Vector3>>(
            () => new List<Vector3>((maxMeshSegments + 1) * 4),
            l => l.Clear(),
            null, null, false, 2, 10
        );
        trisPool = new ObjectPool<List<int>>(
            () => new List<int>(maxMeshSegments * 6),
            l => l.Clear(),
            null, null, false, 2, 10
        );
    }

    void Start()
    {
        // Create a single visual mesh object (no pooling needed for this)
        var go = new GameObject($"{name}_TrailMesh");
        go.transform.SetParent(null);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = trailMaterial;
        trailMesh = new Mesh();
        mf.sharedMesh = trailMesh;

        var colorizer = GetComponentInParent<BikeRandomColor>();
        if (colorizer)
        {
            // If your BikeRandomColor has Apply() that tints all children,
            // keeping the renderer as a child now is enough:
            colorizer.ApplyToRenderer(mr);

            // (Optional) if you add an API to tint a specific renderer, call it here:
            // colorizer.ApplyToRenderer(mr);
        }

        lastPos = trailEmitter.position;
    }

    void FixedUpdate()
    {
        if (!trailActive) return;

        var curr = trailEmitter.position;
        if (Vector3.Distance(lastPos, curr) >= segmentLength)
        {
            // 1) Colliders (pooled)
            SpawnColliderSegment(lastPos, curr);

            // 2) Visuals: record anchors then rebuild the single mesh
            AddTrailPoint();
            lastPos = curr;
        }
    }

    void AddTrailPoint()
    {
        var p = new TrailPointData
        {
            leftBottom  = trailAnchors.leftBottom.position,
            leftTop     = trailAnchors.leftTop.position,
            rightBottom = trailAnchors.rightBottom.position,
            rightTop    = trailAnchors.rightTop.position
        };
        points.AddLast(p);
        if (points.Count > maxMeshSegments + 1)
            points.RemoveFirst();

        RebuildMesh();
    }

    void SpawnColliderSegment(Vector3 from, Vector3 to)
    {
        if (!colliderPrefab || !ObjectPool.Instance)
            return;

        if (activeColliders.Count >= maxColliderSegments)
        {
            var old = activeColliders[0];
            ObjectPool.Instance.Return(old, colliderPrefab);
            activeColliders.RemoveAt(0);
        }

        // Center at mid of from/to, raised by half height; length along forward
        Vector3 center = (from + to) * 0.5f + Vector3.up * (trailHeight * 0.5f);
        float len = Mathf.Min(segmentLength * 1.5f, Vector3.Distance(from, to));
        var rot  = Quaternion.LookRotation((to - from).normalized, Vector3.up);

        var go = ObjectPool.Instance.Get(colliderPrefab, center, rot);
        go.transform.localScale = new Vector3(trailWidth, trailHeight, len);

        // Set trail owner for self-ignore
        var tc = go.GetComponent<TrailCollision>();
        if (tc) tc.ownerRoot = ownerRoot;

        activeColliders.Add(go);
    }

    void RebuildMesh()
    {
        if (!trailMesh) return;
        if (points.Count < 2)
        {
            trailMesh.Clear();
            return;
        }

        var verts = vertsPool.Get();
        var tris  = trisPool.Get();

        // First “back face” quad (closing start)
        var node = points.First;
        AddQuad(verts, node.Value.leftBottom, node.Value.leftTop, node.Value.rightBottom, node.Value.rightTop);

        // For each pair, stitch faces:
        while (node.Next != null)
        {
            var a = node.Value;
            var b = node.Next.Value;

            // Left side face (prev LB/LT to curr LB/LT)
            AddQuad(verts, b.leftBottom, b.leftTop, a.leftBottom, a.leftTop);
            // Right side face
            AddQuad(verts, a.rightBottom, a.rightTop, b.rightBottom, b.rightTop);
            // Top face
            AddQuad(verts, a.leftTop, b.leftTop, a.rightTop, b.rightTop);

            node = node.Next;
        }

        // Triangles for all quads
        for (int i = 0; i < verts.Count; i += 4)
        {
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i + 2); tris.Add(i + 1); tris.Add(i + 3);
        }

        trailMesh.Clear();
        trailMesh.SetVertices(verts);
        trailMesh.SetTriangles(tris, 0);
        trailMesh.RecalculateNormals(); // simple lighting; skip if using Unlit

        vertsPool.Release(verts);
        trisPool.Release(tris);
    }

    static void AddQuad(List<Vector3> v, Vector3 bl, Vector3 tl, Vector3 br, Vector3 tr)
    {
        v.Add(bl); v.Add(tl); v.Add(br); v.Add(tr);
    }

    // --------- Public control ---------

    public void ClearTrail()
    {
        trailActive = false;

        // Return colliders
        if (ObjectPool.Instance)
        {
            foreach (var go in activeColliders)
                ObjectPool.Instance.Return(go, colliderPrefab);
        }
        activeColliders.Clear();

        points.Clear();
        if (trailMesh) trailMesh.Clear();

        lastPos = trailEmitter ? trailEmitter.position : transform.position;
    }

    public void ResumeTrail()
    {
        lastPos = trailEmitter ? trailEmitter.position : transform.position;
        trailActive = true;
    }
}
