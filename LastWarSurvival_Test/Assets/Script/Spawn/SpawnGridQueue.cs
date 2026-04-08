using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Components;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.ScriptableObjects;
using Vit.SpawnKit.Services;

public class SpawnGridQueue : CoreEventBase
{
    [Header("Spawn configuration")]
    [FormerlySerializedAs("teammateSpawnPreset")]
    [SerializeField] private SpawnPresetSO spawnPreset;

    [FormerlySerializedAs("teammateSpawnParent")]
    [SerializeField] private Transform spawnParent;

    [FormerlySerializedAs("teammateSpawnGridZone")]
    [SerializeField] private ColliderSurfaceGridZone spawnGridZone;

    [Header("Formation arrangement")]
    [FormerlySerializedAs("teammateInitialSpawnMode")]
    [SerializeField] private FormationSpawnInitialPoseMode initialSpawnMode =
        FormationSpawnInitialPoseMode.OwnSlot;

    [FormerlySerializedAs("teammateFormationSpawnTransform")]
    [SerializeField] private Transform formationSpawnTransform;

    [FormerlySerializedAs("teammateFormationSpawnCollider")]
    [SerializeField] private Collider formationSpawnCollider;

    [SerializeField, Min(1)] private int maxSpawnPerFrame = 4;
    [SerializeField, Min(0)] private int poolSizePadding = 8;
    [SerializeField] private bool prewarmPoolOnStart = true;

    private Coroutine _spawnRoutine;
    private int _pendingSpawnCount;
    private readonly List<SpawnableSO> _spawnablesBuffer = new List<SpawnableSO>(8);
    private readonly Dictionary<SpawnableSO, int> _preparedPoolSizes = new Dictionary<SpawnableSO, int>(8);
    private bool _hasWarnedMissingSpawnPreset;
    private bool _hasWarnedMissingSpawnZone;
    private bool _hasWarnedMissingSpawnManager;
    private bool _hasWarnedMissingFormationSpawnTransform;
    private bool _hasWarnedMissingFormationSpawnCollider;
    private CancellationTokenSource _spawnCancellationSource;

    protected virtual string SpawnedObjectLabel => "object";
    protected int PendingSpawnCount => _pendingSpawnCount;
    protected bool HasPendingSpawnRequests => _pendingSpawnCount > 0 || _spawnRoutine != null;

    protected void ResetPendingSpawnQueue()
    {
        if (_spawnRoutine != null)
        {
            StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }

        CancelSpawnRequests();
        _pendingSpawnCount = 0;
    }

    protected virtual void Start()
    {
        if (prewarmPoolOnStart)
            PreparePool(ResolveSafeMaxSpawnPerFrame());
    }

    protected virtual void OnDisable()
    {
        ResetPendingSpawnQueue();
    }

    public override void SubscribeEvents()
    {
    }

    public bool QueueSpawn(int count)
    {
        if (count <= 0) return false;
        if (!isActiveAndEnabled) return false;
        if (!CanSpawn()) return false;

        _pendingSpawnCount += count;

        var gridZone = ResolveSpawnGridZone();
        int occupiedSlots = gridZone != null ? gridZone.OccupiedSlotCount : 0;

        PreparePool(occupiedSlots + _pendingSpawnCount);

        // Debug.Log(
        //     $"'{name}' queued {count} {SpawnedObjectLabel} spawn(s). Pending: {_pendingSpawnCount}.",
        //     this);

        if (_spawnRoutine == null)
            _spawnRoutine = StartCoroutine(SpawnRoutine());

        return true;
    }

    public virtual int GetOccupiedSlotCount()
    {
        var gridZone = ResolveSpawnGridZone();
        return gridZone != null ? gridZone.OccupiedSlotCount : 0;
    }

    public virtual Collider GetFormationSpawnCollider()
    {
        return formationSpawnCollider;
    }

    public virtual Transform GetFormationSpawnTransform()
    {
        return formationSpawnTransform;
    }

