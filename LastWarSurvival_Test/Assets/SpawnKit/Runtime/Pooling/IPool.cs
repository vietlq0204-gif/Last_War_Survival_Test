using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.Factories;
using Vit.SpawnKit.Services;

namespace Vit.SpawnKit.Pooling
{
/// <summary>
/// Basic pool contract.
/// </summary>
public interface IPool
{
    SpawnKey Key { get; }
    bool IsReady { get; }

    GameObject Rent(Transform parent, Vector3 position, Quaternion rotation, SpawnLifecycle lifecycle);
    bool Return(GameObject go);
    void Prewarm(int count);
    void Dispose();
}

/// <summary>
/// GameObject pool that uses a LIFO stack for inactive instances.
/// </summary>
public sealed class GameObjectPool : IPool
{
    private readonly Stack<GameObject> _available;
    private readonly HashSet<EntityId> _inactiveInstanceIds;
    private readonly HashSet<EntityId> _activeInstanceIds;
    private readonly List<Transform> _pendingReparents;
    private readonly ISpawnFactory _factory;
    private readonly PoolConfig _config;
    private readonly TimedDespawnScheduler _scheduler;

    private readonly Transform _root;
    private readonly int _poolId;

    private int _totalCount;
    private bool _disposed;

    public SpawnKey Key { get; }
    public bool IsReady => _factory.IsReady;
    public int ActiveCount => _activeInstanceIds.Count;
    public int InactiveCount => _inactiveInstanceIds.Count;
    public int TotalCount => _totalCount;
    public int MaxSize => _config.maxSize;

    internal GameObjectPool(
        SpawnKey key,
        int poolId,
        ISpawnFactory factory,
        PoolConfig config,
        TimedDespawnScheduler scheduler,
        Transform poolsRoot,
        string debugName = null)
    {
        Key = key;
        _poolId = poolId;
        _factory = factory;
        _config = config ?? new PoolConfig();
        _config.Sanitize();
        _scheduler = scheduler;

        _available = new Stack<GameObject>(Mathf.Max(4, _config.prewarmCount));
        _inactiveInstanceIds = new HashSet<EntityId>();
        _activeInstanceIds = new HashSet<EntityId>();
        _pendingReparents = new List<Transform>(8);

        string rootName = string.IsNullOrWhiteSpace(debugName) ? $"Pool_{key.Id}" : debugName;
        _root = new GameObject(rootName).transform;
        _root.SetParent(poolsRoot, false);
        _root.gameObject.SetActive(true);
    }

    public void Prewarm(int count)
    {
        if (_disposed) return;
        if (!IsReady) return;

        int target = Mathf.Min(_config.maxSize, _totalCount + count);
        while (_totalCount < target)
        {
            var go = CreateNewInstance(_root, Vector3.zero, Quaternion.identity);
            if (go == null) break;
            PrepareReturned(go);
            PushAvailable(go);
        }
    }

    public GameObject Rent(Transform parent, Vector3 position, Quaternion rotation, SpawnLifecycle lifecycle)
    {
        if (_disposed) return null;
        if (!IsReady) return null;

        GameObject go = null;

        while (_available.Count > 0)
        {
            go = _available.Pop();
            if (go == null) continue;

            EntityId id = GetRuntimeId(go);
            _inactiveInstanceIds.Remove(id);
            break;
        }

        if (go == null)
        {
            if (_totalCount >= _config.maxSize)
            {
                return null;
            }

            int createCount = _config.allowGrow ? Mathf.Min(_config.growStep, _config.maxSize - _totalCount) : 1;

            go = CreateNewInstance(parent, position, rotation);
            if (go == null) return null;

            for (int i = 1; i < createCount; i++)
            {
                var extra = CreateNewInstance(_root, Vector3.zero, Quaternion.identity);
                if (extra == null) break;
                PrepareReturned(extra);
                PushAvailable(extra);
            }
        }

        var tr = go.transform;
        RemovePendingReparent(tr);
        tr.SetParent(parent, false);
        tr.SetPositionAndRotation(position, rotation);

        var pooled = go.GetComponent<PooledObject>();
        if (pooled != null)
        {
            pooled.OnRent(lifecycle);
        }

        go.SetActive(true);
        EntityId instanceId = GetRuntimeId(go);
        _inactiveInstanceIds.Remove(instanceId);

        if (!_activeInstanceIds.Add(instanceId))
        {
            Debug.LogWarning($"Pool '{_root.name}' rented a duplicated active instance: {go.name}", go);
        }

        return go;
    }

