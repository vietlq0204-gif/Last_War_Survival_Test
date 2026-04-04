using System;
using System.Collections.Generic;
using UnityEngine;

public enum PathMovementMode
{
    FollowPathLine = 0,
    StraightToEnd = 1
}

public enum PathUpdateMode
{
    TransformUpdate = 0,
    TransformFixedUpdate = 1,
    RigidbodyFixedUpdate = 2
}

[RequireComponent(typeof(PointBaker))]
public class ControllObjectOnPath : CoreEventBase
{
    private struct RuntimeObject
    {
        public ObjectSpawned spawnedObject;
        public Transform cachedTransform;
        public Rigidbody cachedRigidbody;
        public float distance;
        public float speed;
        public int segmentIndex;
        public EntityId spawnZoneId;
    }

    private struct SpawnWindowWatcher
    {
        public float threshold;
        public Action callback;
    }

    [SerializeField, Min(0.01f)] private float defaultMoveSpeed = 2f;
    [SerializeField] private PathMovementMode movementMode = PathMovementMode.FollowPathLine;
    [SerializeField] private PathUpdateMode updateMode = PathUpdateMode.TransformUpdate;

    private readonly ListPoint.PathCache _pathCache = new ListPoint.PathCache();
    private readonly List<RuntimeObject> _runtimeObjects = new List<RuntimeObject>(128);
    private readonly Dictionary<EntityId, int> _runtimeIndexMap = new Dictionary<EntityId, int>(128);
    private readonly List<ObjectReachedEndInfo> _reachedEndBuffer = new List<ObjectReachedEndInfo>(64);
    private readonly List<SpawnWindowWatcher> _spawnWindowWatchers = new List<SpawnWindowWatcher>(8);

    private PointBaker _pointBaker;
    private bool _hasValidPath;
    private float _closestDistanceToStart = float.PositiveInfinity;
    private Vector3 _pathStartPosition;
    private Vector3 _pathEndPosition;

    public event Action<IReadOnlyList<ObjectReachedEndInfo>> ObjectsReachedEnd;

    public bool HasValidPath => _hasValidPath;
    public float PathLength => _hasValidPath ? _pathCache.totalLength : 0f;
    public int ActiveObjectCount => _runtimeObjects.Count;
    public float ClosestDistanceToStart => _runtimeObjects.Count == 0 ? float.PositiveInfinity : _closestDistanceToStart;

    protected override void Awake()
    {
        base.Awake();
        ResolvePointBaker();
        RebuildPathCache();
    }

    public override void SubscribeEvents()
    {
    }

    private void Update()
    {
        if (updateMode != PathUpdateMode.TransformUpdate) return;
        TickMovement(Time.deltaTime, useRigidbodyMove: false);
    }

    private void FixedUpdate()
    {
        if (updateMode == PathUpdateMode.TransformUpdate) return;
        TickMovement(Time.fixedDeltaTime, useRigidbodyMove: updateMode == PathUpdateMode.RigidbodyFixedUpdate);
    }

    public bool RebuildPathCache()
    {
        ResolvePointBaker();
        _hasValidPath = _pointBaker != null && _pointBaker.listPoint.BuildPathCache(_pathCache);
        _closestDistanceToStart = float.PositiveInfinity;

        if (!_hasValidPath)
        {
            _pathStartPosition = default;
            _pathEndPosition = default;
            return false;
        }

        if (!_pointBaker.listPoint.TryGetFirstPoint(out var firstPoint) || firstPoint == null) return false;
        if (!_pointBaker.listPoint.TryGetLastPoint(out var lastPoint) || lastPoint == null) return false;

        _pathStartPosition = firstPoint.transform.position;
        _pathEndPosition = lastPoint.transform.position;
        return true;
    }

    public bool TryEvaluateDistance(float distance, out Vector3 position)
    {
        position = default;
        if (!_hasValidPath && !RebuildPathCache()) return false;

        int segmentIndex = movementMode == PathMovementMode.FollowPathLine
            ? ResolveSegmentIndex(distance)
            : 0;
        return TryResolvePosition(distance, ref segmentIndex, out position);
    }