    private IEnumerator SpawnRoutine()
    {
        while (_pendingSpawnCount > 0)
        {
            if (!CanSpawn())
            {
                _pendingSpawnCount = 0;
                break;
            }

            var gridZone = ResolveSpawnGridZone();
            var gridAlgorithm = gridZone != null ? gridZone.ResolveAlgorithm() : null;
            if (gridZone == null || gridAlgorithm == null)
            {
                _pendingSpawnCount = 0;
                break;
            }

            int availableSlotCount = gridZone.GetAvailableSlotCount();
            if (availableSlotCount <= 0)
            {
                yield return null;
                continue;
            }

            int spawnCapacity = ResolveSpawnCapacityForMode(availableSlotCount);
            if (spawnCapacity <= 0)
            {
                yield return null;
                continue;
            }

            int requestCount = Mathf.Min(_pendingSpawnCount, spawnCapacity);
            PreparePool(gridZone.OccupiedSlotCount + requestCount);

            Task<SpawnHandle> spawnTask = SpawnKit.SpawnAsync(
                CreateSpawnRequest(requestCount, gridZone, gridAlgorithm),
                ResolveSafeMaxSpawnPerFrame(),
                ResolveSpawnCancellationToken());

            yield return new WaitUntil(() => spawnTask.IsCompleted);

            if (spawnTask.IsCanceled)
                break;

            if (spawnTask.IsFaulted)
            {
                Debug.LogException(spawnTask.Exception?.GetBaseException() ?? spawnTask.Exception, this);
                _pendingSpawnCount = 0;
                break;
            }

            var handle = spawnTask.Result;
            int spawnedCount = handle != null ? handle.Instances.Count : 0;
            if (spawnedCount <= 0)
            {
                Debug.LogWarning(
                    $"'{name}' could not spawn any {SpawnedObjectLabel}. Check spawnPreset, spawnGridZone, and pool configuration.",
                    this);
                _pendingSpawnCount = 0;
                break;
            }

            _pendingSpawnCount = Mathf.Max(0, _pendingSpawnCount - spawnedCount);

            // Debug.Log(
            //     $"'{name}' spawned {spawnedCount} {SpawnedObjectLabel} into zone '{gridZone.name}'. Pending: {_pendingSpawnCount}.",
            //     this);
        }

        _spawnRoutine = null;
    }

    private void PreparePool(int targetTotalCount)
    {
        if (spawnPreset == null || !spawnPreset.HasSpawnables) return;

        spawnPreset.GetSpawnables(_spawnablesBuffer);
        if (_spawnablesBuffer.Count <= 0) return;

        int safeMaxSpawnPerFrame = ResolveSafeMaxSpawnPerFrame();
        int desiredPoolSize = Mathf.Max(1, targetTotalCount + poolSizePadding);
        int prewarmCount = Mathf.Clamp(Mathf.Max(safeMaxSpawnPerFrame, targetTotalCount), 0, desiredPoolSize);
        int growStep = safeMaxSpawnPerFrame;

        for (int i = 0; i < _spawnablesBuffer.Count; i++)
        {
            SpawnableSO spawnable = _spawnablesBuffer[i];
            if (spawnable == null) continue;

            _preparedPoolSizes.TryGetValue(spawnable, out int preparedPoolSize);
            if (desiredPoolSize <= preparedPoolSize) continue;

            if (!SpawnKit.EnsurePoolCapacity(
                    spawnable,
                    desiredPoolSize,
                    prewarmCount,
                    growStep,
                    allowGrow: true))
                continue;

            _preparedPoolSizes[spawnable] = desiredPoolSize;
        }
    }

    private bool CanSpawn()
    {
        if (spawnPreset == null || !spawnPreset.HasSpawnables)
        {
            if (!_hasWarnedMissingSpawnPreset)
            {
                _hasWarnedMissingSpawnPreset = true;
                Debug.LogWarning($"'{name}' is missing a valid spawnPreset.", this);
            }

            return false;
        }

        _hasWarnedMissingSpawnPreset = false;

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
        return ResolveSpawnGridZone() != null
               && ValidateInitialSpawnModeConfiguration();
    }

    private int ResolveSpawnCapacityForMode(int availableSlotCount)
    {
        availableSlotCount = Mathf.Max(0, availableSlotCount);

        switch (initialSpawnMode)
        {
            case FormationSpawnInitialPoseMode.RandomEmptySlot:
            case FormationSpawnInitialPoseMode.NearestEmptySlot:
                return (availableSlotCount + 1) / 2;

            case FormationSpawnInitialPoseMode.CenterSlot:
            case FormationSpawnInitialPoseMode.OwnSlot:
            case FormationSpawnInitialPoseMode.FixedTransform:
            case FormationSpawnInitialPoseMode.AuxiliaryColliderScatter:
            default:
                return availableSlotCount;
        }
    }

    private bool ValidateInitialSpawnModeConfiguration()
    {
        switch (initialSpawnMode)
        {
            case FormationSpawnInitialPoseMode.FixedTransform:
                _hasWarnedMissingFormationSpawnCollider = false;

                if (formationSpawnTransform != null)
                {
                    _hasWarnedMissingFormationSpawnTransform = false;
                    return true;
                }

                if (!_hasWarnedMissingFormationSpawnTransform)
                {
                    _hasWarnedMissingFormationSpawnTransform = true;
                    Debug.LogWarning($"'{name}' needs formationSpawnTransform when using FixedTransform.", this);
                }

                return false;

            case FormationSpawnInitialPoseMode.AuxiliaryColliderScatter:
                _hasWarnedMissingFormationSpawnTransform = false;

                if (formationSpawnCollider != null)
                {
                    _hasWarnedMissingFormationSpawnCollider = false;
                    return true;
                }

                if (!_hasWarnedMissingFormationSpawnCollider)
                {
                    _hasWarnedMissingFormationSpawnCollider = true;
                    Debug.LogWarning($"'{name}' needs formationSpawnCollider when using AuxiliaryColliderScatter.", this);
                }

                return false;

            case FormationSpawnInitialPoseMode.CenterSlot:
            case FormationSpawnInitialPoseMode.OwnSlot:
            case FormationSpawnInitialPoseMode.RandomEmptySlot:
            case FormationSpawnInitialPoseMode.NearestEmptySlot:
            default:
                _hasWarnedMissingFormationSpawnTransform = false;
                _hasWarnedMissingFormationSpawnCollider = false;
                return true;
        }
    }

