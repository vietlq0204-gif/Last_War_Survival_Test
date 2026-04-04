using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.ScriptableObjects;

public abstract class SpawnZone : CoreEventBase
{
    [SerializeField] protected SpawnPresetSO preset;
    [SerializeField] protected PointBaker pointBaker;
    [SerializeField] private Transform spawnParent;
    [SerializeField, Min(1)] private int maxSpawnPerFrame = 32;
    [SerializeField, Min(0f)] private float respawnDelay = 0f;
    [SerializeField, Min(1)] private int despawnBatchSize = 64;
    [SerializeField, Min(0)] private int poolSizePadding = 8;

    private readonly Dictionary<EntityId, ObjectSpawned> _activeInstances = new Dictionary<EntityId, ObjectSpawned>(128);
    private readonly List<ObjectSpawnInstruction> _spawnBatchBuffer = new List<ObjectSpawnInstruction>(128);
    private readonly List<GameObject> _spawnResultsBuffer = new List<GameObject>(128);
    private readonly List<GameObject> _despawnBuffer = new List<GameObject>(128);
    private readonly WaitForEndOfFrame _endOfFrameYield = new WaitForEndOfFrame();

    private Coroutine _initialFillRoutine;
    private Coroutine _refillRoutine;
    private Coroutine _despawnRoutine;
    private EntityId _spawnZoneId;
    private bool _isRunning;
    private bool _waitingForSpawnWindow;
    private bool _restartInitialFillAfterDespawn;
    private int _targetSpawnCount;
    private int _pendingRefillCount;
    private Transform _resolvedSpawnParent;
    private FixedPoseAlgorithm _spawnAlgorithm;
    private SpawnLifecycle? _cachedLifecycleOverride;

    protected PointBaker PointBaker => pointBaker;
    protected EntityId SpawnZoneId => _spawnZoneId;
    protected int ActiveCount => _activeInstances.Count;
    protected int TargetSpawnCount => _targetSpawnCount;
    protected int MaxSpawnPerFrame => maxSpawnPerFrame;
    protected float RespawnDelay => respawnDelay;

    protected override void Awake()
    {
        base.Awake();
        _spawnZoneId = gameObject.GetEntityId();
        ResolvePointBaker();
    }

    protected virtual void OnDisable()
    {
        StopSpawnLoops();

        if (_despawnRoutine != null)
        {
            StopCoroutine(_despawnRoutine);
            _despawnRoutine = null;
        }

        _isRunning = false;
        _waitingForSpawnWindow = false;
        _restartInitialFillAfterDespawn = false;
        _pendingRefillCount = 0;
        ReturnAllToPool(useBufferedDespawn: false);
        FlushDespawnBufferImmediate();
    }

    public sealed override void SubscribeEvents()
    {
        SubscribeZoneEvents();
    }

    protected virtual void SubscribeZoneEvents()
    {
    }

    protected bool TryStartZone(int targetCount)
    {
        targetCount = Mathf.Max(0, targetCount);
        if (targetCount <= 0) return false;
        if (!PrepareSpawnSession(targetCount)) return false;

        StopSpawnLoops();

        _targetSpawnCount = targetCount;
        _pendingRefillCount = 0;
        _waitingForSpawnWindow = false;
        _isRunning = true;

        _initialFillRoutine = StartCoroutine(SpawnInitialFillRoutine());
        return true;
    }

    protected virtual bool PrepareSpawnSession(int targetCount)
    {
        ResolvePointBaker();

        if (preset == null || preset.spawnable == null) return false;
        if (!TryResolveSpawnPose(out var position, out var rotation)) return false;

        _resolvedSpawnParent = ResolveSpawnParent();
        _spawnAlgorithm = new FixedPoseAlgorithm(position, rotation);
        _cachedLifecycleOverride = ResolveLifecycleOverride();

        EnsureRuntimePoolCapacity(targetCount);
        return true;
    }

    protected abstract bool TryResolveSpawnPose(out Vector3 position, out Quaternion rotation);
    protected abstract bool DispatchSpawnBatch(IReadOnlyList<ObjectSpawnInstruction> entries);

    protected virtual int ResolveDesiredPoolSize(int targetCount)
    {
        return Mathf.Max(1, targetCount + poolSizePadding);
    }

    protected virtual int ResolveDesiredPrewarmCount(int targetCount, int desiredPoolSize)
    {
        return Mathf.Clamp(targetCount, 0, desiredPoolSize);
    }

    protected virtual int ResolveDesiredGrowStep(int desiredPoolSize)
    {
        return Mathf.Clamp(maxSpawnPerFrame, 1, Mathf.Max(1, desiredPoolSize));
    }

    protected virtual float ResolveInitialDistanceForIndex(int streamIndex)
    {
        return 0f;
    }

    protected virtual float ResolveRefillDistance()
    {
        return 0f;
    }

    protected virtual bool CanSpawnRefillNow()
    {
        return true;
    }

    protected virtual bool TryRegisterSpawnWindowCallback(Action callback)
    {
        return false;
    }