    public void RegisterSpawnedBatch(IReadOnlyList<ObjectSpawnInstruction> entries)
    {
        if (entries == null || entries.Count == 0) return;
        if (!_hasValidPath && !RebuildPathCache()) return;

        float closestDistance = _runtimeObjects.Count == 0 ? float.PositiveInfinity : _closestDistanceToStart;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var spawnedObject = entry.SpawnedObject;
            if (!AcceptSpawnedObject(spawnedObject)) continue;

            float initialDistance = Mathf.Clamp(
                entry.InitialDistance,
                0f,
                Mathf.Max(0f, _pathCache.totalLength - 0.0001f));

            int segmentIndex = movementMode == PathMovementMode.FollowPathLine
                ? ResolveSegmentIndex(initialDistance)
                : 0;
            if (!TryResolvePosition(initialDistance, ref segmentIndex, out var worldPosition))
                continue;

            float speed = entry.MoveSpeed > 0f ? entry.MoveSpeed : defaultMoveSpeed;
            EntityId instanceId = spawnedObject.gameObject.GetEntityId();
            var cachedTransform = spawnedObject.CachedTransform;
            var cachedRigidbody = spawnedObject.CachedRigidbody;

            spawnedObject.SetControlledCollisionEnabled(false);
            ApplyPosition(cachedTransform, cachedRigidbody, worldPosition, useRigidbodyMove: false);

            if (_runtimeIndexMap.TryGetValue(instanceId, out int existingIndex))
            {
                var runtimeObject = _runtimeObjects[existingIndex];
                runtimeObject.spawnedObject = spawnedObject;
                runtimeObject.cachedTransform = cachedTransform;
                runtimeObject.cachedRigidbody = cachedRigidbody;
                runtimeObject.distance = initialDistance;
                runtimeObject.speed = speed;
                runtimeObject.segmentIndex = segmentIndex;
                runtimeObject.spawnZoneId = entry.SpawnZoneId;
                _runtimeObjects[existingIndex] = runtimeObject;
            }
            else
            {
                _runtimeIndexMap.Add(instanceId, _runtimeObjects.Count);
                _runtimeObjects.Add(new RuntimeObject
                {
                    spawnedObject = spawnedObject,
                    cachedTransform = cachedTransform,
                    cachedRigidbody = cachedRigidbody,
                    distance = initialDistance,
                    speed = speed,
                    segmentIndex = segmentIndex,
                    spawnZoneId = entry.SpawnZoneId
                });
            }

            if (initialDistance < closestDistance)
                closestDistance = initialDistance;
        }

