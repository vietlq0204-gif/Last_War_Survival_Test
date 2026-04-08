using System.Collections.Generic;
using UnityEngine;

public class EnemyGridGroup : ObjectSpawned
{
    [Tooltip("Ordered list of grid spawners managed by this group. If Auto Collect Child Grids is enabled, this list is rebuilt from children.")]
    [SerializeField] private EnemySpawner[] enemyGrids;

    [Tooltip("Automatically gather EnemySpawner children and keep them ordered by sibling index.")]
    [SerializeField] private bool autoCollectChildGrids = true;

    [Tooltip("Local offset between consecutive grids in the row. Editing this in the Inspector immediately relayouts the row.")]
    [SerializeField] private Vector3 gridLocalOffset = new Vector3(0f, 0f, -8f);

    [Tooltip("Apply the row layout when the component awakens.")]
    [SerializeField] private bool layoutGridsOnAwake = true;

    [Tooltip("If enabled, every managed grid tries to fill all available slots on Start.")]
    [SerializeField] private bool fillAllGridsOnStart = true;

    [Tooltip("Collider used as the recycle trigger. When a managed grid exits this trigger, it is recycled to the start of the lane and refilled.")]
    [SerializeField] private Collider recycleTriggerCollider;

    [Tooltip("Write recycle trigger and loop diagnostics to the Console.")]
    [SerializeField] private bool debugRecycleLogs = true;

    private readonly List<EnemySpawner> _orderedGrids = new List<EnemySpawner>(8);
    private readonly List<ObjectSpawnInstruction> _pathEntriesBuffer = new List<ObjectSpawnInstruction>(8);
    private Collider _fallbackRecycleTriggerCollider;
    private readonly Dictionary<EnemySpawner, bool> _gridInsideTriggerStates = new Dictionary<EnemySpawner, bool>(8);
    private readonly List<EnemySpawner> _pendingRecycleGrids = new List<EnemySpawner>(4);
    private ObjectOnPathController _pathController;
    private EnemyGridGroupPathLane _owningLane;
    private float _pathMoveSpeed;
    private float _pathStartDistance;
    private EntityId _pathSpawnZoneId;

    public bool UsesPathRuntime => _pathController != null;

    protected override void Awake()
    {
        base.Awake();
        RebuildGridCache();

        if (layoutGridsOnAwake)
            ApplyGridLayout();
    }

