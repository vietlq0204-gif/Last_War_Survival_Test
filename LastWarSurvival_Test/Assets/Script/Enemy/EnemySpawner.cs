using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Components;

public class EnemySpawner : SpawnGridQueue
{
    [Header("Enemy Grid")]
    [Tooltip("Collider used to test whether this grid still contains active enemy objects. If empty, the grid zone collider or local collider is used.")]
    [SerializeField] private Collider occupancyCollider;

    [Tooltip("Physics layers checked when falling back to overlap-based occupancy detection.")]
    [SerializeField] private LayerMask occupancyLayers = ~0;

    [Tooltip("Size of the reusable non-alloc overlap buffer for occupancy checks.")]
    [SerializeField, Min(1)] private int overlapBufferSize = 32;

    [Tooltip("If enabled, this grid queues a fill request on Start.")]
    [SerializeField] private bool fillAvailableSlotsOnStart;

    [Header("Obstacle Probe")]
    [SerializeField] private bool useBatchedObstacleProbe = true;
    [SerializeField] private LayerMask obstacleProbeLayers;
    [SerializeField, Min(1)] private int obstacleProbeBatchSize = 16;
    [SerializeField, Min(1)] private int obstacleProbeHitBufferSize = 8;
    [SerializeField, Min(0f)] private float obstacleProbeExtraRadius = 0.05f;
    [SerializeField, Min(0)] private int obstacleProbeCooldownFrames = 4;

    [Header("Home Damage Batch")]
    [SerializeField, Min(0)] private int homeDamageDispatchDelayFrames = 2;

    private ColliderSurfaceGridZone _cachedGridZone;
    private Collider[] _overlapBuffer;
    private readonly HashSet<EntityId> _aliveEnemyIds = new HashSet<EntityId>();
    private readonly HashSet<long> _blockedSlotKeys = new HashSet<long>();
    private readonly List<Enemy> _activeEnemiesBuffer = new List<Enemy>(64);
    private readonly List<Enemy> _unassignedEnemiesBuffer = new List<Enemy>(32);
    private readonly List<Enemy> _overflowEnemiesBuffer = new List<Enemy>(16);
    private Collider[] _obstacleProbeHits;
    private EnemyGridGroup _owningGroup;
    private bool _hasPendingEmptyRecycleRequest;
    private int _pendingHomeDamage;
    private int _pendingHomeHitCount;
    private int _homeDamageDispatchFrame = -1;
    private int _pendingHomeCollisionLayer = -1;
    private bool _hasMotionSample;
    private Vector3 _lastMotionSamplePosition;
    private Vector3 _pathMoveDirection = Vector3.forward;
    private float _pathMoveSpeed;
    private int _nextObstacleProbeStartIndex;

    private void OnValidate()
    {
        homeDamageDispatchDelayFrames = Mathf.Max(0, homeDamageDispatchDelayFrames);
        obstacleProbeBatchSize = Mathf.Max(1, obstacleProbeBatchSize);
        obstacleProbeHitBufferSize = Mathf.Max(1, obstacleProbeHitBufferSize);
        obstacleProbeExtraRadius = Mathf.Max(0f, obstacleProbeExtraRadius);
        obstacleProbeCooldownFrames = Mathf.Max(0, obstacleProbeCooldownFrames);
        EnsureObstacleProbeLayer();
    }

    protected override string SpawnedObjectLabel => "enemy";

    protected override void Start()
    {
        base.Start();

        if (fillAvailableSlotsOnStart)
            FillAvailableSlots();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        FlushPendingHomeDamage(forceImmediate: true);
        _aliveEnemyIds.Clear();
        _hasPendingEmptyRecycleRequest = false;
        _pendingHomeDamage = 0;
        _pendingHomeHitCount = 0;
        _homeDamageDispatchFrame = -1;
        _pendingHomeCollisionLayer = -1;
        _blockedSlotKeys.Clear();
        _nextObstacleProbeStartIndex = 0;
        SyncBlockedSlotsWithAlgorithm();
    }