        _closestDistanceToStart = _runtimeObjects.Count == 0 ? float.PositiveInfinity : closestDistance;
    }

    public void RequestNotifyWhenDistanceToStartAtLeast(float threshold, Action callback)
    {
        if (callback == null) return;

        if (ClosestDistanceToStart >= threshold)
        {
            callback.Invoke();
            return;
        }

        _spawnWindowWatchers.Add(new SpawnWindowWatcher
        {
            threshold = threshold,
            callback = callback
        });
    }

    protected virtual bool AcceptSpawnedObject(ObjectSpawned spawnedObject)
    {
        return spawnedObject != null;
    }

    private void TickMovement(float deltaTime, bool useRigidbodyMove)
    {
        if (!_hasValidPath || _runtimeObjects.Count == 0 || deltaTime <= 0f) return;

        _reachedEndBuffer.Clear();
        float pathLength = _pathCache.totalLength;
        float closestDistance = float.PositiveInfinity;

        for (int i = _runtimeObjects.Count - 1; i >= 0; i--)
        {
            var runtimeObject = _runtimeObjects[i];

            if (!IsRuntimeObjectValid(runtimeObject))
            {
                RemoveRuntimeObjectAt(i);
                continue;
            }

            float nextDistance = runtimeObject.distance + runtimeObject.speed * deltaTime;
            if (nextDistance >= pathLength)
            {
                _reachedEndBuffer.Add(new ObjectReachedEndInfo
                {
                    SpawnedObject = runtimeObject.spawnedObject,
                    SpawnZoneId = runtimeObject.spawnZoneId
                });

                RemoveRuntimeObjectAt(i);
                continue;
            }

            if (!TryResolvePosition(nextDistance, ref runtimeObject.segmentIndex, out var worldPosition))
            {
                RemoveRuntimeObjectAt(i);
                continue;
            }

            runtimeObject.distance = nextDistance;
            ApplyPosition(runtimeObject.cachedTransform, runtimeObject.cachedRigidbody, worldPosition, useRigidbodyMove);
            _runtimeObjects[i] = runtimeObject;

            if (nextDistance < closestDistance)
                closestDistance = nextDistance;
        }

        _closestDistanceToStart = _runtimeObjects.Count == 0 ? float.PositiveInfinity : closestDistance;
        NotifySpawnWindowWatchers();
        RaiseReachedEndIfNeeded();
    }

    private bool TryResolvePosition(float distance, ref int segmentIndex, out Vector3 position)
    {
        position = default;

        if (movementMode == PathMovementMode.StraightToEnd)
        {
            if (_pathCache.totalLength <= Mathf.Epsilon) return false;

            float linearT = Mathf.Clamp01(distance / _pathCache.totalLength);
            position = Vector3.LerpUnclamped(_pathStartPosition, _pathEndPosition, linearT);
            return true;
        }

        if (_pointBaker == null || _pathCache.segStartPosIndex.Count == 0 || _pathCache.totalLength <= Mathf.Epsilon)
            return false;

        float clampedDistance = Mathf.Clamp(distance, 0f, _pathCache.totalLength);
        if (clampedDistance <= 0f)
        {
            segmentIndex = 0;
            position = _pathCache.positions[0];
            return true;
        }

        int maxSegmentIndex = _pathCache.segStartPosIndex.Count - 1;
        segmentIndex = Mathf.Clamp(segmentIndex, 0, maxSegmentIndex);

        while (segmentIndex < maxSegmentIndex && clampedDistance > _pathCache.cumulativeLengths[segmentIndex + 1])
            segmentIndex++;

        while (segmentIndex > 0 && clampedDistance < _pathCache.cumulativeLengths[segmentIndex])
            segmentIndex--;

        int a = _pathCache.segStartPosIndex[segmentIndex];
        int b = a + 1;
        float lenA = _pathCache.cumulativeLengths[segmentIndex];
        float lenB = _pathCache.cumulativeLengths[segmentIndex + 1];
        float segmentLength = lenB - lenA;

        if (segmentLength <= Mathf.Epsilon)
        {
            position = _pathCache.positions[a];
            return true;
        }

        float segmentT = (clampedDistance - lenA) / segmentLength;
        position = Vector3.LerpUnclamped(_pathCache.positions[a], _pathCache.positions[b], segmentT);
        return true;
    }

    private int ResolveSegmentIndex(float distance)
    {
        if (_pathCache.segStartPosIndex.Count == 0) return 0;

        float d = Mathf.Clamp(distance, 0f, _pathCache.totalLength);
        var cumulativeLengths = _pathCache.cumulativeLengths;

        int lo = 0;
        int hi = cumulativeLengths.Count - 1;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            float value = cumulativeLengths[mid];

            if (value < d) lo = mid + 1;
            else hi = mid - 1;
        }

        return Mathf.Clamp(lo - 1, 0, _pathCache.segStartPosIndex.Count - 1);
    }

    private void ResolvePointBaker()
    {
        if (_pointBaker != null) return;
        if (TryGetComponent(out _pointBaker)) return;

        if (transform.parent != null)
            _pointBaker = transform.parent.GetComponentInChildren<PointBaker>(true);
    }

    private bool IsRuntimeObjectValid(RuntimeObject runtimeObject)
    {
        return runtimeObject.spawnedObject != null
            && runtimeObject.cachedTransform != null
            && runtimeObject.spawnedObject.gameObject.activeInHierarchy;
    }

    private void RaiseReachedEndIfNeeded()
    {
        if (_reachedEndBuffer.Count == 0) return;
        ObjectsReachedEnd?.Invoke(_reachedEndBuffer);
        _reachedEndBuffer.Clear();
    }

    private void NotifySpawnWindowWatchers()
    {
        if (_spawnWindowWatchers.Count == 0) return;

        float closestDistance = ClosestDistanceToStart;
        for (int i = _spawnWindowWatchers.Count - 1; i >= 0; i--)
        {
            var watcher = _spawnWindowWatchers[i];
            if (closestDistance < watcher.threshold) continue;

            _spawnWindowWatchers.RemoveAt(i);
            watcher.callback?.Invoke();
        }
    }

    private void RemoveRuntimeObjectAt(int index)
    {
        int lastIndex = _runtimeObjects.Count - 1;
        EntityId removedId = _runtimeObjects[index].spawnedObject != null
            ? _runtimeObjects[index].spawnedObject.gameObject.GetEntityId()
            : default;

        if (index != lastIndex)
        {
            var lastObject = _runtimeObjects[lastIndex];
            _runtimeObjects[index] = lastObject;

            if (lastObject.spawnedObject != null)
                _runtimeIndexMap[lastObject.spawnedObject.gameObject.GetEntityId()] = index;
        }

        _runtimeObjects.RemoveAt(lastIndex);

        if (removedId != default)
            _runtimeIndexMap.Remove(removedId);
    }

    private static void ApplyPosition(Transform cachedTransform, Rigidbody cachedRigidbody, Vector3 worldPosition, bool useRigidbodyMove)
    {
        if (cachedTransform == null) return;

        if (useRigidbodyMove && cachedRigidbody != null)
        {
            cachedRigidbody.MovePosition(worldPosition);
            return;
        }

        cachedTransform.position = worldPosition;
    }
}
