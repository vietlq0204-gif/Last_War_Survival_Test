using System.Collections.Generic;
using UnityEngine;

public class CardAddQuantitySpawnZone : SpawnZone
{
    [SerializeField] private ObjectOnPathController pathController;
    [SerializeField, Min(0.01f)] private float moveSpeed = 2f;
    [SerializeField] private bool autoFillRoadOnStart = true;
    [SerializeField, Min(1)] private int loopCardCount = 1;
    [SerializeField, Min(0.05f)] private float cardSpacing = 0.72f;
    [SerializeField, Min(1)] private int maxVisibleCards = 128;

    private Transform _firstPoint;
    private float _pathLength;
    private bool _isSubscribedToController;

    protected override void SubscribeZoneEvents()
    {
        CoreEvents.gameStart.Subscribe(HandleGameStart, Binder);
        ResolvePathController();
        EnsureControllerSubscribed();
    }

    protected override void OnDisable()
    {
        UnsubscribeFromController();
        base.OnDisable();
    }

    protected override bool PrepareSpawnSession(int targetCount)
    {
        ResolvePathController();
        EnsureControllerSubscribed();
        CacheEndpoints();

        if (PointBaker == null || pathController == null || _firstPoint == null) return false;
        if (!pathController.HasValidPath && !pathController.RebuildPathCache()) return false;

        _pathLength = pathController.PathLength;
        if (_pathLength <= Mathf.Epsilon) return false;

        return base.PrepareSpawnSession(targetCount);
    }

    protected override bool TryResolveSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        CacheEndpoints();

        if (_firstPoint == null)
        {
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        position = _firstPoint.position;
        rotation = _firstPoint.rotation;
        return true;
    }

    protected override bool DispatchSpawnBatch(IReadOnlyList<ObjectSpawnInstruction> entries)
    {
        if (pathController == null) return false;
        pathController.RegisterSpawnedBatch(entries);
        return true;
    }

    protected override float ResolveInitialDistanceForIndex(int streamIndex)
    {
        float maxDistance = Mathf.Max(0f, _pathLength - 0.0001f);
        return Mathf.Min(streamIndex * cardSpacing, maxDistance);
    }

    protected override bool CanSpawnRefillNow()
    {
        if (pathController == null) return false;
        if (ActiveCount == 0) return true;

        return pathController.ClosestDistanceToStart >= cardSpacing;
    }

    protected override bool TryRegisterSpawnWindowCallback(System.Action callback)
    {
        if (pathController == null) return false;

        pathController.RequestNotifyWhenDistanceToStartAtLeast(cardSpacing, callback);
        return true;
    }

    protected override float ResolveMoveSpeedFor(ObjectSpawned spawnedObject)
    {
        return moveSpeed;
    }

    private void HandleGameStart(GameStartEvent e)
    {
        if (e == null || !e.IsStarted) return;
        TryStartZone(ResolveTargetCardCount());
    }

    private void HandleObjectsReachedEnd(IReadOnlyList<ObjectReachedEndInfo> entries)
    {
        HandleReachedEndBatch(entries);
    }

    private int ResolveTargetCardCount()
    {
        ResolvePathController();
        EnsureControllerSubscribed();

        if (pathController != null && (!pathController.HasValidPath && !pathController.RebuildPathCache()))
            return Mathf.Max(1, loopCardCount);

        _pathLength = pathController != null ? pathController.PathLength : 0f;

        if (!autoFillRoadOnStart)
            return Mathf.Max(1, loopCardCount);

        float usableLength = Mathf.Max(0f, _pathLength - 0.0001f);
        int estimatedCount = Mathf.FloorToInt(usableLength / cardSpacing) + 1;
        return Mathf.Clamp(estimatedCount, 1, maxVisibleCards);
    }

    private void ResolvePathController()
    {
        if (pathController != null) return;
        if (PointBaker == null) return;

        if (PointBaker.TryGetComponent(out CardAddQuantityOnPathController typedController))
        {
            pathController = typedController;
            return;
        }

        if (PointBaker.TryGetComponent(out ObjectOnPathController genericController))
        {
            pathController = genericController;
            return;
        }

        pathController = PointBaker.gameObject.AddComponent<CardAddQuantityOnPathController>();
    }

    private void EnsureControllerSubscribed()
    {
        if (_isSubscribedToController || pathController == null) return;
        pathController.ObjectsReachedEnd += HandleObjectsReachedEnd;
        _isSubscribedToController = true;
    }

    private void UnsubscribeFromController()
    {
        if (!_isSubscribedToController || pathController == null) return;
        pathController.ObjectsReachedEnd -= HandleObjectsReachedEnd;
        _isSubscribedToController = false;
    }

    private void CacheEndpoints()
    {
        _firstPoint = null;

        if (PointBaker == null) return;
        PointBaker.listPoint.TryGetFirstPoint(out var firstPoint);
        _firstPoint = firstPoint != null ? firstPoint.transform : null;
    }
}
