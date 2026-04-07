using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.ScriptableObjects;
using Vit.SpawnKit.Services;

public sealed class ObstacleSpawner : MonoBehaviour
{
    [SerializeField] private SpawnPresetSO obstacleSpawnPreset;
    [SerializeField] private PointBaker pointBaker;
    [SerializeField] private Transform spawnParent;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private bool keepRoadFilled;
    [SerializeField, Min(0)] private int poolSizePadding = 4;

    private readonly List<Transform> _slotPoints = new List<Transform>(64);
    private readonly List<Transform> _spawnSlotBuffer = new List<Transform>(64);
    private readonly List<Obstacle> _activeObstacles = new List<Obstacle>(64);
    private readonly List<GameObject> _spawnResults = new List<GameObject>(64);

    private SpawnableSO _preparedPoolSpawnable;
    private int _preparedPoolSize;
    private bool _suppressDespawnNotifications;
    private bool _hasWarnedMissingPointBaker;
    private bool _hasWarnedMissingPreset;
    private bool _hasWarnedMissingSpawnManager;
    private bool _hasPendingFillRequest;
    private SpawnManager _cachedSpawnManager;

    private void Awake()
    {
        ResolvePointBaker();
    }

    private void Start()
    {
        if (spawnOnStart)
            SpawnObstacles();
    }

    private void LateUpdate()
    {
        if (!_hasPendingFillRequest || !keepRoadFilled)
            return;

        _hasPendingFillRequest = false;
        FillEmptySlots();
    }

    public bool SpawnObstacles()
    {
        ResolvePointBaker();

        if (!TryBuildSlotPoints())
            return false;

        if (obstacleSpawnPreset == null || obstacleSpawnPreset.spawnable == null)
        {
            if (!_hasWarnedMissingPreset)
            {
                _hasWarnedMissingPreset = true;
                Debug.LogWarning($"'{name}' needs a valid obstacleSpawnPreset.", this);
            }

            return false;
        }

        _hasWarnedMissingPreset = false;

        if (ResolveSpawnManager() == null)
        {
            if (!_hasWarnedMissingSpawnManager)
            {
                _hasWarnedMissingSpawnManager = true;
                Debug.LogWarning($"'{name}' could not find a SpawnManager in the scene.", this);
            }

            return false;
        }

        _hasWarnedMissingSpawnManager = false;

        int spawnCount = ResolveTargetActiveCount();
        if (spawnCount <= 0)
            return false;

        DespawnActiveObstacles();
        PreparePool(spawnCount);

        var algorithm = new PointSequenceSpawnAlgorithm(_slotPoints);
        int[] variantPlan = obstacleSpawnPreset.spawnable.BuildVariantPlan(spawnCount);
        SpawnLifecycle? lifecycle = obstacleSpawnPreset.overrideLifecycle
            ? obstacleSpawnPreset.lifecycle
            : (SpawnLifecycle?)null;

        var request = new SpawnRequest(
            obstacleSpawnPreset.spawnable,
            spawnCount,
            ResolveSpawnParent(),
            algorithm,
            obstacleSpawnPreset.seed,
            lifecycle,
            variantPlan);

        _spawnResults.Clear();
        int spawnedCount = FindSpawnManagerAndSpawn(request);
        if (spawnedCount <= 0)
            return false;

        RegisterSpawnedObstacles();
        QueueFillEmptySlotsIfNeeded();
        return _activeObstacles.Count > 0;
    }

    public void NotifyObstacleDespawned(Obstacle obstacle)
    {
        if (_suppressDespawnNotifications || obstacle == null)
            return;

        int vacatedSlotIndex = Mathf.Max(0, obstacle.SlotIndex);
        int removedIndex = -1;
        for (int i = 0; i < _activeObstacles.Count; i++)
        {
            Obstacle activeObstacle = _activeObstacles[i];
            if (activeObstacle == null)
                continue;

            if (activeObstacle == obstacle || activeObstacle.CachedEntityId.Equals(obstacle.CachedEntityId))
            {
                removedIndex = i;
                break;
            }
        }

        if (removedIndex < 0)
            return;

        _activeObstacles.RemoveAt(removedIndex);
        CompactSlotsFrom(vacatedSlotIndex);
        QueueFillEmptySlotsIfNeeded();
    }

