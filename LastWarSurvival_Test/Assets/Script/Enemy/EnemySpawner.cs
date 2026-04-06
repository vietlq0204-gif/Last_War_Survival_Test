using UnityEngine;
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

    [Tooltip("If enabled, the linked grid zone is forced to use aligned slots so formation enemies stay on a strict grid.")]
    [SerializeField] private bool enforceAlignedGridSlots = true;

    private ColliderSurfaceGridZone _cachedGridZone;
    private Collider[] _overlapBuffer;

    private void OnValidate()
    {
        SyncGridZoneSettings();
    }

    protected override string SpawnedObjectLabel => "enemy";

    protected override void Start()
    {
        SyncGridZoneSettings();
        base.Start();

        if (fillAvailableSlotsOnStart)
            FillAvailableSlots();
    }

    public bool FillAvailableSlots()
    {
        int requestCount = Mathf.Max(0, GetAvailableSlotCount() - PendingSpawnCount);
        return requestCount > 0 && QueueSpawn(requestCount);
    }

    public int GetAvailableSlotCount()
    {
        var gridZone = ResolveGridZone();
        return gridZone != null ? gridZone.GetAvailableSlotCount() : 0;
    }

    public int GetOccupiedSlotCount()
    {
        var gridZone = ResolveGridZone();
        return gridZone != null ? gridZone.OccupiedSlotCount : 0;
    }

    public bool HasSpawnedObjectsInside()
    {
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

            return true;
        }

        return false;
    }

    public bool Intersects(Collider other)
    {
        var resolvedOccupancyCollider = ResolveOccupancyCollider();
        return resolvedOccupancyCollider != null
               && other != null
               && resolvedOccupancyCollider.bounds.Intersects(other.bounds);
    }

    private ColliderSurfaceGridZone ResolveGridZone()
    {
        if (_cachedGridZone != null)
        {
            SyncGridZoneSettings(_cachedGridZone);
            return _cachedGridZone;
        }

        _cachedGridZone = GetComponentInChildren<ColliderSurfaceGridZone>(true);
        SyncGridZoneSettings(_cachedGridZone);
        return _cachedGridZone;
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

    private void SyncGridZoneSettings()
    {
        SyncGridZoneSettings(_cachedGridZone != null ? _cachedGridZone : GetComponentInChildren<ColliderSurfaceGridZone>(true));
    }

    private void SyncGridZoneSettings(ColliderSurfaceGridZone gridZone)
    {
        if (gridZone == null || !enforceAlignedGridSlots)
            return;

        gridZone.RandomizeCellPositions = false;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(
            Mathf.Abs(value.x),
            Mathf.Abs(value.y),
            Mathf.Abs(value.z));
    }
}
