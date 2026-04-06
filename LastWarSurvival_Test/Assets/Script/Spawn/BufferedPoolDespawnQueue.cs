using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Api;

[DefaultExecutionOrder(-900)]
public sealed class BufferedPoolDespawnQueue : MonoBehaviour
{
    private const string RuntimeObjectName = "__BufferedPoolDespawnQueue";
    private const int DefaultDespawnBatchSize = 32;

    private static BufferedPoolDespawnQueue _instance;

    [SerializeField, Min(1)] private int despawnBatchSize = DefaultDespawnBatchSize;

    private readonly Queue<QueuedDespawn> _queuedDespawns = new Queue<QueuedDespawn>(256);
    private readonly HashSet<EntityId> _queuedEntityIds = new HashSet<EntityId>();

    private struct QueuedDespawn
    {
        public EntityId entityId;
        public GameObject instance;
    }

    public static bool Queue(GameObject instance)
    {
        if (instance == null)
            return false;

        return ResolveInstance().EnqueueInternal(instance);
    }

    public static bool Queue(ObjectSpawned spawnedObject)
    {
        return spawnedObject != null && Queue(spawnedObject.gameObject);
    }

    private static BufferedPoolDespawnQueue ResolveInstance()
    {
        if (_instance != null)
            return _instance;

        _instance = FindAnyObjectByType<BufferedPoolDespawnQueue>();
        if (_instance != null)
            return _instance;

        var runtimeObject = new GameObject(RuntimeObjectName);
        DontDestroyOnLoad(runtimeObject);
        _instance = runtimeObject.AddComponent<BufferedPoolDespawnQueue>();
        return _instance;
    }

    private bool EnqueueInternal(GameObject instance)
    {
        EntityId entityId = instance.GetEntityId();
        if (!_queuedEntityIds.Add(entityId))
            return false;

        _queuedDespawns.Enqueue(new QueuedDespawn
        {
            entityId = entityId,
            instance = instance,
        });

        return true;
    }

    private void LateUpdate()
    {
        int batchBudget = Mathf.Max(1, despawnBatchSize);
        while (batchBudget-- > 0 && _queuedDespawns.Count > 0)
        {
            QueuedDespawn queued = _queuedDespawns.Dequeue();
            _queuedEntityIds.Remove(queued.entityId);

            if (queued.instance != null)
                SpawnKit.Despawn(queued.instance);
        }
    }

    private void OnDisable()
    {
        if (_instance == this)
            _instance = null;
    }
}