    private void Start()
    {
        if (fillAllGridsOnStart)
            FillAllGrids();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            if (autoCollectChildGrids)
                RebuildGridCache();

            ApplyGridLayout();
            ResetRecycleTracking();
        }
    }

    private void LateUpdate()
    {
        if (_orderedGrids.Count == 0)
            return;

        var resolvedRecycleTriggerCollider = ResolveRecycleTriggerCollider();
        if (resolvedRecycleTriggerCollider == null)
            return;

        ProcessPendingRecycleGrids();
        TrackGridTriggerTransitions(resolvedRecycleTriggerCollider);
    }

    public void SetFallbackRecycleTriggerCollider(Collider colliderReference)
    {
        _fallbackRecycleTriggerCollider = colliderReference;
        ResetRecycleTracking();
        LogDebug($"Fallback recycle trigger set to '{GetColliderLabel(colliderReference)}'.");
    }

    public void ConfigurePathRuntime(ObjectOnPathController controller, float moveSpeed, float startDistance, EntityId spawnZoneId)
    {
        ConfigurePathRuntime(controller, null, moveSpeed, startDistance, spawnZoneId);
    }

    public void ConfigurePathRuntime(
        ObjectOnPathController controller,
        EnemyGridGroupPathLane owningLane,
        float moveSpeed,
        float startDistance,
        EntityId spawnZoneId)
    {
        _pathController = controller;
        _owningLane = owningLane;
        _pathMoveSpeed = Mathf.Max(0.01f, moveSpeed);
        _pathStartDistance = Mathf.Max(0f, startDistance);
        _pathSpawnZoneId = spawnZoneId;
        ResetRecycleTracking();
        LogDebug(
            $"Configured path runtime. Trigger='{GetColliderLabel(ResolveRecycleTriggerCollider())}', startDistance={_pathStartDistance:0.###}, moveSpeed={_pathMoveSpeed:0.###}.");
    }

    public int AppendPathEntries(List<ObjectSpawnInstruction> entries, float startDistance, float moveSpeed, EntityId spawnZoneId)
    {
        if (entries == null)
            return 0;

        if (_orderedGrids.Count == 0)
            RebuildGridCache();

        float spacing = GetGridSpacingDistance();
        int addedCount = 0;

        for (int i = 0; i < _orderedGrids.Count; i++)
        {
            var grid = _orderedGrids[i];
            var pathItem = ResolvePathItem(grid);
            if (pathItem == null)
                continue;

            int distanceOrderIndex = (_orderedGrids.Count - 1) - i;
            float initialDistance = startDistance + spacing * distanceOrderIndex;

            entries.Add(new ObjectSpawnInstruction
            {
                SpawnedObject = pathItem,
                MoveSpeed = moveSpeed,
                InitialDistance = initialDistance,
                SpawnZoneId = spawnZoneId
            });

            LogDebug(
                $"Registered grid '{grid.name}' from hierarchyIndex={i} to initialDistance={initialDistance:0.###} (distanceOrderIndex={distanceOrderIndex}).",
                grid);

            addedCount++;
        }

        return addedCount;
    }

    public float GetPathSpan()
    {
        if (_orderedGrids.Count == 0)
            RebuildGridCache();

        return _orderedGrids.Count > 1
            ? GetGridSpacingDistance() * (_orderedGrids.Count - 1)
            : 0f;
    }

    public void HandlePathItemReachedEnd(EnemyGridPathItem pathItem)
    {
        if (pathItem == null)
            return;

        var grid = pathItem.Spawner;
        if (grid == null || !_orderedGrids.Contains(grid))
            return;

        LogDebug(
            $"Grid '{grid.name}' reached path end at worldPos={grid.transform.position}. Recycling to path start.",
            grid);
        HandleGridExitedTrigger(grid);
    }

    public void FillAllGrids()
    {
        if (_orderedGrids.Count == 0)
            RebuildGridCache();

        for (int i = 0; i < _orderedGrids.Count; i++)
        {
            var grid = _orderedGrids[i];
            if (grid != null)
                grid.FillAvailableSlots();
        }
    }

    public void ApplyGridLayout()
    {
        if (_orderedGrids.Count == 0)
            RebuildGridCache();

        for (int i = 0; i < _orderedGrids.Count; i++)
        {
            var grid = _orderedGrids[i];
            if (grid == null)
                continue;

            grid.transform.localPosition = gridLocalOffset * i;
        }
    }

    public int CopyOrderedGridsTo(List<EnemySpawner> destination)
    {
        if (destination == null)
            return 0;

        if (_orderedGrids.Count == 0)
            RebuildGridCache();

        destination.Clear();
        for (int i = 0; i < _orderedGrids.Count; i++)
        {
            var grid = _orderedGrids[i];
            if (grid != null)
                destination.Add(grid);
        }

        return destination.Count;
    }

    public float GetGridSpacingDistanceHint()
    {
        return GetGridSpacingDistance();
    }

    public bool RequestImmediateRecycle(EnemySpawner grid)
    {
        if (grid == null)
            return false;

        if (_orderedGrids.Count == 0)
            RebuildGridCache();

        if (!_orderedGrids.Contains(grid) || _pendingRecycleGrids.Contains(grid))
            return false;

        LogDebug($"Immediate recycle requested for empty grid '{grid.name}'.", grid);
        RecycleGrid(grid);
        return true;
    }

    public bool TryBuildPathEntry(
        EnemySpawner grid,
        float initialDistance,
        float moveSpeed,
        EntityId spawnZoneId,
        out ObjectSpawnInstruction entry)
    {
        entry = default;

        var pathItem = ResolvePathItem(grid);
        if (pathItem == null)
            return false;

        entry = new ObjectSpawnInstruction
        {
            SpawnedObject = pathItem,
            MoveSpeed = moveSpeed,
            InitialDistance = initialDistance,
            SpawnZoneId = spawnZoneId
        };
        return true;
    }

    private Collider ResolveRecycleTriggerCollider()
    {
        if (recycleTriggerCollider != null)
            return recycleTriggerCollider;

        return _fallbackRecycleTriggerCollider;
    }

    private void RebuildGridCache()
    {
        _orderedGrids.Clear();
        ResetRecycleTracking();

        if (autoCollectChildGrids || enemyGrids == null || enemyGrids.Length == 0)
        {
            enemyGrids = GetComponentsInChildren<EnemySpawner>(true);
        }

        if (enemyGrids == null || enemyGrids.Length == 0)
            return;

        for (int i = 0; i < enemyGrids.Length; i++)
        {
            var grid = enemyGrids[i];
            if (grid == null || grid.transform == transform)
                continue;

            _orderedGrids.Add(grid);
        }

        _orderedGrids.Sort(CompareGridOrder);
    }

    private void RecycleGrid(EnemySpawner grid)
    {
        if (grid == null)
            return;

        if (IsUsingPathRuntime())
        {
            RecycleGridOnPath(grid);
            return;
        }

        int gridIndex = _orderedGrids.IndexOf(grid);
        if (gridIndex < 0)
            return;

        _orderedGrids.RemoveAt(gridIndex);

        if (ShouldMoveGridToBack(gridIndex))
            _orderedGrids.Add(grid);
        else
            _orderedGrids.Insert(0, grid);

        ApplyGridLayout();
        _pendingRecycleGrids.Remove(grid);
        _gridInsideTriggerStates[grid] = false;

        grid.RecycleAndRefillSlots();
    }

    private void ProcessPendingRecycleGrids()
    {
        _pendingRecycleGrids.Clear();
    }

    private void TrackGridTriggerTransitions(Collider resolvedRecycleTriggerCollider)
    {
        EnemySpawner exitedGrid = null;

        for (int i = 0; i < _orderedGrids.Count; i++)
        {
            var grid = _orderedGrids[i];
            if (grid == null)
                continue;

            bool wasInsideTrigger = _gridInsideTriggerStates.TryGetValue(grid, out bool cachedState) && cachedState;
            bool isInsideTrigger = grid.Intersects(resolvedRecycleTriggerCollider);

            if (wasInsideTrigger != isInsideTrigger)
            {
                LogDebug(
                    $"Grid '{grid.name}' {(isInsideTrigger ? "entered" : "exited")} trigger '{GetColliderLabel(resolvedRecycleTriggerCollider)}'. gridPos={grid.transform.position}, triggerPos={resolvedRecycleTriggerCollider.transform.position}.",
                    grid);
            }

            if (exitedGrid == null && wasInsideTrigger && !isInsideTrigger)
                exitedGrid = grid;

            _gridInsideTriggerStates[grid] = isInsideTrigger;
        }

        if (exitedGrid != null)
            HandleGridExitedTrigger(exitedGrid);
    }

    private void HandleGridExitedTrigger(EnemySpawner grid)
    {
        if (grid == null || _pendingRecycleGrids.Contains(grid))
            return;

        LogDebug(
            $"Recycle requested for grid '{grid.name}' after passing trigger '{GetColliderLabel(ResolveRecycleTriggerCollider())}'. hasSpawnedObjectsInside={grid.HasSpawnedObjectsInside()}.",
            grid);
        RecycleGrid(grid);
    }

    private void RecycleGridOnPath(EnemySpawner grid)
    {
        if (_pathController == null)
            return;

        if (_owningLane != null && _owningLane.TryEnqueueGridRecycle(this, grid))
        {
            _pendingRecycleGrids.Remove(grid);
            _gridInsideTriggerStates[grid] = false;

            LogDebug(
                $"Grid '{grid.name}' queued for recycle to path start distance={_pathStartDistance:0.###}.",
                grid);
            return;
        }

        var pathItem = ResolvePathItem(grid);
        if (pathItem == null)
            return;

        _pathEntriesBuffer.Clear();
        _pathEntriesBuffer.Add(new ObjectSpawnInstruction
        {
            SpawnedObject = pathItem,
            MoveSpeed = _pathMoveSpeed,
            InitialDistance = _pathStartDistance,
            SpawnZoneId = _pathSpawnZoneId
        });

        _pathController.RegisterSpawnedBatch(_pathEntriesBuffer);
        _pendingRecycleGrids.Remove(grid);
        _gridInsideTriggerStates[grid] = false;

        LogDebug(
            $"Grid '{grid.name}' recycled to path start distance={_pathStartDistance:0.###}. worldPosNow={grid.transform.position}.",
            grid);
        grid.RecycleAndRefillSlots();
    }

    private bool ShouldMoveGridToBack(int gridIndex)
    {
        int lastIndex = _orderedGrids.Count;
        if (lastIndex <= 0)
            return true;

        int currentLastIndex = lastIndex - 1;
        if (gridIndex <= 0)
            return true;

        if (gridIndex >= currentLastIndex)
            return false;

        return gridIndex <= currentLastIndex / 2;
    }

    private void ResetRecycleTracking()
    {
        _gridInsideTriggerStates.Clear();
        _pendingRecycleGrids.Clear();
    }

    private float GetGridSpacingDistance()
    {
        float spacing = gridLocalOffset.magnitude;
        return spacing > Mathf.Epsilon ? spacing : 0.01f;
    }

    private bool IsUsingPathRuntime()
    {
        return _pathController != null;
    }

    private EnemyGridPathItem ResolvePathItem(EnemySpawner grid)
    {
        if (grid == null)
            return null;

        var pathItem = grid.GetComponent<EnemyGridPathItem>();
        if (pathItem != null || !Application.isPlaying)
            return pathItem;

        return grid.gameObject.AddComponent<EnemyGridPathItem>();
    }

    private void LogDebug(string message, Object context = null)
    {
        if (!debugRecycleLogs)
            return;

        Debug.Log($"[EnemyGridGroup:{name}] {message}", context != null ? context : this);
    }

    private static string GetColliderLabel(Collider colliderReference)
    {
        return colliderReference != null ? colliderReference.name : "<null>";
    }

    private static int CompareGridOrder(EnemySpawner left, EnemySpawner right)
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
