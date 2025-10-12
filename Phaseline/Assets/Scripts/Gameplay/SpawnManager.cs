using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class SpawnManager : MonoBehaviour
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
    private readonly Dictionary<Damageable,(GameObject go, bool isPlayer)> registry = new();

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        foreach (var go in GameObject.FindGameObjectsWithTag(spawnTag))
            points.Add(go.transform);
        if (points.Count == 0) Debug.LogWarning("[SpawnManager] No Respawn points found.");
    }

    void Start()
    {
        // Spawn Player
        var p = Spawn(playerBikePrefab, out var dmgP, true);

        var cam = Camera.main?.GetComponent<FollowCamera>();
        if (cam) cam.target = registry.First(kv => kv.Value.isPlayer).Value.go.transform;

        // Spawn Bots
        for (int i = 0; i < botCount; i++)
            Spawn(botBikePrefab, out _, false);

        // Fill opponents for all bots
        RefreshOpponents();
    }

    Transform NextPoint() => points.Count > 0 ? points[(nextIdx++) % points.Count] : null;

    GameObject Spawn(GameObject prefab, out Damageable dmg, bool isPlayer)
    {
        var sp = NextPoint();
        var go = Instantiate(prefab, sp ? sp.position : Vector3.zero, sp ? sp.rotation : Quaternion.identity);
        go.TryGetComponent(out dmg);
        if (!dmg) dmg = go.AddComponent<Damageable>();
        var prot = go.GetComponent<SpawnProtection>();
        if (prot) prot.EnableFor(2f);
        registry[dmg] = (go, isPlayer);
        return go;
    }

    public void HandleDeath(Damageable dmg)
    {
        if (!registry.TryGetValue(dmg, out var info)) return;
        var go = info.go;

        // Clear trail if present
        var trail = go.GetComponent<TrailMesh>();
        if (trail) trail.ClearTrail();

        // Hide body, then respawn
        go.SetActive(false);
        StartCoroutine(CoRespawn(dmg, info.isPlayer));
    }

    System.Collections.IEnumerator CoRespawn(Damageable dmg, bool isPlayer)
    {
        // wait out the death delay
        yield return new WaitForSeconds(respawnDelay);

        // safety: the entry might have been removed
        if (!registry.TryGetValue(dmg, out var info))
            yield break;

        var go = info.go;

        // clear any leftover trail before reactivating so we don't spawn-kill
        if (go.TryGetComponent<TrailMesh>(out var trailPooled)) trailPooled.ClearTrail();
        else if (go.TryGetComponent<TrailMesh>(out var trailSimple))  trailSimple.ClearTrail();

        // place at next spawn
        var sp = NextPoint();
        go.transform.SetPositionAndRotation(
            sp ? sp.position : Vector3.zero,
            sp ? sp.rotation : Quaternion.identity
        );

        // reset physics
        if (go.TryGetComponent<Rigidbody>(out var rb))
        {
    #if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
    #else
            rb.velocity = Vector3.zero;
    #endif
            rb.angularVelocity = Vector3.zero;
        }

        // revive flag first
        dmg.Revive();

        // reactivate first so coroutines/components can run
        go.SetActive(true);

        if (go.TryGetComponent<TrailMesh>(out var trail))
            trail.ResumeTrail();

        // now enable spawn protection
        if (go.TryGetComponent<SpawnProtection>(out var prot))
            prot.EnableFor(2f);

        // rebuild bot opponent lists if you do that here
        RefreshOpponents();
    }

    void RefreshOpponents()
    {
        var allRiders = registry.Values.Select(v => v.go.transform).ToList();
        foreach (var kv in registry)
        {
            var ai = kv.Value.go.GetComponent<BotController>();
            if (!ai) continue;
            ai.opponents = allRiders.Where(t => t != kv.Value.go.transform).ToArray();
        }
    }
}
