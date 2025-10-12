using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Simple per-prefab GameObject pool.
/// Keeps objects inactive when returned; reactivates on Get.
/// </summary>
public class ObjectPool : MonoBehaviour
{
    public static ObjectPool Instance { get; private set; }

    // Optional: prewarm on Awake
    [System.Serializable]
    public class PrewarmEntry { public GameObject prefab; public int count = 8; }
    public List<PrewarmEntry> prewarm = new();

    private readonly Dictionary<GameObject, ObjectPool<GameObject>> pools = new();

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Prewarm if asked
        foreach (var e in prewarm)
        {
            if (!e.prefab) continue;
            var pool = GetPool(e.prefab);
            for (int i = 0; i < e.count; i++)
            {
                var go = pool.Get();
                pool.Release(go);
            }
        }
    }

    ObjectPool<GameObject> GetPool(GameObject prefab)
    {
        if (!pools.TryGetValue(prefab, out var pool))
        {
            pool = new ObjectPool<GameObject>(
                createFunc: () => Instantiate(prefab),
                actionOnGet: go => go.SetActive(true),
                actionOnRelease: go => go.SetActive(false),
                actionOnDestroy: go => Destroy(go),
                collectionCheck: false,
                defaultCapacity: 16,
                maxSize: 512
            );
            pools.Add(prefab, pool);
        }
        return pool;
    }

    public GameObject Get(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        var pool = GetPool(prefab);
        var go = pool.Get();
        go.transform.SetPositionAndRotation(pos, rot);
        return go;
    }

    public void Return(GameObject instance, GameObject prefab)
    {
        if (!instance || !prefab) return;
        if (!pools.TryGetValue(prefab, out var pool)) { Destroy(instance); return; }
        pool.Release(instance);
    }
}