    public bool FillAvailableSlots()
    {
        int requestCount = Mathf.Max(0, GetAvailableSlotCount() - PendingSpawnCount);
        return requestCount > 0 && QueueSpawn(requestCount);
    }

    public void RecycleAndRefillSlots()
    {
        ResetPendingSpawnQueue();
        ResetBlockedSlots();
        _nextObstacleProbeStartIndex = 0;

        ColliderSurfaceGridAlgorithm algorithm = ResolveGridAlgorithm();
        if (algorithm == null)
            return;

        ReassignActiveEnemiesToSlots(algorithm);
        FillAvailableSlots();
    }

    public int GetAvailableSlotCount()
    {
        ColliderSurfaceGridAlgorithm algorithm = ResolveGridAlgorithm();
        return algorithm != null ? algorithm.GetAvailableSlotCount() : 0;
    }

    public override int GetOccupiedSlotCount()
    {
        ColliderSurfaceGridAlgorithm algorithm = ResolveGridAlgorithm();
        return algorithm != null ? algorithm.OccupiedSlotCount : 0;
    }

    public bool HasSpawnedObjectsInside()
    {
        if (_aliveEnemyIds.Count > 0)
            return true;

        if (HasSpawnedChildren())
            return true;

        var resolvedOccupancyCollider = ResolveOccupancyCollider();
        if (resolvedOccupancyCollider == null)
            return GetOccupiedSlotCount() > 0;

        EnsureOverlapBuffer();

        int hitCount = OverlapOccupancyVolume(resolvedOccupancyCollider);
        var owningGroup = GetComponentInParent<EnemyGridGroup>();

        for (int i = 0; i < hitCount; i++)
        {
            var hitCollider = _overlapBuffer[i];
            if (hitCollider == null || hitCollider == resolvedOccupancyCollider)
                continue;

            var spawnedObject = hitCollider.GetComponentInParent<ObjectSpawned>();
            if (spawnedObject == null)
                continue;

            if (owningGroup != null && spawnedObject == owningGroup)
                continue;

            if (spawnedObject is EnemyGridGroup)
                continue;

            if (spawnedObject is Enemy enemy && !enemy.IsAlive)
                continue;

            return true;
        }

        return false;
    }

    public void RegisterSpawnedEnemy(Enemy enemy)
    {
        if (enemy == null)
            return;

        _aliveEnemyIds.Add(enemy.CachedEntityId);
        _hasPendingEmptyRecycleRequest = false;
    }

    public void NotifyEnemyDefeated(Enemy enemy)
    {
        RemoveTrackedEnemy(enemy);
        TryRequestImmediateRecycleWhenEmpty();
    }

    public void NotifyEnemyDespawned(Enemy enemy)
    {
        RemoveTrackedEnemy(enemy);
    }

    public void NotifyEnemyExitedGrid(Enemy enemy)
    {
        RemoveTrackedEnemy(enemy);
        TryRequestImmediateRecycleWhenEmpty();
    }

    public void NotifyEnemyReachedHome(Enemy enemy, int collisionLayer)
    {
        if (enemy == null)
            return;

        _pendingHomeDamage = AddClamped(_pendingHomeDamage, enemy.ContactDamage);
        _pendingHomeHitCount = AddClamped(_pendingHomeHitCount, 1);
        _pendingHomeCollisionLayer = collisionLayer;

        if (_homeDamageDispatchFrame < 0)
            _homeDamageDispatchFrame = Time.frameCount + Mathf.Max(0, homeDamageDispatchDelayFrames);
    }

    public bool Intersects(Collider other)
    {
        var resolvedOccupancyCollider = ResolveOccupancyCollider();
        return resolvedOccupancyCollider != null
               && other != null
               && resolvedOccupancyCollider.bounds.Intersects(other.bounds);
    }

    private void Update()
    {
        FlushPendingHomeDamage();
    }

    private void LateUpdate()
    {
        SamplePathMotion();
        ScanObstacleContactsBatch();
    }