    protected virtual void OnObjectsRemovedFromStream(int removedCount)
    {
        if (removedCount <= 0 || !_isRunning) return;

        if (ActiveCount == 0)
        {
            _pendingRefillCount = 0;
            if (_despawnBuffer.Count > 0)
            {
                _restartInitialFillAfterDespawn = true;
                return;
            }

            RestartInitialFill();
            return;
        }

        QueueRefill(removedCount);
    }

    protected void HandleReachedEndBatch(IReadOnlyList<ObjectReachedEndInfo> entries)
    {
        if (entries == null || entries.Count == 0) return;

        int removedCount = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.SpawnZoneId != _spawnZoneId) continue;

            var spawnedObject = entry.SpawnedObject;
            if (spawnedObject == null) continue;

            if (!_activeInstances.Remove(spawnedObject.gameObject.GetEntityId())) continue;

            _despawnBuffer.Add(spawnedObject.gameObject);
            removedCount++;
        }

        if (removedCount <= 0) return;

        EnsureDespawnRoutine();
        OnObjectsRemovedFromStream(removedCount);
    }

    protected bool IsActiveInstance(ObjectSpawned spawned)
    {
        return spawned != null && _activeInstances.ContainsKey(spawned.gameObject.GetEntityId());
    }

    protected virtual float ResolveMoveSpeedFor(ObjectSpawned spawnedObject)
    {
        return 0f;
    }

    private IEnumerator SpawnInitialFillRoutine()
    {
        while (_isRunning && ActiveCount < _targetSpawnCount)
        {
            int remaining = _targetSpawnCount - ActiveCount;
            int batchCount = Mathf.Min(maxSpawnPerFrame, remaining);
            if (SpawnBatch(ActiveCount, batchCount, useInitialDistances: true) <= 0)
            {
                _initialFillRoutine = null;
                yield break;
            }

            if (ActiveCount < _targetSpawnCount)
                yield return null;
        }

        _initialFillRoutine = null;
    }

    private IEnumerator RefillRoutine()
    {
        yield return null;

        if (respawnDelay > 0f)
            yield return new WaitForSeconds(respawnDelay);

        while (_isRunning)
        {
            if (_despawnBuffer.Count > 0)
            {
                yield return null;
                continue;
            }

            if (_pendingRefillCount <= 0 || ActiveCount >= _targetSpawnCount)
                break;

            if (!CanSpawnRefillNow())
            {
                WaitForSpawnWindow();
                break;
            }

            int capacity = _targetSpawnCount - ActiveCount;
            int batchCount = Mathf.Min(maxSpawnPerFrame, Mathf.Min(capacity, _pendingRefillCount));
            int spawnedCount = SpawnBatch(0, batchCount, useInitialDistances: false);

            if (spawnedCount <= 0)
            {
                WaitForSpawnWindow();
                break;
            }

            _pendingRefillCount = Mathf.Max(0, _pendingRefillCount - spawnedCount);

            if (_pendingRefillCount > 0 && ActiveCount < _targetSpawnCount)
                yield return null;
        }

        _refillRoutine = null;

        if (_pendingRefillCount > 0 && !_waitingForSpawnWindow && _isRunning)
            EnsureRefillRoutine();
    }

    private IEnumerator FlushDespawnRoutine()
    {
        yield return _endOfFrameYield;

        while (_despawnBuffer.Count > 0)
        {
            int batchSize = Mathf.Min(despawnBatchSize, _despawnBuffer.Count);
            for (int i = 0; i < batchSize; i++)
            {
                int lastIndex = _despawnBuffer.Count - 1;
                var instance = _despawnBuffer[lastIndex];
                _despawnBuffer.RemoveAt(lastIndex);

                if (instance != null)
                    SpawnKit.Despawn(instance);
            }

            if (_despawnBuffer.Count > 0)
                yield return null;
        }

        _despawnRoutine = null;

        if (_restartInitialFillAfterDespawn)
        {
            _restartInitialFillAfterDespawn = false;
            RestartInitialFill();
            yield break;
        }

        EnsureRefillRoutine();
    }

    private int SpawnBatch(int startStreamIndex, int batchCount, bool useInitialDistances)
    {
        if (batchCount <= 0 || preset == null || preset.spawnable == null || _spawnAlgorithm == null)
            return 0;

        _spawnResultsBuffer.Clear();
        int spawnedCount = SpawnKit.SpawnNonAlloc(
            preset.spawnable,
            batchCount,
            _spawnResultsBuffer,
            _resolvedSpawnParent,
            _spawnAlgorithm,
            preset.seed,
            _cachedLifecycleOverride);

        if (spawnedCount <= 0) return 0;

        _spawnBatchBuffer.Clear();
        int registeredCount = 0;

        for (int i = 0; i < _spawnResultsBuffer.Count; i++)
        {
            var instance = _spawnResultsBuffer[i];
            if (instance == null || !instance.TryGetComponent(out ObjectSpawned spawnedObject)) continue;

            _activeInstances[instance.GetEntityId()] = spawnedObject;
            float distance = useInitialDistances
                ? ResolveInitialDistanceForIndex(startStreamIndex + registeredCount)
                : ResolveRefillDistance();

            _spawnBatchBuffer.Add(new ObjectSpawnInstruction
            {
                SpawnedObject = spawnedObject,
                MoveSpeed = ResolveMoveSpeedFor(spawnedObject),
                InitialDistance = distance,
                SpawnZoneId = _spawnZoneId
            });

            registeredCount++;
        }

        if (_spawnBatchBuffer.Count > 0 && !DispatchSpawnBatch(_spawnBatchBuffer))
        {
            for (int i = 0; i < _spawnBatchBuffer.Count; i++)
            {
                var spawnedObject = _spawnBatchBuffer[i].SpawnedObject;
                if (spawnedObject == null) continue;

                _activeInstances.Remove(spawnedObject.gameObject.GetEntityId());
                _despawnBuffer.Add(spawnedObject.gameObject);
            }

            EnsureDespawnRoutine();
            _spawnBatchBuffer.Clear();
            return 0;
        }

        _spawnBatchBuffer.Clear();
        return registeredCount;
    }

    private void RestartInitialFill()
    {
        if (!_isRunning) return;

        _restartInitialFillAfterDespawn = false;
        if (_initialFillRoutine != null) StopCoroutine(_initialFillRoutine);
        _initialFillRoutine = StartCoroutine(SpawnInitialFillRoutine());
    }

    private void QueueRefill(int count)
    {
        if (!_isRunning || count <= 0) return;

        int capacity = Mathf.Max(0, _targetSpawnCount - ActiveCount);
        _pendingRefillCount = Mathf.Min(capacity, _pendingRefillCount + count);
        EnsureRefillRoutine();
    }

    private void EnsureRefillRoutine()
    {
        if (!_isRunning) return;
        if (_despawnBuffer.Count > 0) return;
        if (_pendingRefillCount <= 0) return;
        if (ActiveCount >= _targetSpawnCount) return;
        if (_waitingForSpawnWindow) return;
        if (_refillRoutine != null) return;

        _refillRoutine = StartCoroutine(RefillRoutine());
    }

    private void WaitForSpawnWindow()
    {
        if (_waitingForSpawnWindow) return;

        _waitingForSpawnWindow = true;
        if (TryRegisterSpawnWindowCallback(HandleSpawnWindowOpened)) return;

        _waitingForSpawnWindow = false;
    }

    private void HandleSpawnWindowOpened()
    {
        _waitingForSpawnWindow = false;
        if (!isActiveAndEnabled || !_isRunning) return;
        EnsureRefillRoutine();
    }

    private void EnsureDespawnRoutine()
    {
        if (_despawnRoutine != null || _despawnBuffer.Count == 0) return;
        _despawnRoutine = StartCoroutine(FlushDespawnRoutine());
    }

    private void EnsureRuntimePoolCapacity(int targetCount)
    {
        int desiredPoolSize = ResolveDesiredPoolSize(targetCount);
        int prewarmCount = ResolveDesiredPrewarmCount(targetCount, desiredPoolSize);
        int growStep = ResolveDesiredGrowStep(desiredPoolSize);

        SpawnKit.EnsurePoolCapacity(
            preset.spawnable,
            desiredPoolSize,
            prewarmCount,
            growStep,
            allowGrow: true);
    }

    private void StopSpawnLoops()
    {
        if (_initialFillRoutine != null)
        {
            StopCoroutine(_initialFillRoutine);
            _initialFillRoutine = null;
        }

        if (_refillRoutine != null)
        {
            StopCoroutine(_refillRoutine);
            _refillRoutine = null;
        }
    }

    private void ReturnAllToPool(bool useBufferedDespawn)
    {
        foreach (var instance in _activeInstances.Values)
        {
            if (instance != null)
                _despawnBuffer.Add(instance.gameObject);
        }

        _activeInstances.Clear();

        if (useBufferedDespawn)
            EnsureDespawnRoutine();
    }

    private void FlushDespawnBufferImmediate()
    {
        while (_despawnBuffer.Count > 0)
        {
            int lastIndex = _despawnBuffer.Count - 1;
            var instance = _despawnBuffer[lastIndex];
            _despawnBuffer.RemoveAt(lastIndex);

            if (instance != null)
                SpawnKit.Despawn(instance);
        }
    }

    private Transform ResolveSpawnParent()
    {
        if (spawnParent != null) return spawnParent;
        if (transform.parent != null) return transform.parent;
        return transform;
    }

    private SpawnLifecycle? ResolveLifecycleOverride()
    {
        return preset != null && preset.overrideLifecycle
            ? preset.lifecycle
            : (SpawnLifecycle?)null;
    }

    private void ResolvePointBaker()
    {
        if (pointBaker != null) return;
        if (TryGetComponent(out pointBaker)) return;

        if (transform.parent != null)
            pointBaker = transform.parent.GetComponentInChildren<PointBaker>(true);
        if (pointBaker != null) return;

        pointBaker = FindAnyObjectByType<PointBaker>();
    }
}
