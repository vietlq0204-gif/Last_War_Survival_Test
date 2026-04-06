using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PointBaker))]
public class EnemyGridGroupPathLane : CoreEventBase
{
    private struct PlannedGridSpawn
    {
        public EnemyGridGroup group;
        public EnemySpawner grid;
        public float targetDistance;
        public int stableOrder;
    }

    private struct PendingGridSpawn
    {
        public EnemyGridGroup group;
        public EnemySpawner grid;
        public float requiredGap;
    }

    [Tooltip("PointBaker providing the path used by the enemy grid groups.")]
    [SerializeField] private PointBaker pointBaker;

    [Tooltip("Path controller that moves the enemy grid groups. If empty, one is resolved or created from the PointBaker object.")]
    [SerializeField] private ObjectOnPathController pathController;

    [Tooltip("Enemy grid groups managed by this lane. If Auto Collect Groups From Parent is enabled, this list is rebuilt automatically.")]
    [SerializeField] private EnemyGridGroup[] groups;

    [Tooltip("Automatically gather EnemyGridGroup children from the parent scope and order them by sibling index.")]
    [SerializeField] private bool autoCollectGroupsFromParent = true;

    [Tooltip("Register all configured groups when GameStart is raised.")]
    [SerializeField] private bool registerOnGameStart = true;

    [Tooltip("Register groups on Start when Register On Game Start is disabled.")]
    [SerializeField] private bool autoRegisterOnStart;

    [Tooltip("Movement speed passed to the path controller for each enemy grid.")]
    [SerializeField, Min(0.01f)] private float moveSpeed = 2f;

    [Tooltip("Distance offset between consecutive enemy grid groups on the shared path.")]
    [SerializeField, Min(0f)] private float groupSpacing = 10f;

    [Tooltip("Initial path distance offset applied to the first registered grid wave.")]
    [SerializeField, Min(0f)] private float startDistanceOffset;

    [Tooltip("Optional baked road point treated as the lane's first point. If empty, the baked Point First is used.")]
    [SerializeField] private Transform firstPointOverride;

    private readonly List<ObjectSpawnInstruction> _entriesBuffer = new List<ObjectSpawnInstruction>(4);
    private readonly List<EnemySpawner> _groupGridBuffer = new List<EnemySpawner>(16);
    private readonly List<PlannedGridSpawn> _plannedGridSpawns = new List<PlannedGridSpawn>(32);
    private readonly List<PendingGridSpawn> _pendingGridSpawns = new List<PendingGridSpawn>(32);
    private readonly Dictionary<EnemyGridGroup, float> _groupSpacingHints = new Dictionary<EnemyGridGroup, float>(8);

    private bool _hasRegistered;
    private bool _isSubscribedToController;
    private bool _isWaitingForSpawnWindow;
    private float _laneStartDistance;
    private float _laneSpawnDistance;
    private EntityId _laneSpawnZoneId;

    protected override void Awake()
    {
        base.Awake();
        ResolvePointBaker();
        ResolvePathController();
        EnsureControllerSubscribed();
    }

    private void Start()
    {
        if (!registerOnGameStart && autoRegisterOnStart)
            RegisterGroupsOnPath();
    }

    public override void SubscribeEvents()
    {
        if (registerOnGameStart)
            CoreEvents.gameStart.Subscribe(HandleGameStart, Binder);
    }

    private void OnDisable()
    {
        _isWaitingForSpawnWindow = false;
        UnsubscribeFromController();
    }

