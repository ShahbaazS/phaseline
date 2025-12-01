using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

public class NetworkTrailMesh : NetworkBehaviour
{
    [Header("Anchors")]
    [SerializeField] private Transform leftBottom;
    [SerializeField] private Transform leftTop;
    [SerializeField] private Transform rightBottom;
    [SerializeField] private Transform rightTop;

    [Header("Settings")]
    [SerializeField] private float segmentLength = 0.5f;
    [SerializeField] private float height = 1.2f;
    [Tooltip("Maximum number of segments before the tail disappears.")]
    [SerializeField] private int maxSegmentCount = 100;
    [Tooltip("If the bike moves further than this in one frame, cut the line instead of drawing.")]
    [SerializeField] private float maxDrawDistance = 5.0f;
    [SerializeField] private GameObject trailSegmentPrefab; 
    [SerializeField] private Transform trailEmitter; 
    [SerializeField] private Material trailMaterial;

    private struct TrailPoint { public Vector3 LeftTop, LeftBot, RightTop, RightBot; public bool IsJump; }

    private LinkedList<TrailPoint> _points = new LinkedList<TrailPoint>();
    private Mesh _mesh;
    private MeshRenderer _mr;
    private MeshFilter _mf;
    private Vector3 _lastPos;
    private GameObject _visualsObj; 
    public bool trailActive = true;
    
    private List<GameObject> _spawnedSegments = new List<GameObject>();
    
    // Cache the protection component
    private SpawnProtection _protection;

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

        // Find the protection component (works for Player AND Bots)
        _protection = GetComponent<SpawnProtection>();
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
    
    private void OnEnable()
    {
        if (trailEmitter) _lastPos = trailEmitter.position;

        // Subscribe to the event
        if (_protection) _protection.OnProtectionEnded += OnProtectionEnded;
    }
    
    private void OnDisable()
    {
        // Unsubscribe
        if (_protection) _protection.OnProtectionEnded -= OnProtectionEnded;

        // Force clear locally immediately when disabled (Death)
        ClearTrailInternal(); 
    }

    // This event fires when SpawnProtection finishes (2 seconds after spawn)
    private void OnProtectionEnded()
    {
        // Only the Server should initiate the Resume command to keep everyone in sync
        if (IsServerInitialized)
        {
            ResumeTrail(transform.position);
        }
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

        // FAILSAFE: If distance is too huge (Teleport/Lag), cut the line
        if (dist > maxDrawDistance)
        {
            _lastPos = trailEmitter.position;
            AddPoint(true);
            return;
        }

        if (dist >= segmentLength)
        {
            AddPoint(false);
            
            if (base.IsServerInitialized)
                SpawnColliderSegment(_lastPos, trailEmitter.position);

            _lastPos = trailEmitter.position;
        }
    }

    // --- API ---

    public void NotifyTeleportStart() => AddPoint(true);
    public void NotifyTeleportEnd(Vector3 newPos) { _lastPos = newPos; AddPoint(false); }

    public void ResumeTrail(Vector3 startPos)
    {
        ResumeTrailInternal(startPos);
        if (base.IsServerInitialized) RpcResumeTrail(startPos);
    }

    [ObserversRpc]
    private void RpcResumeTrail(Vector3 startPos) => ResumeTrailInternal(startPos);

    private void ResumeTrailInternal(Vector3 startPos)
    {
        trailActive = true;
        _lastPos = startPos;
        AddPoint(false); 
    }

    public void ClearTrail()
    {
        ClearTrailInternal();
        if (base.IsServerInitialized) RpcClearTrail();
    }

    [ObserversRpc]
    private void RpcClearTrail() => ClearTrailInternal();

    private void ClearTrailInternal()
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
            LeftBot = leftBottom.position, LeftTop = leftTop.position,
            RightBot = rightBottom.position, RightTop = rightTop.position,
            IsJump = isJump
        };

        _points.AddLast(p);
        if (_points.Count > maxSegmentCount + 2) _points.RemoveFirst();
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

        if (_spawnedSegments.Count > maxSegmentCount)
        {
            GameObject oldSegment = _spawnedSegments[0];
            _spawnedSegments.RemoveAt(0);
            if (oldSegment != null) base.Despawn(oldSegment);
        }
    }

    private void RebuildMesh()
    {
        if (_points.Count < 2) return;
        // ... (Standard mesh generation logic remains unchanged) ...
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        var startPoint = _points.First.Value;
        if (!startPoint.IsJump) 
        {
            int capIdx = verts.Count;
            verts.Add(startPoint.LeftBot); verts.Add(startPoint.LeftTop); verts.Add(startPoint.RightBot); verts.Add(startPoint.RightTop);
            AddQuad(tris, capIdx + 0, capIdx + 1, capIdx + 3, capIdx + 2);
        }

        var node = _points.First;
        while (node.Next != null)
        {
            var p1 = node.Value;
            var p2 = node.Next.Value;

            if (!p1.IsJump)
            {
                int baseIdx = verts.Count;
                verts.Add(p1.LeftBot); verts.Add(p1.LeftTop); verts.Add(p1.RightBot); verts.Add(p1.RightTop);
                verts.Add(p2.LeftBot); verts.Add(p2.LeftTop); verts.Add(p2.RightBot); verts.Add(p2.RightTop);

                AddQuad(tris, baseIdx + 0, baseIdx + 4, baseIdx + 5, baseIdx + 1); 
                AddQuad(tris, baseIdx + 3, baseIdx + 7, baseIdx + 6, baseIdx + 2); 
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
        tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(c); tris.Add(d); tris.Add(a);
    }
}