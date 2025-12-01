using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;
using FishNet;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Transporting;
using FishNet.Component.Transforming;

public class SpawnManager : NetworkBehaviour 
{
    public static SpawnManager Instance { get; private set; }

    [Header("Prefabs")]
    public GameObject playerBikePrefab;
    public GameObject botBikePrefab;
    public int botCount = 3;

    [Header("Spawn")]
    public string spawnTag = "Respawn";
    public float respawnDelay = 1.25f;

    private readonly List<Transform> points = new();
    private int nextIdx;
    
    private readonly Dictionary<Damageable, (GameObject go, bool isPlayer)> registry = new();

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        foreach (var go in GameObject.FindGameObjectsWithTag(spawnTag))
            points.Add(go.transform);
        if (points.Count == 0) Debug.LogWarning("[SpawnManager] No Respawn points found.");
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        for (int i = 0; i < botCount; i++)
            Spawn(botBikePrefab, out _, false, null);

        foreach (var conn in InstanceFinder.ServerManager.Clients.Values)
        {
            SpawnPlayer(conn);
        }

        InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;

        RefreshOpponents();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (InstanceFinder.ServerManager != null)
            InstanceFinder.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
    }

    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            SpawnPlayer(conn);
            RefreshOpponents();
        }
    }

    void SpawnPlayer(NetworkConnection conn)
    {
        Spawn(playerBikePrefab, out var dmg, true, conn);
    }

    Transform NextPoint() => points.Count > 0 ? points[(nextIdx++) % points.Count] : null;

    GameObject Spawn(GameObject prefab, out Damageable dmg, bool isPlayer, NetworkConnection owner)
    {
        var sp = NextPoint();
        var go = Instantiate(prefab, sp ? sp.position : Vector3.zero, sp ? sp.rotation : Quaternion.identity);
        
        InstanceFinder.ServerManager.Spawn(go, owner);

        go.TryGetComponent(out dmg);
        if (!dmg) dmg = go.AddComponent<Damageable>();
        
        var prot = go.GetComponent<SpawnProtection>();
        if (prot) prot.EnableFor(2f);
        
        registry[dmg] = (go, isPlayer);
        
        return go;
    }

    public void HandleDeath(Damageable dmg)
    {
        if (!IsServerInitialized) return;

        if (!registry.TryGetValue(dmg, out var info)) return;
        var go = info.go;

        // Hide object while dead
        go.SetActive(false); 
        StartCoroutine(CoRespawn(dmg, info.isPlayer));
    }

    System.Collections.IEnumerator CoRespawn(Damageable dmg, bool isPlayer)
    {
        yield return new WaitForSeconds(respawnDelay);

        if (!registry.TryGetValue(dmg, out var info)) yield break;
        var go = info.go;

        // 1. Determine Spawn Point
        var sp = NextPoint();
        Vector3 spawnPos = sp ? sp.position : Vector3.zero;
        Quaternion spawnRot = sp ? sp.rotation : Quaternion.identity;

        // 2. ACTIVATE FIRST (Critical Fix)
        go.transform.SetPositionAndRotation(spawnPos, spawnRot);
        go.SetActive(true);
        dmg.Revive();

        // 3. Clear Trail
        if (go.TryGetComponent<NetworkTrailMesh>(out var trail)) 
            trail.ClearTrail();

        // 4. Teleport
        if (go.TryGetComponent<NetworkBike>(out var bike))
            bike.Teleport(spawnPos, spawnRot);

        // 5. Reset other systems
        if (go.TryGetComponent<SpawnProtection>(out var prot)) prot.EnableFor(2f);
        if (go.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent)) agent.Warp(spawnPos);

        RefreshOpponents();
    }

    void RefreshOpponents()
    {
        var allRiders = registry.Values
            .Where(v => v.go != null)
            .Select(v => v.go.transform)
            .ToList();

        foreach (var kv in registry)
        {
            if (kv.Value.go == null) continue;
            var ai = kv.Value.go.GetComponent<BotController>();
            if (ai) ai.opponents = allRiders.Where(t => t != kv.Value.go.transform).ToArray();
        }
    }
}