    public bool Return(GameObject go)
    {
        if (_disposed) return false;
        if (go == null) return false;

        var pooled = go.GetComponent<PooledObject>();
        if (pooled == null) return false;
        if (!pooled.IsOwnedBy(this, _poolId))
        {
            Debug.LogWarning($"Pool '{_root.name}' rejected return from foreign instance: {go.name}", go);
            return false;
        }

        return pooled.ReturnToPool();
    }

    internal bool Return(GameObject go, int poolId)
    {
        if (_disposed) return false;
        if (go == null) return false;
        if (poolId != _poolId) return false;

        EntityId id = GetRuntimeId(go);
        if (!_activeInstanceIds.Remove(id))
        {
            Debug.LogWarning($"Pool '{_root.name}' rejected return for non-active instance: {go.name}", go);
            return false;
        }

        if (!_inactiveInstanceIds.Add(id))
        {
            Debug.LogWarning($"Pool '{_root.name}' detected duplicate inactive instance on return: {go.name}", go);
            return false;
        }

        PrepareReturned(go);
        _available.Push(go);
        return true;
    }

    public int Trim(int keepInactive)
    {
        if (_disposed) return 0;

        keepInactive = Mathf.Max(0, keepInactive);
        int removed = 0;

        while (_available.Count > keepInactive)
        {
            var go = _available.Pop();
            if (go == null)
            {
                _totalCount = Mathf.Max(0, _totalCount - 1);
                continue;
            }

            _inactiveInstanceIds.Remove(GetRuntimeId(go));
            Object.Destroy(go);
            _totalCount = Mathf.Max(0, _totalCount - 1);
            removed++;
        }

        return removed;
    }

    public int Clear()
    {
        return Trim(0);
    }

    public int ReleaseUnused()
    {
        return Trim(_config.prewarmCount);
    }

    internal void FlushPendingReparents()
    {
        if (_disposed) return;
        if (_pendingReparents.Count == 0) return;

        for (int i = _pendingReparents.Count - 1; i >= 0; i--)
        {
            var tr = _pendingReparents[i];
            if (tr == null || tr.parent == _root)
            {
                RemovePendingReparentAt(i);
                continue;
            }

            // If the object became active again before we flushed the queue, keep its runtime parent.
            if (tr.gameObject.activeInHierarchy)
            {
                RemovePendingReparentAt(i);
                continue;
            }

            if (TrySetPoolParent(tr))
            {
                RemovePendingReparentAt(i);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Clear();
        _inactiveInstanceIds.Clear();
        _activeInstanceIds.Clear();
        _pendingReparents.Clear();
        _totalCount = 0;

        if (_root != null) Object.Destroy(_root.gameObject);

        _factory.Dispose();
    }

    private void PrepareReturned(GameObject go)
    {
        var pooled = go.GetComponent<PooledObject>();
        if (pooled != null)
        {
            pooled.OnReturned();
        }

        go.SetActive(false);
        ReparentOrQueue(go.transform);
    }

    private GameObject CreateNewInstance(Transform parent, Vector3 pos, Quaternion rot)
    {
        var go = _factory.CreateInstance(parent, pos, rot);
        if (go == null) return null;

        _totalCount++;

        var pooled = go.GetComponent<PooledObject>();
        if (pooled == null) pooled = go.AddComponent<PooledObject>();
        pooled.Bind(this, _poolId, _scheduler);

        return go;
    }

    private void PushAvailable(GameObject go)
    {
        if (go == null) return;

        _inactiveInstanceIds.Add(GetRuntimeId(go));
        _available.Push(go);
    }

    private static EntityId GetRuntimeId(GameObject go)
    {
        return go.GetEntityId();
    }

    private void ReparentOrQueue(Transform tr)
    {
        if (tr == null) return;
        if (tr.parent == _root) return;

        // Delaying reparent for inactive objects avoids Unity errors when a parent hierarchy
        // is currently being activated or deactivated and OnDisable returns the child to pool.
        if (!tr.gameObject.activeInHierarchy)
        {
            if (_pendingReparents.Contains(tr)) return;
            _pendingReparents.Add(tr);
            return;
        }

        if (TrySetPoolParent(tr)) return;
        if (_pendingReparents.Contains(tr)) return;

        _pendingReparents.Add(tr);
    }

    private bool TrySetPoolParent(Transform tr)
    {
        try
        {
            tr.SetParent(_root, false);
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }

    private void RemovePendingReparentAt(int index)
    {
        int lastIndex = _pendingReparents.Count - 1;
        _pendingReparents[index] = _pendingReparents[lastIndex];
        _pendingReparents.RemoveAt(lastIndex);
    }

    private void RemovePendingReparent(Transform tr)
    {
        if (tr == null) return;

        for (int i = _pendingReparents.Count - 1; i >= 0; i--)
        {
            if (_pendingReparents[i] != tr) continue;
            RemovePendingReparentAt(i);
            return;
        }
    }
}
}