    private SpawnRequest CreateSpawnRequest(
        int spawnCount,
        ColliderSurfaceGridZone gridZone,
        ColliderSurfaceGridAlgorithm gridAlgorithm)
    {
        if (spawnPreset == null)
            return default;

        return spawnPreset.CreateRequest(
            spawnCount,
            CreateSpawnAlgorithm(spawnCount, gridZone, gridAlgorithm),
            ResolveSpawnParent());
    }

    private ISpawnAlgorithm CreateSpawnAlgorithm(
        int spawnCount,
        ColliderSurfaceGridZone gridZone,
        ColliderSurfaceGridAlgorithm gridAlgorithm)
    {
        if (gridZone == null || gridAlgorithm == null)
            return gridAlgorithm;

        var zoneCollider = gridZone.ZoneCollider;
        Vector3 fallbackPosition = zoneCollider != null ? zoneCollider.bounds.center : ResolveSpawnParent().position;
        Quaternion fallbackRotation = gridZone.transform.rotation;

        return new FormationSpawnToGridAlgorithm(
            gridAlgorithm,
            spawnCount,
            initialSpawnMode,
            CreateInitialSpawnPoseAlgorithm(spawnCount),
            fallbackPosition,
            fallbackRotation);
    }

    private ISpawnAlgorithm CreateInitialSpawnPoseAlgorithm(int spawnCount)
    {
        switch (initialSpawnMode)
        {
            case FormationSpawnInitialPoseMode.FixedTransform:
                return formationSpawnTransform != null
                    ? new FixedPoseAlgorithm(
                        formationSpawnTransform.position,
                        formationSpawnTransform.rotation)
                    : null;

            case FormationSpawnInitialPoseMode.AuxiliaryColliderScatter:
                return formationSpawnCollider != null
                    ? new ColliderVolumeAlgorithm(
                        formationSpawnCollider,
                        maxTryPerPoint: 32,
                        candidatesPerPoint: 16,
                        minDistance: 0.5f,
                        maxCount: Mathf.Max(1, spawnCount))
                    : null;

            case FormationSpawnInitialPoseMode.CenterSlot:
            case FormationSpawnInitialPoseMode.OwnSlot:
            case FormationSpawnInitialPoseMode.RandomEmptySlot:
            case FormationSpawnInitialPoseMode.NearestEmptySlot:
            default:
                return null;
        }
    }

    private Transform ResolveSpawnParent()
    {
        return spawnParent != null ? spawnParent : transform;
    }

    private ColliderSurfaceGridZone ResolveSpawnGridZone()
    {
        if (spawnGridZone == null)
            spawnGridZone = GetComponentInChildren<ColliderSurfaceGridZone>();

        if (spawnGridZone == null)
        {
            if (!_hasWarnedMissingSpawnZone)
            {
                _hasWarnedMissingSpawnZone = true;
                Debug.LogWarning($"'{name}' is missing a valid spawnGridZone.", this);
            }

            return null;
        }

        var algorithm = spawnGridZone.ResolveAlgorithm();
        if (algorithm != null)
        {
            _hasWarnedMissingSpawnZone = false;
            return spawnGridZone;
        }

        if (!_hasWarnedMissingSpawnZone)
        {
            _hasWarnedMissingSpawnZone = true;
            Debug.LogWarning(
                $"'{name}' has a spawnGridZone but it could not resolve a collider/grid algorithm.",
                this);
        }

        return null;
    }

    private SpawnManager ResolveSpawnManager()
    {
        if (SpawnManager.Instance != null) return SpawnManager.Instance;
        return FindAnyObjectByType<SpawnManager>();
    }

    private int ResolveSafeMaxSpawnPerFrame()
    {
        return Mathf.Max(1, maxSpawnPerFrame);
    }

    private CancellationToken ResolveSpawnCancellationToken()
    {
        if (_spawnCancellationSource != null && !_spawnCancellationSource.IsCancellationRequested)
            return _spawnCancellationSource.Token;

        _spawnCancellationSource?.Dispose();
        _spawnCancellationSource = new CancellationTokenSource();
        return _spawnCancellationSource.Token;
    }

    private void CancelSpawnRequests()
    {
        if (_spawnCancellationSource == null) return;

        if (!_spawnCancellationSource.IsCancellationRequested)
            _spawnCancellationSource.Cancel();

        _spawnCancellationSource.Dispose();
        _spawnCancellationSource = null;
    }
}
