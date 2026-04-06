using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PointBaker))]
public class EnemyGridGroupPathLane : CoreEventBase
{
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

    [Tooltip("Movement speed passed to the path controller for each enemy grid group.")]
    [SerializeField, Min(0.01f)] private float moveSpeed = 2f;

    [Tooltip("Distance offset between consecutive enemy grid groups on the shared path.")]
    [SerializeField, Min(0f)] private float groupSpacing = 10f;

    [Tooltip("Initial path distance offset applied to the first registered group.")]
    [SerializeField, Min(0f)] private float startDistanceOffset;

    [Tooltip("Optional baked road point treated as the lane's first point. If empty, the baked Point First is used.")]
    [SerializeField] private Transform firstPointOverride;

    private readonly List<ObjectSpawnInstruction> _entriesBuffer = new List<ObjectSpawnInstruction>(16);

    private bool _hasRegistered;
    private bool _isSubscribedToController;

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
        _entriesBuffer.Clear();
        float laneStartDistance = ResolveLaneStartDistance();
        float groupDistanceCursor = laneStartDistance + startDistanceOffset;
        EntityId spawnZoneId = gameObject.GetEntityId();

        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (group == null)
                continue;

            group.SetFallbackRecycleTriggerCollider(defaultRecycleTriggerCollider);
            group.ConfigurePathRuntime(pathController, moveSpeed, groupDistanceCursor, spawnZoneId);

            int registeredGridCount = group.AppendPathEntries(
                _entriesBuffer,
                groupDistanceCursor,
                moveSpeed,
                spawnZoneId);

            if (registeredGridCount <= 0)
                continue;

            groupDistanceCursor += group.GetPathSpan() + groupSpacing;
        }

        if (_entriesBuffer.Count <= 0)
            return;

        pathController.RegisterSpawnedBatch(_entriesBuffer);
        _hasRegistered = true;
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
}
