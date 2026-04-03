using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.ScriptableObjects;

public class CardAddQuantitySpawnZone : CoreEventBase
{
    [SerializeField] private SpawnPresetSO preset;
    [SerializeField] private PointBaker pointBaker;
    [SerializeField] private ControllObjectOnPath pathController;
    [SerializeField] private Transform spawnParent;
    [SerializeField, Min(0.01f)] private float moveSpeed = 2f;
    [SerializeField] private bool autoFillRoadOnStart = true;
    [SerializeField, Min(1)] private int loopCardCount = 1;
    [SerializeField, Min(0.05f)] private float cardSpacing = 0.72f;
    [SerializeField, Min(1)] private int maxVisibleCards = 128;
    [SerializeField, Min(1)] private int maxSpawnPerFrame = 32;
    [SerializeField, Min(0f)] private float respawnDelay = 0f;
    [SerializeField, Min(0)] private int poolSizePadding = 8;

    private readonly Dictionary<EntityId, GameObject> _activeInstances = new Dictionary<EntityId, GameObject>(128);
    private readonly List<GameObject> _despawnBuffer = new List<GameObject>(128);
    private GameObject _firstPoint;
    private EntityId _spawnZoneId;
    private bool _isRunning;
    private float _pathLength;
    private int _targetCardCount;
    private float _timeSinceLastSpawn = float.PositiveInfinity;

    protected override void Awake()
    {
        base.Awake();

        _spawnZoneId = gameObject.GetEntityId();
        ResolvePointBaker();
        ResolvePathController();
        CacheEndpoints();
    }

    private void OnDisable()
    {
        _isRunning = false;
        ReturnAllToPool();
    }

    private void Update()
    {
        if (!_isRunning) return;

        _timeSinceLastSpawn += Time.deltaTime;
        BackfillMissingCards();
        TryFlushPendingRespawns();
    }

    public override void SubscribeEvents()
    {
        CoreEvents.gameStart.Subscribe(HandleGameStart, Binder);
        CoreEvents.cardAddQuantityReachedEnd.Subscribe(HandleCardReachedEnd, Binder);
    }

    private void HandleGameStart(GameStartEvent e)
    {
        if (e == null || !e.IsStarted) return;
        if (!PrepareSpawnLoop()) return;

        _isRunning = true;
        _timeSinceLastSpawn = float.PositiveInfinity;
        SpawnInitialFill();
    }

    private void HandleCardReachedEnd(CardAddQuantityReachedEndEvent e)
    {
        if (e == null || e.Card == null || e.PointBaker != pointBaker || e.SpawnZoneId != _spawnZoneId) return;

        EntityId instanceId = e.Card.gameObject.GetEntityId();
        if (!_activeInstances.Remove(instanceId)) return;

        SpawnKit.Despawn(e.Card.gameObject);
    }

    private bool PrepareSpawnLoop()
    {
        ResolvePointBaker();
        ResolvePathController();
        CacheEndpoints();

        if (preset == null || preset.spawnable == null) return false;
        if (pointBaker == null || pathController == null || _firstPoint == null) return false;
        if (!pathController.HasValidPath && !pathController.RebuildPathCache()) return false;

        _pathLength = pathController.PathLength;
        if (_pathLength <= Mathf.Epsilon) return false;

        _targetCardCount = ResolveTargetCardCount();
        ConfigurePoolForStream();
        SpawnKit.Prewarm(preset.spawnable);
        return true;
    }

    private int ResolveTargetCardCount()
    {
        if (!autoFillRoadOnStart)
            return Mathf.Max(1, loopCardCount);

        float usableLength = Mathf.Max(0f, _pathLength - 0.0001f);
        int estimatedCount = Mathf.FloorToInt(usableLength / cardSpacing) + 1;
        return Mathf.Clamp(estimatedCount, 1, maxVisibleCards);
    }

    private void SpawnInitialFill()
    {
        int spawned = _activeInstances.Count;
        int remaining = Mathf.Max(0, _targetCardCount - spawned);

        while (_isRunning && remaining > 0)
        {
            int batchCount = Mathf.Min(maxSpawnPerFrame, remaining);
            int spawnedThisBatch = SpawnInitialBatch(spawned, batchCount);
            if (spawnedThisBatch <= 0) break;

            spawned += spawnedThisBatch;
            remaining -= spawnedThisBatch;
        }
    }

