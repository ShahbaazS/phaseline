using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Pool;

public class NetworkObjectPool : NetworkBehaviour
{
    public static NetworkObjectPool Instance { get; private set; }

    [System.Serializable]
    struct PoolConfig
    {
        public GameObject Prefab;      // must have a NetworkObject
        public int         Prewarm;    // e.g. your maxColliderSegments
    }

    [SerializeField] PoolConfig[] configs;

    // maps prefab to pool
    private readonly Dictionary<GameObject, ObjectPool<NetworkObject>> pools
        = new Dictionary<GameObject, ObjectPool<NetworkObject>>();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        foreach (var cfg in configs)
            Register(cfg.Prefab, cfg.Prewarm);
    }

    public override void OnNetworkDespawn()
    {
        foreach (var kv in pools)
        {
            NetworkManager.Singleton.PrefabHandler.RemoveHandler(kv.Key);
            kv.Value.Clear();
        }
        pools.Clear();
    }

    private void Register(GameObject prefab, int prewarm)
    {
        // configure the ObjectPool<NetworkObject>
        ObjectPool<NetworkObject> pool = new ObjectPool<NetworkObject>(
            createFunc: () => Instantiate(prefab).GetComponent<NetworkObject>(),
            actionOnGet: no => no.gameObject.SetActive(true),
            actionOnRelease: no => no.gameObject.SetActive(false),
            actionOnDestroy: no => Destroy(no.gameObject),
            collectionCheck: false,
            defaultCapacity: prewarm,
            maxSize: prewarm * 2
        );

        pools[prefab] = pool;

        // prewarm
        var temp = new List<NetworkObject>();
        for (int i = 0; i < prewarm; i++)
            temp.Add(pool.Get());
        foreach (var no in temp)
            pool.Release(no);

        // register a prefab handler so clients reuse the pool too
        NetworkManager.Singleton.PrefabHandler.AddHandler(prefab,
            new PooledPrefabInstanceHandler(prefab, this)
        );
    }

    // Called on Server when you want a fresh collider to Spawn()
    public NetworkObject GetPooled( GameObject prefab, Vector3 pos, Quaternion rot )
    {
        var no = pools[prefab].Get();
        no.transform.SetPositionAndRotation(pos, rot);
        return no;
    }

    // Called on Server after you Despawn()
    public void ReturnPooled( NetworkObject no, GameObject prefab )
        => pools[prefab].Release(no);

    // Client‐side & Host will never call Instantiate
    class PooledPrefabInstanceHandler : INetworkPrefabInstanceHandler
    {
        GameObject prefab;
        NetworkObjectPool owner;
        public PooledPrefabInstanceHandler(GameObject p, NetworkObjectPool o)
            { prefab = p; owner = o; }

        public NetworkObject Instantiate(ulong ownerClientId, Vector3 pos, Quaternion rot)
        {
            // client intercepts spawn message and reuses the pool
            var no = owner.pools[prefab].Get();
            no.transform.SetPositionAndRotation(pos, rot);
            return no;
        }

        public void Destroy(NetworkObject no)
        {
            // client intercepts despawn message and returns the instance
            owner.pools[prefab].Release(no);
        }
    }
}