    public bool TryGetPathMotion(out Vector3 moveDirection, out float moveSpeed)
    {
        moveDirection = _pathMoveDirection;
        moveSpeed = _pathMoveSpeed;
        return _pathMoveDirection.sqrMagnitude > 0.0001f;
    }

    public bool TryGetRandomAliveEnemyAnchor(Enemy requester, out Enemy anchor)
    {
        anchor = null;
        int candidateCount = 0;

        Transform root = transform;
        int childCount = root.childCount;

        for (int i = 0; i < childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;

            if (!child.TryGetComponent(out Enemy candidate))
                continue;

            if (candidate == requester || !candidate.IsAlive)
                continue;

            candidateCount++;
            if (Random.Range(0, candidateCount) == 0)
                anchor = candidate;
        }

        return anchor != null;
    }

    public bool TryRelocateEnemyToUnblockedSlot(
        Enemy enemy,
        ObstacleSlotSelectionMode selectionMode,
        float nearbyDistance,
        out Vector3 targetLocalPosition,
        out Quaternion targetLocalRotation)
    {
        targetLocalPosition = default;
        targetLocalRotation = Quaternion.identity;

        if (enemy == null)
            return false;

        ColliderSurfaceGridAlgorithm algorithm = ResolveGridAlgorithm();
        if (algorithm == null)
            return false;

        bool hasBoundReservation = enemy.TryGetComponent(out SpawnGridSlotReservation reservation) && reservation.IsBound;
        if (hasBoundReservation)
        {
            long currentSlotKey = reservation.SlotKey;
            _blockedSlotKeys.Add(currentSlotKey);
            reservation.ReleaseReservationNow();
            SyncBlockedSlotsWithAlgorithm();
        }

        uint seed = (uint)Random.Range(int.MinValue, int.MaxValue);
        uint salt = (uint)Time.frameCount;

        if (!TryResolveObstaclePlacement(
                algorithm,
                seed,
                salt,
                selectionMode,
                out ColliderSurfaceGridPlacement placement,
                out bool shouldReservePlacement))
        {
            return false;
        }

        if (shouldReservePlacement)
            algorithm.BindReservation(enemy.gameObject, in placement);

        Vector3 slotLocalPosition = transform.InverseTransformPoint(placement.Position);
        Vector3 slotOffset = ResolveSlotOffset(slotLocalPosition, nearbyDistance);

        targetLocalPosition = slotLocalPosition + slotOffset;
        targetLocalPosition.y = enemy.transform.localPosition.y;
        targetLocalRotation = Quaternion.Inverse(transform.rotation) * placement.Rotation;
        return true;
    }

    public void ResetBlockedSlots()
    {
        if (_blockedSlotKeys.Count <= 0)
            return;

        _blockedSlotKeys.Clear();
        SyncBlockedSlotsWithAlgorithm();
    }

    private ColliderSurfaceGridZone ResolveGridZone()
    {
        if (_cachedGridZone != null)
            return _cachedGridZone;

        _cachedGridZone = GetComponentInChildren<ColliderSurfaceGridZone>(true);
        return _cachedGridZone;
    }

    private ColliderSurfaceGridAlgorithm ResolveGridAlgorithm()
    {
        ColliderSurfaceGridZone gridZone = ResolveGridZone();
        if (gridZone == null)
            return null;

        ColliderSurfaceGridAlgorithm algorithm = gridZone.ResolveAlgorithm();
        if (algorithm == null)
            return null;

        algorithm.SetBlockedSlots(_blockedSlotKeys);
        return algorithm;
    }

    private Collider ResolveOccupancyCollider()
    {
        if (occupancyCollider != null)
            return occupancyCollider;

        var gridZone = ResolveGridZone();
        if (gridZone != null && gridZone.ZoneCollider != null)
        {
            occupancyCollider = gridZone.ZoneCollider;
            return occupancyCollider;
        }

        occupancyCollider = GetComponent<Collider>();
        return occupancyCollider;
    }