    public void RegisterGroupsOnPath()
    {
        ResolvePointBaker();
        ResolvePathController();
        EnsureControllerSubscribed();
        ResolveGroups();

        if (_hasRegistered || pathController == null || groups == null || groups.Length == 0)
            return;

        if (!pathController.HasValidPath && !pathController.RebuildPathCache())
            return;

        var defaultRecycleTriggerCollider = ResolveDefaultRecycleTriggerCollider();
        _plannedGridSpawns.Clear();
        _pendingGridSpawns.Clear();
        _groupSpacingHints.Clear();

        _laneStartDistance = ResolveLaneStartDistance();
        _laneSpawnDistance = _laneStartDistance + startDistanceOffset;
        _laneSpawnZoneId = gameObject.GetEntityId();

        float groupDistanceCursor = _laneSpawnDistance;
        int stableOrder = 0;

        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (group == null)
                continue;

            group.SetFallbackRecycleTriggerCollider(defaultRecycleTriggerCollider);
            group.ConfigurePathRuntime(pathController, this, moveSpeed, _laneSpawnDistance, _laneSpawnZoneId);

            int gridCount = group.CopyOrderedGridsTo(_groupGridBuffer);
            if (gridCount <= 0)
                continue;

            float gridSpacing = Mathf.Max(group.GetGridSpacingDistanceHint(), 0.01f);
            float groupSpan = gridCount > 1 ? gridSpacing * (gridCount - 1) : 0f;
            _groupSpacingHints[group] = gridSpacing;

            for (int gridIndex = 0; gridIndex < _groupGridBuffer.Count; gridIndex++)
            {
                var grid = _groupGridBuffer[gridIndex];
                if (grid == null)
                    continue;

                MoveGridToLaneStart(grid);

                int distanceOrderIndex = (_groupGridBuffer.Count - 1) - gridIndex;
                _plannedGridSpawns.Add(new PlannedGridSpawn
                {
                    group = group,
                    grid = grid,
                    targetDistance = groupDistanceCursor + gridSpacing * distanceOrderIndex,
                    stableOrder = stableOrder++
                });
            }

            groupDistanceCursor += groupSpan + groupSpacing;
        }

        if (_plannedGridSpawns.Count <= 0)
            return;

        _plannedGridSpawns.Sort(ComparePlannedGridSpawnOrder);

        for (int i = 0; i < _plannedGridSpawns.Count; i++)
        {
            float requiredGap = 0f;
            if (i > 0)
            {
                requiredGap = Mathf.Max(
                    0.01f,
                    _plannedGridSpawns[i - 1].targetDistance - _plannedGridSpawns[i].targetDistance);
            }

            _pendingGridSpawns.Add(new PendingGridSpawn
            {
                group = _plannedGridSpawns[i].group,
                grid = _plannedGridSpawns[i].grid,
                requiredGap = requiredGap
            });
        }

