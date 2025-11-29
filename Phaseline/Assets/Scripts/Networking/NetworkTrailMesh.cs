using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

public class NetworkTrailMesh : NetworkBehaviour
{
    [Header("Anchors (Assign from Bike Model)")]
    [SerializeField] private Transform leftBottom;
    [SerializeField] private Transform leftTop;
    [SerializeField] private Transform rightBottom;
    [SerializeField] private Transform rightTop;

    [Header("Settings")]
    [SerializeField] private float segmentLength = 0.5f;
    [SerializeField] private float height = 1.2f;
    [SerializeField] private GameObject trailSegmentPrefab; 
    [SerializeField] private Transform trailEmitter; 
    [SerializeField] private Material trailMaterial;

    private struct TrailPoint
    {
        public Vector3 LeftTop, LeftBot, RightTop, RightBot;
        public bool IsJump; 
    }

    private LinkedList<TrailPoint> _points = new LinkedList<TrailPoint>();
    private Mesh _mesh;
    private MeshRenderer _mr;
    private MeshFilter _mf;
    private Vector3 _lastPos;
    private GameObject _visualsObj; 
    public bool trailActive = true;
    private List<GameObject> _spawnedSegments = new List<GameObject>();

    private void Awake()
    {
        _mesh = new Mesh();
        _visualsObj = new GameObject("TrailVisuals");
        _visualsObj.transform.SetParent(transform); 
        _visualsObj.transform.localPosition = Vector3.zero;
        
        _mf = _visualsObj.AddComponent<MeshFilter>();
        _mr = _visualsObj.AddComponent<MeshRenderer>();
        _mr.material = trailMaterial;
        _mf.mesh = _mesh;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (trailEmitter) _lastPos = trailEmitter.position;

        if (_visualsObj)
        {
            _visualsObj.transform.SetParent(null);
            _visualsObj.transform.position = Vector3.zero;
            _visualsObj.transform.rotation = Quaternion.identity;
            _visualsObj.transform.localScale = Vector3.one;
            _visualsObj.SetActive(true);
        }

        var colorizer = GetComponentInParent<BikeRandomColor>();
        if (colorizer) colorizer.ApplyToRenderer(_mr);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (_visualsObj)
        {
            _visualsObj.transform.SetParent(transform);
            _visualsObj.SetActive(false);
        }
        ClearTrail();
    }

    private void OnDestroy() { if (_visualsObj) Destroy(_visualsObj); }

    private void FixedUpdate()
    {
        if (!trailActive || !trailEmitter) return;

        float dist = Vector3.Distance(trailEmitter.position, _lastPos);
        if (dist >= segmentLength)
        {
            AddPoint(false); // Add normal segment point
            
            if (base.IsServerInitialized)
                SpawnColliderSegment(_lastPos, trailEmitter.position);

            _lastPos = trailEmitter.position;
        }
    }

    // --- API ---

    // Called BEFORE transform moves (Caps the old line)
    public void NotifyTeleportStart()
    {
        AddPoint(true); // IsJump=true ends the current mesh strip
    }

    // Called AFTER transform moves (Starts the new line)
    public void NotifyTeleportEnd(Vector3 newPos)
    {
        _lastPos = newPos;
        AddPoint(false); // Start new strip immediately at new pos
    }

    // Legacy support if needed
    public void NotifyTeleport(Vector3 newPos) => NotifyTeleportEnd(newPos);

    public void PauseTrail() => trailActive = false;

    public void ResumeTrail()
    {
        trailActive = true;
        if (trailEmitter)
        {
            _lastPos = trailEmitter.position;
            AddPoint(false); // FIX: Anchor the trail immediately to fill the start gap
        }
    }
    
    public void ResumeTrailAt(Vector3 pos)
    {
        trailActive = true;
        _lastPos = pos;
        // Assume transform is already at pos, so AddPoint captures correct anchors
        AddPoint(false); 
    }

    public void ClearTrail()
    {
        trailActive = false;
        _points.Clear();
        _mesh.Clear();
        
        if (base.IsServerInitialized)
        {
            foreach (var go in _spawnedSegments) { if (go != null) base.Despawn(go); }
            _spawnedSegments.Clear();
        }
        
        if (trailEmitter) _lastPos = trailEmitter.position;
    }

    // --- Internal Logic ---

    private void AddPoint(bool isJump)
    {
        TrailPoint p = new TrailPoint
        {
            LeftBot = leftBottom.position,
            LeftTop = leftTop.position,
            RightBot = rightBottom.position,
            RightTop = rightTop.position,
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
        GameObject go = Instantiate(trailSegmentPrefab, center, rot);
        go.transform.localScale = new Vector3(1f, height, Vector3.Distance(start, end)); 
        base.Spawn(go, base.Owner);
        _spawnedSegments.Add(go);
    }

    private void RebuildMesh()
    {
        if (_points.Count < 2) return;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        // 1. BACK FACE CAP (Fill the start hole)
        // Uses the very first point's geometry
        var startPoint = _points.First.Value;
        // Only draw cap if the start isn't a "Jump" (unlikely for first point, but safe)
        if (!startPoint.IsJump) 
        {
            int capIdx = verts.Count;
            verts.Add(startPoint.LeftBot);  // 0
            verts.Add(startPoint.LeftTop);  // 1
            verts.Add(startPoint.RightBot); // 2
            verts.Add(startPoint.RightTop); // 3

            // Winding: BL(0) -> BR(2) -> TR(3) -> TL(1) 
            // Right x Up = Backward (-Z) -> Normal points OUT
            AddQuad(tris, capIdx + 0, capIdx + 1, capIdx + 3, capIdx + 2);
        }

        // 2. SEGMENTS
        var node = _points.First;
        while (node.Next != null)
        {
            var p1 = node.Value;
            var p2 = node.Next.Value;

            if (!p1.IsJump)
            {
                int baseIdx = verts.Count;
                verts.Add(p1.LeftBot);  // 0
                verts.Add(p1.LeftTop);  // 1
                verts.Add(p1.RightBot); // 2
                verts.Add(p1.RightTop); // 3
                verts.Add(p2.LeftBot);  // 4
                verts.Add(p2.LeftTop);  // 5
                verts.Add(p2.RightBot); // 6
                verts.Add(p2.RightTop); // 7

                // Left Side (Outward): BL -> FL -> FT -> BT
                AddQuad(tris, baseIdx + 0, baseIdx + 4, baseIdx + 5, baseIdx + 1); 

                // Right Side (Outward): TR -> FR -> FB -> BB
                AddQuad(tris, baseIdx + 3, baseIdx + 7, baseIdx + 6, baseIdx + 2); 
                
                // Top Face (Upward): TL -> FL -> FR -> TR
                AddQuad(tris, baseIdx + 1, baseIdx + 5, baseIdx + 7, baseIdx + 3); 
            }
            node = node.Next;
        }

        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds(); 
    }

    private void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(c); tris.Add(d); tris.Add(a);
    }
}