    private void RemoveTrackedEnemy(Enemy enemy)
    {
        if (enemy == null)
            return;

        _aliveEnemyIds.Remove(enemy.CachedEntityId);
    }

    private void TryRequestImmediateRecycleWhenEmpty()
    {
        if (_aliveEnemyIds.Count > 0 || _hasPendingEmptyRecycleRequest)
            return;

        EnemyGridGroup owningGroup = ResolveOwningGroup();
        if (owningGroup == null)
            return;

        if (!owningGroup.RequestImmediateRecycle(this))
            return;

        _hasPendingEmptyRecycleRequest = true;
    }

    private EnemyGridGroup ResolveOwningGroup()
    {
        if (_owningGroup != null)
            return _owningGroup;

        _owningGroup = GetComponentInParent<EnemyGridGroup>();
        return _owningGroup;
    }

    private void EnsureOverlapBuffer()
    {
        int desiredBufferSize = Mathf.Max(1, overlapBufferSize);
        if (_overlapBuffer != null && _overlapBuffer.Length == desiredBufferSize)
            return;

        _overlapBuffer = new Collider[desiredBufferSize];
    }

    private bool HasSpawnedChildren()
    {
        Transform root = transform;
        int childCount = root.childCount;

        for (int i = 0; i < childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;

            if (!child.TryGetComponent(out ObjectSpawned spawnedObject))
                continue;

            if (spawnedObject is EnemyGridGroup)
                continue;

            if (spawnedObject is Enemy enemy && !enemy.IsAlive)
                continue;

            return true;
        }

        return false;
    }

    private int OverlapOccupancyVolume(Collider sourceCollider)
    {
        if (sourceCollider is BoxCollider boxCollider)
        {
            Vector3 halfExtents = Vector3.Scale(boxCollider.size * 0.5f, Abs(boxCollider.transform.lossyScale));
            Vector3 center = boxCollider.transform.TransformPoint(boxCollider.center);
            return Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _overlapBuffer,
                boxCollider.transform.rotation,
                ResolveOccupancyLayerMask(),
                QueryTriggerInteraction.Collide);
        }