    private int SpawnInitialBatch(int startStreamIndex, int batchCount)
    {
        if (batchCount <= 0) return 0;

        var handle = SpawnKit.Spawn(
            preset.spawnable,
            batchCount,
            ResolveSpawnParent(),
            new FixedPoseAlgorithm(_firstPoint.transform.position, _firstPoint.transform.rotation),
            preset.seed,
            ResolveLifecycleOverride());

        if (handle == null || handle.IsEmpty) return 0;

        int registeredCount = 0;
        float maxDistance = Mathf.Max(0f, _pathLength - 0.0001f);

        for (int i = 0; i < handle.Instances.Count; i++)
        {
            GameObject instance = handle.Instances[i];
            if (instance == null || !instance.TryGetComponent(out CardAddQuantity card)) continue;

            float initialDistance = Mathf.Min((startStreamIndex + registeredCount) * cardSpacing, maxDistance);
            RegisterSpawnedCard(card, initialDistance);
            registeredCount++;
        }

        return registeredCount;
    }

    private bool SpawnReplacementCard()
    {
        var handle = SpawnKit.Spawn(
            preset.spawnable,
            1,
            ResolveSpawnParent(),
            new FixedPoseAlgorithm(_firstPoint.transform.position, _firstPoint.transform.rotation),
            preset.seed,
            ResolveLifecycleOverride());

        if (handle == null || handle.IsEmpty) return false;

        GameObject instance = handle.FirstOrDefault;
        if (instance == null || !instance.TryGetComponent(out CardAddQuantity card)) return false;

        RegisterSpawnedCard(card, 0f);
        _timeSinceLastSpawn = 0f;
        return true;
    }

    private void RegisterSpawnedCard(CardAddQuantity card, float initialDistance)
    {
        if (card == null) return;

        _activeInstances[card.gameObject.GetEntityId()] = card.gameObject;

        CoreEvents.cardAddQuantitySpawned.Raise(new CardAddQuantitySpawnedEvent
        {
            Card = card,
            PointBaker = pointBaker,
            MoveSpeed = moveSpeed,
            InitialDistance = initialDistance,
            SpawnZoneId = _spawnZoneId
        });
    }

    private void BackfillMissingCards()
    {
        int missingCount = _targetCardCount - _activeInstances.Count;
        if (missingCount <= 0) return;

        if (_activeInstances.Count == 0)
        {
            SpawnInitialFill();
            return;
        }
    }

    private void TryFlushPendingRespawns()
    {
        if (_activeInstances.Count >= _targetCardCount) return;
        if (!CanSpawnAtStart()) return;
        if (_timeSinceLastSpawn < respawnDelay) return;

        int budget = Mathf.Min(maxSpawnPerFrame, _targetCardCount - _activeInstances.Count);
        for (int i = 0; i < budget; i++)
        {
            if (!CanSpawnAtStart()) break;
            if (!SpawnReplacementCard()) break;
        }
    }

    private bool CanSpawnAtStart()
    {
        if (pathController == null) return false;
        if (_activeInstances.Count == 0) return true;

        return pathController.ClosestDistanceToStart >= cardSpacing;
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

    private void ReturnAllToPool()
    {
        _despawnBuffer.Clear();

        foreach (var instance in _activeInstances.Values)
        {
            if (instance != null)
                _despawnBuffer.Add(instance);
        }

        _activeInstances.Clear();

        for (int i = 0; i < _despawnBuffer.Count; i++)
        {
            SpawnKit.Despawn(_despawnBuffer[i]);
        }

        _despawnBuffer.Clear();
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

    private void ResolvePathController()
    {
        if (pathController != null) return;
        if (pointBaker == null) return;

        if (!pointBaker.TryGetComponent(out pathController))
            pathController = pointBaker.gameObject.AddComponent<ControllObjectOnPath>();
    }

    private void CacheEndpoints()
    {
        _firstPoint = null;

        if (pointBaker == null) return;
        pointBaker.listPoint.TryGetFirstPoint(out _firstPoint);
    }

    private void ConfigurePoolForStream()
    {
        if (preset?.spawnable?.poolConfig == null) return;

        var poolConfig = preset.spawnable.poolConfig;
        int desiredPoolSize = Mathf.Max(1, _targetCardCount + poolSizePadding);

        poolConfig.maxSize = Mathf.Max(poolConfig.maxSize, desiredPoolSize);
        poolConfig.prewarmCount = Mathf.Max(poolConfig.prewarmCount, Mathf.Min(_targetCardCount, desiredPoolSize));
        poolConfig.growStep = Mathf.Max(poolConfig.growStep, Mathf.Min(32, desiredPoolSize));
        poolConfig.allowGrow = true;
        poolConfig.Sanitize();
    }
}