    private int FindSpawnManagerAndSpawn(in SpawnRequest request)
    {
        SpawnManager spawnManager = ResolveSpawnManager();
        return spawnManager != null
            ? spawnManager.SpawnNonAlloc(request, _spawnResults)
            : 0;
    }

    private void RegisterSpawnedObstacles()
    {
        CleanupAndSortActiveObstacles();

        for (int i = 0; i < _spawnResults.Count; i++)
        {
            GameObject instance = _spawnResults[i];
            if (instance == null)
                continue;

            if (!instance.TryGetComponent(out Obstacle obstacle))
            {
                Debug.LogWarning(
                    $"Spawned obstacle '{instance.name}' is missing an Obstacle component and will be returned to pool.",
                    instance);
                SpawnKit.Despawn(instance);
                continue;
            }

            int slotIndex = _activeObstacles.Count;
            Transform slot = slotIndex < _slotPoints.Count ? _slotPoints[slotIndex] : null;
            obstacle.Bind(this, slotIndex, slot);
            _activeObstacles.Add(obstacle);
        }

        CompactSlotsFrom(0);
    }

    private void CompactSlotsFrom(int startIndex)
    {
        CleanupAndSortActiveObstacles();

        if (startIndex < 0)
            startIndex = 0;

        for (int i = startIndex; i < _activeObstacles.Count; i++)
        {
            Obstacle obstacle = _activeObstacles[i];
            if (obstacle == null)
                continue;

            Transform slot = i < _slotPoints.Count ? _slotPoints[i] : null;
            obstacle.Bind(this, i, slot);
        }
    }

    private void CleanupAndSortActiveObstacles()
    {
        for (int i = _activeObstacles.Count - 1; i >= 0; i--)
        {
            Obstacle obstacle = _activeObstacles[i];
            if (obstacle == null)
                _activeObstacles.RemoveAt(i);
        }

        _activeObstacles.Sort(CompareObstacleSlotOrder);
    }

    private static int CompareObstacleSlotOrder(Obstacle left, Obstacle right)
    {
        if (ReferenceEquals(left, right))
            return 0;

        if (left == null)
            return 1;

        if (right == null)
            return -1;

        return left.SlotIndex.CompareTo(right.SlotIndex);
    }

    private void DespawnActiveObstacles()
    {
        if (_activeObstacles.Count <= 0)
            return;

        _suppressDespawnNotifications = true;

        for (int i = 0; i < _activeObstacles.Count; i++)
        {
            Obstacle obstacle = _activeObstacles[i];
            if (obstacle == null)
                continue;

            SpawnKit.Despawn(obstacle.gameObject);
        }

        _activeObstacles.Clear();
        _suppressDespawnNotifications = false;
        _hasPendingFillRequest = false;
    }

    private int ResolveTargetActiveCount()
    {
        if (obstacleSpawnPreset == null || obstacleSpawnPreset.spawnable == null)
            return 0;

        if (keepRoadFilled)
            return _slotPoints.Count;

        int maxByPreset = obstacleSpawnPreset.spawnable.ResolveSpawnCount(obstacleSpawnPreset.MaxCount);
        return Mathf.Min(_slotPoints.Count, maxByPreset);
    }

    private bool FillEmptySlots()
    {
        ResolvePointBaker();

        if (!TryBuildSlotPoints())
            return false;

        if (obstacleSpawnPreset == null || obstacleSpawnPreset.spawnable == null)
            return false;

        if (ResolveSpawnManager() == null)
            return false;

        CleanupAndSortActiveObstacles();

        int targetActiveCount = ResolveTargetActiveCount();
        int missingCount = Mathf.Max(0, targetActiveCount - _activeObstacles.Count);
        if (missingCount <= 0)
            return false;

        _spawnSlotBuffer.Clear();
        for (int i = _activeObstacles.Count; i < targetActiveCount && i < _slotPoints.Count; i++)
        {
            Transform slot = _slotPoints[i];
            if (slot != null)
                _spawnSlotBuffer.Add(slot);
        }

        if (_spawnSlotBuffer.Count <= 0)
            return false;

        PreparePool(targetActiveCount);

        var algorithm = new PointSequenceSpawnAlgorithm(_spawnSlotBuffer);
        int[] variantPlan = obstacleSpawnPreset.spawnable.BuildVariantPlan(_spawnSlotBuffer.Count);
        SpawnLifecycle? lifecycle = obstacleSpawnPreset.overrideLifecycle
            ? obstacleSpawnPreset.lifecycle
            : (SpawnLifecycle?)null;

        var request = new SpawnRequest(
            obstacleSpawnPreset.spawnable,
            _spawnSlotBuffer.Count,
            ResolveSpawnParent(),
            algorithm,
            obstacleSpawnPreset.seed,
            lifecycle,
            variantPlan);

        _spawnResults.Clear();
        int spawnedCount = FindSpawnManagerAndSpawn(request);
        if (spawnedCount <= 0)
            return false;

        RegisterSpawnedObstacles();
        QueueFillEmptySlotsIfNeeded();
        return true;
    }