        Bounds bounds = sourceCollider.bounds;
        return Physics.OverlapBoxNonAlloc(
            bounds.center,
            bounds.extents,
            _overlapBuffer,
            Quaternion.identity,
            ResolveOccupancyLayerMask(),
            QueryTriggerInteraction.Collide);
    }

    private int ResolveOccupancyLayerMask()
    {
        return occupancyLayers.value != 0 ? occupancyLayers.value : Physics.AllLayers;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(
            Mathf.Abs(value.x),
            Mathf.Abs(value.y),
            Mathf.Abs(value.z));
    }

    private void FlushPendingHomeDamage(bool forceImmediate = false)
    {
        if (_pendingHomeDamage <= 0 || _pendingHomeHitCount <= 0)
            return;

        if (!forceImmediate && _homeDamageDispatchFrame >= 0 && Time.frameCount < _homeDamageDispatchFrame)
            return;

        CoreEvents.enemyHomeDamageBatch.Raise(new EnemyHomeDamageBatchEvent(
            this,
            _pendingHomeDamage,
            _pendingHomeHitCount,
            _pendingHomeCollisionLayer));

        _pendingHomeDamage = 0;
        _pendingHomeHitCount = 0;
        _homeDamageDispatchFrame = -1;
        _pendingHomeCollisionLayer = -1;
    }

    private void SamplePathMotion()
    {
        Vector3 currentPosition = transform.position;
        if (!_hasMotionSample)
        {
            _lastMotionSamplePosition = currentPosition;
            _hasMotionSample = true;
            return;
        }

        Vector3 delta = currentPosition - _lastMotionSamplePosition;
        float deltaTime = Time.deltaTime;

        if (deltaTime > Mathf.Epsilon && delta.sqrMagnitude > 0.000001f)
        {
            _pathMoveDirection = delta.normalized;
            _pathMoveSpeed = delta.magnitude / deltaTime;
        }

        _lastMotionSamplePosition = currentPosition;
    }

    private void SyncBlockedSlotsWithAlgorithm()
    {
        ColliderSurfaceGridZone gridZone = ResolveGridZone();
        if (gridZone == null)
            return;

        ColliderSurfaceGridAlgorithm algorithm = gridZone.ResolveAlgorithm();
        algorithm?.SetBlockedSlots(_blockedSlotKeys);
    }

    private void ScanObstacleContactsBatch()
    {
        if (!useBatchedObstacleProbe)
            return;

        int obstacleLayerMask = ResolveObstacleProbeMask();
        if (obstacleLayerMask == 0)
            return;

        CollectActiveAliveEnemies(_activeEnemiesBuffer);
        int activeCount = _activeEnemiesBuffer.Count;
        if (activeCount <= 0)
        {
            _nextObstacleProbeStartIndex = 0;
            return;
        }

        EnsureObstacleProbeHitBuffer();

        int batchSize = Mathf.Min(Mathf.Max(1, obstacleProbeBatchSize), activeCount);
        int startIndex = Mathf.Clamp(_nextObstacleProbeStartIndex, 0, Mathf.Max(0, activeCount - 1));

        for (int offset = 0; offset < batchSize; offset++)
        {
            int enemyIndex = (startIndex + offset) % activeCount;
            Enemy enemy = _activeEnemiesBuffer[enemyIndex];
            if (enemy == null || !enemy.IsAlive)
                continue;

            if (!enemy.TryGetObstacleProbeCapsule(
                    obstacleProbeExtraRadius,
                    out Vector3 point0,
                    out Vector3 point1,
                    out float radius))
            {
                continue;
            }

            int hitCount = Physics.OverlapCapsuleNonAlloc(
                point0,
                point1,
                radius,
                _obstacleProbeHits,
                obstacleLayerMask,
                QueryTriggerInteraction.Collide);

            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                Collider obstacle = _obstacleProbeHits[hitIndex];
                if (obstacle == null)
                    continue;

                if (enemy.TryHandleObstacleProbe(obstacle, Time.frameCount, obstacleProbeCooldownFrames))
                    break;
            }
        }

        _nextObstacleProbeStartIndex = (startIndex + batchSize) % activeCount;
    }

    private void EnsureObstacleProbeHitBuffer()
    {
        int desiredSize = Mathf.Max(1, obstacleProbeHitBufferSize);
        if (_obstacleProbeHits != null && _obstacleProbeHits.Length == desiredSize)
            return;

        _obstacleProbeHits = new Collider[desiredSize];
    }

    private int ResolveObstacleProbeMask()
    {
        EnsureObstacleProbeLayer();
        return obstacleProbeLayers.value;
    }

    private void EnsureObstacleProbeLayer()
    {
        if (obstacleProbeLayers.value != 0)
            return;

        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        if (obstacleLayer < 0)
            return;

        obstacleProbeLayers = 1 << obstacleLayer;
    }

    private static bool TryResolveObstaclePlacement(
        ColliderSurfaceGridAlgorithm algorithm,
        uint seed,
        uint salt,
        ObstacleSlotSelectionMode selectionMode,
        out ColliderSurfaceGridPlacement placement,
        out bool shouldReservePlacement)
    {
        placement = default;
        shouldReservePlacement = false;

        switch (selectionMode)
        {
            case ObstacleSlotSelectionMode.OccupiedOnly:
                return algorithm.TryGetRandomOccupiedPlacement(seed, salt, null, out placement);

            case ObstacleSlotSelectionMode.AnyUnlocked:
                if (!algorithm.TryGetRandomPlacement(seed, salt, null, out placement))
                    return false;

                shouldReservePlacement = !algorithm.IsSlotOccupied(placement.Key);
                return true;

            case ObstacleSlotSelectionMode.EmptyOnly:
            default:
                if (!algorithm.TryGetRandomAvailablePlacement(seed, salt, null, out placement))
                    return false;

                shouldReservePlacement = true;
                return true;
        }
    }

    private void ReassignActiveEnemiesToSlots(ColliderSurfaceGridAlgorithm algorithm)
    {
        _activeEnemiesBuffer.Clear();
        _unassignedEnemiesBuffer.Clear();
        _overflowEnemiesBuffer.Clear();

        CollectActiveAliveEnemies(_activeEnemiesBuffer);

        for (int i = 0; i < _activeEnemiesBuffer.Count; i++)
        {
            Enemy enemy = _activeEnemiesBuffer[i];
            if (enemy == null)
                continue;

            if (TrySnapReservedEnemyToSlot(algorithm, enemy))
                continue;

            _unassignedEnemiesBuffer.Add(enemy);
        }

        for (int i = 0; i < _unassignedEnemiesBuffer.Count; i++)
        {
            Enemy enemy = _unassignedEnemiesBuffer[i];
            if (enemy == null)
                continue;

            if (TryAssignEnemyToAvailableSlot(algorithm, enemy))
                continue;

            _overflowEnemiesBuffer.Add(enemy);
        }

        for (int i = 0; i < _overflowEnemiesBuffer.Count; i++)
        {
            Enemy overflowEnemy = _overflowEnemiesBuffer[i];
            overflowEnemy?.DespawnForRecycle();
        }
    }

    private void CollectActiveAliveEnemies(List<Enemy> results)
    {
        if (results == null)
            return;

        results.Clear();

        Transform root = transform;
        int childCount = root.childCount;

        for (int i = 0; i < childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;

            if (!child.TryGetComponent(out Enemy enemy) || !enemy.IsAlive)
                continue;

            results.Add(enemy);
        }
    }

    private bool TrySnapReservedEnemyToSlot(ColliderSurfaceGridAlgorithm algorithm, Enemy enemy)
    {
        if (algorithm == null || enemy == null)
            return false;

        if (!enemy.TryGetComponent(out SpawnGridSlotReservation reservation) || !reservation.IsBound)
            return false;

        if (!algorithm.TryGetPlacementPose(reservation.SlotKey, out Vector3 worldPosition, out Quaternion worldRotation))
        {
            reservation.ReleaseReservationNow();
            return false;
        }

        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Quaternion localRotation = Quaternion.Inverse(transform.rotation) * worldRotation;

        if (enemy.transform.parent != transform)
            enemy.transform.SetParent(transform, true);

        enemy.SnapToGridSlot(localPosition, localRotation);
        return true;
    }

    private bool TryAssignEnemyToAvailableSlot(ColliderSurfaceGridAlgorithm algorithm, Enemy enemy)
    {
        if (algorithm == null || enemy == null)
            return false;

        if (!algorithm.TryGetNearestAvailablePlacement(enemy.transform.position, null, out ColliderSurfaceGridPlacement placement))
            return false;

        algorithm.BindReservation(enemy.gameObject, in placement);

        Vector3 localPosition = transform.InverseTransformPoint(placement.Position);
        Quaternion localRotation = Quaternion.Inverse(transform.rotation) * placement.Rotation;

        if (enemy.transform.parent != transform)
            enemy.transform.SetParent(transform, true);

        enemy.SnapToGridSlot(localPosition, localRotation);
        return true;
    }

    private static Vector3 ResolveSlotOffset(Vector3 slotLocalPosition, float nearbyDistance)
    {
        if (nearbyDistance <= 0f)
            return Vector3.zero;

        Vector3 planarDirection = new Vector3(slotLocalPosition.x, 0f, slotLocalPosition.z);
        if (planarDirection.sqrMagnitude <= 0.0001f)
        {
            planarDirection = Random.insideUnitSphere;
            planarDirection.y = 0f;
        }

        planarDirection = planarDirection.sqrMagnitude > 0.0001f
            ? planarDirection.normalized
            : Vector3.right;

        return planarDirection * nearbyDistance;
    }

    private static int AddClamped(int currentValue, int delta)
    {
        if (delta <= 0)
            return Mathf.Max(0, currentValue);

        long total = (long)Mathf.Max(0, currentValue) + delta;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }
}
