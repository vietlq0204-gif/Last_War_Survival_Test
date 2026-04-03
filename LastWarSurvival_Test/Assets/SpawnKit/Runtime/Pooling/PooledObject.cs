using UnityEngine;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.Services;

namespace Vit.SpawnKit.Pooling
{
/// <summary>
/// Callback contract for prefabs that reset state when they are spawned or despawned.
/// </summary>
public interface ISpawnPoolCallbacks
{
    void OnSpawnedFromPool();
    void OnDespawnedToPool();
}

/// <summary>
/// Component attached to pooled instances to handle ownership and lifecycle.
/// </summary>
public sealed class PooledObject : MonoBehaviour
{
    private GameObjectPool _ownerPool;
    private TimedDespawnScheduler _scheduler;
    private int _poolId;

    private SpawnLifecycle _lifecycle;
    private bool _isSpawned;
    private float _despawnAt;
    private uint _scheduleVersion;

    private ISpawnPoolCallbacks[] _callbacks;
    private bool _callbacksCached;

    internal bool IsSpawned => _isSpawned;
    internal uint ScheduleVersion => _scheduleVersion;

    internal void Bind(GameObjectPool owner, int poolId, TimedDespawnScheduler scheduler)
    {
        _ownerPool = owner;
        _poolId = poolId;
        _scheduler = scheduler;
        CacheCallbacks();
    }

    public void OnRent(SpawnLifecycle lifecycle)
    {
        _lifecycle = lifecycle;
        _lifecycle.Sanitize();
        _isSpawned = true;

        if (_lifecycle.IsTimed)
        {
            float now = _lifecycle.useUnscaledTime ? Time.unscaledTime : Time.time;
            _despawnAt = now + _lifecycle.delaySeconds;
            _scheduleVersion++;
            _scheduler?.Schedule(this, _scheduleVersion, _despawnAt, _lifecycle.useUnscaledTime);
        }
        else
        {
            InvalidateTimedSchedule();
        }

        for (int i = 0; i < _callbacks.Length; i++)
        {
            _callbacks[i]?.OnSpawnedFromPool();
        }
    }

    public void OnReturned()
    {
        if (!_isSpawned) return;

        _isSpawned = false;
        InvalidateTimedSchedule();

        for (int i = 0; i < _callbacks.Length; i++)
        {
            _callbacks[i]?.OnDespawnedToPool();
        }
    }

    public bool ReturnToPool()
    {
        if (_ownerPool == null) return false;
        if (!_isSpawned) return false;
        return _ownerPool.Return(gameObject, _poolId);
    }

    private void OnDisable()
    {
        if (!_isSpawned) return;
        if (_lifecycle.mode != SpawnReleaseMode.OnDisable) return;

        ReturnToPool();
    }

    private void OnDestroy()
    {
        _isSpawned = false;
        InvalidateTimedSchedule();
        _ownerPool = null;
        _scheduler = null;
    }

    internal bool IsOwnedBy(GameObjectPool owner, int poolId)
    {
        return _ownerPool == owner && _poolId == poolId;
    }

    internal void CacheCallbacks()
    {
        if (_callbacksCached) return;

        _callbacks = GetComponentsInChildren<ISpawnPoolCallbacks>(true);
        _callbacksCached = true;
    }

    private void InvalidateTimedSchedule()
    {
        _despawnAt = 0f;
        _scheduleVersion++;
    }
}
}