    private void PreparePool(int spawnCount)
    {
        SpawnableSO spawnable = obstacleSpawnPreset != null ? obstacleSpawnPreset.spawnable : null;
        if (spawnable == null)
            return;

        if (_preparedPoolSpawnable != spawnable)
        {
            _preparedPoolSpawnable = spawnable;
            _preparedPoolSize = 0;
        }

        int desiredPoolSize = Mathf.Max(1, spawnCount + Mathf.Max(0, poolSizePadding));
        if (desiredPoolSize <= _preparedPoolSize)
            return;

        int prewarmCount = Mathf.Clamp(spawnCount, 0, desiredPoolSize);
        int growStep = Mathf.Max(1, spawnCount);
        if (!SpawnKit.EnsurePoolCapacity(spawnable, desiredPoolSize, prewarmCount, growStep, allowGrow: true))
            return;

        _preparedPoolSize = desiredPoolSize;
    }

    private bool TryBuildSlotPoints()
    {
        _slotPoints.Clear();

        if (pointBaker == null || pointBaker.listPoint == null || pointBaker.listPoint.pointData == null)
        {
            if (!_hasWarnedMissingPointBaker)
            {
                _hasWarnedMissingPointBaker = true;
                Debug.LogWarning($"'{name}' needs a PointBaker with baked points.", this);
            }

            return false;
        }

        var orderedPoints = new List<ListPoint.PointData>(pointBaker.listPoint.pointData.Length);
        for (int i = 0; i < pointBaker.listPoint.pointData.Length; i++)
        {
            ListPoint.PointData pointData = pointBaker.listPoint.pointData[i];
            if (pointData.point == null)
                continue;

            orderedPoints.Add(pointData);
        }

        orderedPoints.Sort((left, right) => left.index.CompareTo(right.index));

        for (int i = 0; i < orderedPoints.Count; i++)
        {
            Transform slot = orderedPoints[i].point != null
                ? orderedPoints[i].point.transform
                : null;

            if (slot != null)
                _slotPoints.Add(slot);
        }

        _hasWarnedMissingPointBaker = false;
        return _slotPoints.Count > 0;
    }

    private Transform ResolveSpawnParent()
    {
        return spawnParent != null ? spawnParent : transform;
    }

    private void ResolvePointBaker()
    {
        if (pointBaker != null)
            return;

        if (TryGetComponent(out pointBaker))
            return;

        pointBaker = GetComponentInChildren<PointBaker>(true);
        if (pointBaker != null)
            return;

        if (transform.parent != null)
            pointBaker = transform.parent.GetComponentInChildren<PointBaker>(true);
    }

    private SpawnManager ResolveSpawnManager()
    {
        if (_cachedSpawnManager != null)
            return _cachedSpawnManager;

        _cachedSpawnManager = SpawnManager.Instance != null
            ? SpawnManager.Instance
            : FindAnyObjectByType<SpawnManager>();

        return _cachedSpawnManager;
    }

    private void QueueFillEmptySlotsIfNeeded()
    {
        if (!keepRoadFilled)
            return;

        CleanupAndSortActiveObstacles();

        if (_activeObstacles.Count >= ResolveTargetActiveCount())
            return;

        _hasPendingFillRequest = true;
    }

    private sealed class PointSequenceSpawnAlgorithm : ISpawnAlgorithm
    {
        private readonly IReadOnlyList<Transform> _points;

        public PointSequenceSpawnAlgorithm(IReadOnlyList<Transform> points)
        {
            _points = points;
        }

        public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
        {
            if (_points != null && index >= 0 && index < _points.Count && _points[index] != null)
            {
                position = _points[index].position;
                rotation = _points[index].rotation;
                return;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
        }
    }
}