        _hasRegistered = true;
        _isWaitingForSpawnWindow = false;
        TryFlushPendingGridSpawns();
    }

    public bool TryEnqueueGridRecycle(EnemyGridGroup group, EnemySpawner grid)
    {
        if (pathController == null || group == null || grid == null)
            return false;

        var pathItem = grid.GetComponent<EnemyGridPathItem>();
        if (pathItem != null)
            pathController.TryReleaseSpawnedObject(pathItem);

        MoveGridToLaneStart(grid);

        _pendingGridSpawns.Add(new PendingGridSpawn
        {
            group = group,
            grid = grid,
            requiredGap = ResolveRequiredGapFor(group)
        });

        TryFlushPendingGridSpawns();
        return true;
    }

    private void HandleGameStart(GameStartEvent gameStartEvent)
    {
        if (gameStartEvent == null || !gameStartEvent.IsStarted)
            return;

        RegisterGroupsOnPath();
    }

    private void ResolvePointBaker()
    {
        if (pointBaker != null)
            return;

        if (TryGetComponent(out pointBaker))
            return;

        pointBaker = GetComponentInChildren<PointBaker>(true);
    }

    private void ResolvePathController()
    {
        if (pathController != null)
            return;

        if (pointBaker == null)
            return;

        if (pointBaker.TryGetComponent(out EnemyGridGroupOnPathController typedController))
        {
            pathController = typedController;
            return;
        }

        if (pointBaker.TryGetComponent(out ObjectOnPathController genericController))
        {
            pathController = genericController;
            return;
        }

        pathController = pointBaker.gameObject.AddComponent<EnemyGridGroupOnPathController>();
    }

    private void EnsureControllerSubscribed()
    {
        if (_isSubscribedToController || pathController == null)
            return;

        pathController.ObjectsReachedEnd += HandleObjectsReachedEnd;
        _isSubscribedToController = true;
    }

    private void UnsubscribeFromController()
    {
        if (!_isSubscribedToController || pathController == null)
            return;

        pathController.ObjectsReachedEnd -= HandleObjectsReachedEnd;
        _isSubscribedToController = false;
    }

    private void ResolveGroups()
    {
        if (!autoCollectGroupsFromParent && groups != null && groups.Length > 0)
            return;

        Transform scope = transform.parent != null ? transform.parent : transform;
        groups = scope.GetComponentsInChildren<EnemyGridGroup>(true);
        System.Array.Sort(groups, CompareGroupOrder);
    }

    private void TryFlushPendingGridSpawns()
    {
        if (pathController == null)
            return;

        while (_pendingGridSpawns.Count > 0)
        {
            float requiredGap = Mathf.Max(0f, _pendingGridSpawns[0].requiredGap);
            if (pathController.ActiveObjectCount > 0 && pathController.ClosestDistanceToStart < requiredGap)
            {
                RequestNextSpawnWindow(requiredGap);
                return;
            }

            _isWaitingForSpawnWindow = false;
            SpawnNextPendingGrid();
        }
    }

    private void SpawnNextPendingGrid()
    {
        if (_pendingGridSpawns.Count <= 0)
            return;

        var pendingSpawn = _pendingGridSpawns[0];
        _pendingGridSpawns.RemoveAt(0);

        if (pendingSpawn.group == null || pendingSpawn.grid == null)
            return;

        if (!pendingSpawn.group.TryBuildPathEntry(
                pendingSpawn.grid,
                _laneSpawnDistance,
                moveSpeed,
                _laneSpawnZoneId,
                out var entry))
        {
            return;
        }

        MoveGridToLaneStart(pendingSpawn.grid);

        _entriesBuffer.Clear();
        _entriesBuffer.Add(entry);
        pathController.RegisterSpawnedBatch(_entriesBuffer);
    }

    private void RequestNextSpawnWindow(float requiredGap)
    {
        if (_isWaitingForSpawnWindow || pathController == null)
            return;

        _isWaitingForSpawnWindow = true;
        pathController.RequestNotifyWhenDistanceToStartAtLeast(requiredGap, HandleSpawnWindowReached);
    }

    private void HandleSpawnWindowReached()
    {
        _isWaitingForSpawnWindow = false;
        TryFlushPendingGridSpawns();
    }

    private void MoveGridToLaneStart(EnemySpawner grid)
    {
        if (grid == null)
            return;

        var startPoint = ResolveConfiguredFirstPoint();
        if (startPoint == null)
            return;

        grid.transform.SetPositionAndRotation(startPoint.position, startPoint.rotation);
    }

    private float ResolveRequiredGapFor(EnemyGridGroup group)
    {
        if (group != null && _groupSpacingHints.TryGetValue(group, out float spacing))
            return Mathf.Max(0.01f, spacing);

        return Mathf.Max(group != null ? group.GetGridSpacingDistanceHint() : 0.01f, 0.01f);
    }

    private Collider ResolveDefaultRecycleTriggerCollider()
    {
        if (pointBaker == null)
            return null;

        if (!pointBaker.listPoint.TryGetLastPoint(out var lastPoint) || lastPoint == null)
            return null;

        return lastPoint.GetComponent<Collider>();
    }

    private float ResolveLaneStartDistance()
    {
        if (pointBaker == null)
            return 0f;

        var resolvedFirstPoint = ResolveConfiguredFirstPoint();
        if (resolvedFirstPoint == null)
            return 0f;

        if (pointBaker.listPoint.TryGetDistanceToPoint(resolvedFirstPoint.gameObject, out float distance))
            return Mathf.Max(0f, distance);

        Debug.LogWarning(
            $"[EnemyGridGroupPathLane:{name}] First Point Override '{resolvedFirstPoint.name}' is not part of the baked path. Falling back to distance 0.",
            this);
        return 0f;
    }

    private Transform ResolveConfiguredFirstPoint()
    {
        if (firstPointOverride != null)
            return firstPointOverride;

        if (pointBaker == null)
            return null;

        if (!pointBaker.listPoint.TryGetFirstPoint(out var firstPoint) || firstPoint == null)
            return null;

        return firstPoint.transform;
    }

    private void HandleObjectsReachedEnd(IReadOnlyList<ObjectReachedEndInfo> entries)
    {
        if (entries == null || entries.Count == 0)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            var pathItem = entries[i].SpawnedObject as EnemyGridPathItem;
            if (pathItem == null)
                continue;

            var owningGroup = pathItem.OwningGroup;
            if (owningGroup == null)
                continue;

            owningGroup.HandlePathItemReachedEnd(pathItem);
        }
    }

    private static int CompareGroupOrder(EnemyGridGroup left, EnemyGridGroup right)
    {
        if (left == right)
            return 0;

        if (left == null)
            return 1;

        if (right == null)
            return -1;

        return left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex());
    }

    private static int ComparePlannedGridSpawnOrder(PlannedGridSpawn left, PlannedGridSpawn right)
    {
        int distanceCompare = right.targetDistance.CompareTo(left.targetDistance);
        if (distanceCompare != 0)
            return distanceCompare;

        return left.stableOrder.CompareTo(right.stableOrder);
    }
}
