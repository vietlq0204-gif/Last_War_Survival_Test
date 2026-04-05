using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Components;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.ScriptableObjects;
using Vit.SpawnKit.Services;

public class Player : CoreEventBase
{
    /// <summary>
    /// SpawnPreset duoc dung de cau hinh cach spawn teammate cho Player.
    /// </summary>
    [Header("Cau hinh spawn teammate")]
    [SerializeField] private SpawnPresetSO teammateSpawnPreset;

    /// <summary>
    /// Parent de gom teammate sau khi spawn. Bo trong se dung chinh transform cua Player.
    /// </summary>
    [SerializeField] private Transform teammateSpawnParent;

    /// <summary>
    /// Zone component quy dinh collider va cau hinh grid de teammate duoc phan bo deu xung quanh Player.
    /// </summary>
    [SerializeField] private ColliderSurfaceGridZone teammateSpawnGridZone;

    /// <summary>
    /// So teammate toi da duoc spawn trong mot frame de tranh spike.
    /// </summary>
    [SerializeField, Min(1)] private int maxSpawnPerFrame = 4;

    /// <summary>
    /// So slot pool du phong de giam tan suat pool phai mo rong.
    /// </summary>
    [SerializeField, Min(0)] private int poolSizePadding = 8;

    /// <summary>
    /// Neu bat, Player se prewarm pool teammate ngay tu luc Start.
    /// </summary>
    [SerializeField] private bool prewarmPoolOnStart = true;

    /// <summary>
    /// Coroutine dang xu ly queue spawn teammate qua SpawnAsync.
    /// </summary>
    private Coroutine _spawnTeammateRoutine;

    /// <summary>
    /// Tong so teammate con cho duoc spawn tu cac event da nhan.
    /// </summary>
    private int _pendingTeammateSpawnCount;

    /// <summary>
    /// Kich thuoc pool lon nhat da duoc chuan bi cho spawnable hien tai.
    /// </summary>
    private int _preparedPoolSize;

    /// <summary>
    /// Spawnable da duoc dung de cache thong tin pool.
    /// </summary>
    private SpawnableSO _preparedPoolSpawnable;

    /// <summary>
    /// Chan warning thieu cau hinh bi log lap lai.
    /// </summary>
    private bool _hasWarnedMissingSpawnPreset;
    private bool _hasWarnedMissingSpawnZone;
    private bool _hasWarnedMissingSpawnManager;

    /// <summary>
    /// Token dung de huy request SpawnAsync dang cho khi Player bi disable.
    /// </summary>
    private CancellationTokenSource _spawnCancellationSource;

    private void Start()
    {
        if (prewarmPoolOnStart)
            PrepareTeammatePool(ResolveSafeMaxSpawnPerFrame());
    }

    private void OnDisable()
    {
        if (_spawnTeammateRoutine != null)
        {
            StopCoroutine(_spawnTeammateRoutine);
            _spawnTeammateRoutine = null;
        }

        CancelSpawnRequests();
        _pendingTeammateSpawnCount = 0;
    }

    public override void SubscribeEvents()
    {
        CoreEvents.collition.Subscribe(HandleCollitionEvent, Binder);
    }

    /// <summary>
    /// Nhan event tu CardAddQuantity va queue spawn teammate neu data card hop le.
    /// </summary>
    private void HandleCollitionEvent(CollitionEvent collitionEvent)
    {
        if (collitionEvent == null) return;
        if (collitionEvent.collitionType != CollitionEvent.CollitionType.Trigger) return;
        if (collitionEvent.collitionTag != CollitionEvent.CollitionTag.Player) return;
        if (!collitionEvent.hasValidCardAddQuantityData) return;

        QueueSpawnTeammates(collitionEvent.cardAddQuantityData);
    }

    /// <summary>
    /// Cong don teammate can spawn tu data card va dam bao coroutine xu ly queue dang chay.
    /// </summary>
    private void QueueSpawnTeammates(CardAddQuantitySO cardData)
    {
        if (cardData == null || !cardData.HasValidData) return;
        if (!CanSpawnTeammates()) return;

        _pendingTeammateSpawnCount += cardData.TeammateSpawnCount;

        var gridZone = ResolveTeammateSpawnGridZone();
        int occupiedSlots = gridZone != null ? gridZone.OccupiedSlotCount : 0;
        PrepareTeammatePool(occupiedSlots + _pendingTeammateSpawnCount);

        Debug.Log(
            $"Player '{name}' nhan CollitionEvent voi data '{cardData.name}', queue spawn them {cardData.TeammateSpawnCount} teammate. Pending: {_pendingTeammateSpawnCount}.",
            this);

        if (_spawnTeammateRoutine == null)
            _spawnTeammateRoutine = StartCoroutine(SpawnTeammatesRoutine());
    }

    /// <summary>
    /// Spawn teammate bang SpawnAsync va chi lay cac slot grid con trong cua teammateSpawnZone.
    /// </summary>
    private IEnumerator SpawnTeammatesRoutine()
    {
        while (_pendingTeammateSpawnCount > 0)
        {
            if (!CanSpawnTeammates())
            {
                _pendingTeammateSpawnCount = 0;
                break;
            }

            var gridZone = ResolveTeammateSpawnGridZone();
            var gridAlgorithm = gridZone != null ? gridZone.ResolveAlgorithm() : null;
            if (gridZone == null || gridAlgorithm == null)
            {
                _pendingTeammateSpawnCount = 0;
                break;
            }

            int availableSlotCount = gridZone.GetAvailableSlotCount();
            if (availableSlotCount <= 0)
            {
                yield return null;
                continue;
            }

            int requestCount = Mathf.Min(_pendingTeammateSpawnCount, availableSlotCount);
            PrepareTeammatePool(gridZone.OccupiedSlotCount + requestCount);

            Task<SpawnHandle> spawnTask = SpawnKit.SpawnAsync(
                CreateTeammateSpawnRequest(requestCount, gridAlgorithm),
                ResolveSafeMaxSpawnPerFrame(),
                ResolveSpawnCancellationToken());

            yield return new WaitUntil(() => spawnTask.IsCompleted);

            if (spawnTask.IsCanceled)
                break;

            if (spawnTask.IsFaulted)
            {
                Debug.LogException(spawnTask.Exception?.GetBaseException() ?? spawnTask.Exception, this);
                _pendingTeammateSpawnCount = 0;
                break;
            }

            var handle = spawnTask.Result;
            int spawnedCount = handle != null ? handle.Instances.Count : 0;
            if (spawnedCount <= 0)
            {
                Debug.LogWarning(
                    "Player khong spawn duoc teammate vao teammateSpawnGridZone. Kiem tra lai teammateSpawnPreset, teammateSpawnGridZone hoac pool config.",
                    this);
                _pendingTeammateSpawnCount = 0;
                break;
            }

            _pendingTeammateSpawnCount = Mathf.Max(0, _pendingTeammateSpawnCount - spawnedCount);

            Debug.Log(
                $"Player '{name}' da spawn {spawnedCount} teammate vao zone '{gridZone.name}'. Con lai trong queue: {_pendingTeammateSpawnCount}.",
                this);
        }

        _spawnTeammateRoutine = null;
    }

    /// <summary>
    /// Chuan bi truoc pool teammate de giam chi phi grow khi spawn batch.
    /// </summary>
    private void PrepareTeammatePool(int targetTotalCount)
    {
        var teammateSpawnable = ResolveTeammateSpawnable();
        if (teammateSpawnable == null) return;

        if (_preparedPoolSpawnable != teammateSpawnable)
        {
            _preparedPoolSpawnable = teammateSpawnable;
            _preparedPoolSize = 0;
        }

        int safeMaxSpawnPerFrame = ResolveSafeMaxSpawnPerFrame();
        int desiredPoolSize = Mathf.Max(1, targetTotalCount + poolSizePadding);
        if (desiredPoolSize <= _preparedPoolSize) return;

        int prewarmCount = Mathf.Clamp(Mathf.Max(safeMaxSpawnPerFrame, targetTotalCount), 0, desiredPoolSize);
        int growStep = safeMaxSpawnPerFrame;

        if (!SpawnKit.EnsurePoolCapacity(
            teammateSpawnable,
            desiredPoolSize,
            prewarmCount,
            growStep,
            allowGrow: true))
            return;

        _preparedPoolSize = desiredPoolSize;
    }

    /// <summary>
    /// Xac dinh Player da co du cau hinh de spawn teammate qua SpawnAsync hay chua.
    /// </summary>
    private bool CanSpawnTeammates()
    {
        if (teammateSpawnPreset == null || teammateSpawnPreset.spawnable == null)
        {
            if (!_hasWarnedMissingSpawnPreset)
            {
                _hasWarnedMissingSpawnPreset = true;
                Debug.LogWarning("Player chua duoc gan teammateSpawnPreset hop le nen khong the spawn teammate.", this);
            }

            return false;
        }

        _hasWarnedMissingSpawnPreset = false;

        if (ResolveSpawnManager() == null)
        {
            if (!_hasWarnedMissingSpawnManager)
            {
                _hasWarnedMissingSpawnManager = true;
                Debug.LogWarning("Khong tim thay SpawnManager trong scene nen Player khong the SpawnAsync teammate.", this);
            }

            return false;
        }

        _hasWarnedMissingSpawnManager = false;
        return ResolveTeammateSpawnGridZone() != null;
    }

    /// <summary>
    /// Tao SpawnRequest tu preset hien tai va grid algorithm dang giu trang thai occupied slot.
    /// </summary>
    private SpawnRequest CreateTeammateSpawnRequest(int teammateCount, ColliderSurfaceGridAlgorithm gridAlgorithm)
    {
        SpawnLifecycle? lifecycle = teammateSpawnPreset != null && teammateSpawnPreset.overrideLifecycle
            ? teammateSpawnPreset.lifecycle
            : (SpawnLifecycle?)null;

        var teammateSpawnable = ResolveTeammateSpawnable();
        int[] variantPlan = teammateSpawnable != null
            ? teammateSpawnable.BuildVariantPlan(teammateCount)
            : null;

        return new SpawnRequest(
            teammateSpawnable,
            teammateCount,
            ResolveTeammateSpawnParent(),
            gridAlgorithm,
            teammateSpawnPreset != null ? teammateSpawnPreset.seed : 0,
            lifecycle,
            variantPlan);
    }

    /// <summary>
    /// Parent mac dinh de chua teammate neu nguoi dung chua gan rieng.
    /// </summary>
    private Transform ResolveTeammateSpawnParent()
    {
        return teammateSpawnParent != null ? teammateSpawnParent : transform;
    }

    /// <summary>
    /// Khoi tao hoac tai su dung grid algorithm cho teammateSpawnZone.
    /// </summary>
    private ColliderSurfaceGridZone ResolveTeammateSpawnGridZone()
    {
        if (teammateSpawnGridZone == null)
            teammateSpawnGridZone = GetComponentInChildren<ColliderSurfaceGridZone>();

        if (teammateSpawnGridZone == null)
        {
            if (!_hasWarnedMissingSpawnZone)
            {
                _hasWarnedMissingSpawnZone = true;
                Debug.LogWarning("Player chua duoc gan teammateSpawnGridZone hop le nen khong the phan bo teammate theo grid zone.", this);
            }

            return null;
        }

        var algorithm = teammateSpawnGridZone.ResolveAlgorithm();
        if (algorithm != null)
        {
            _hasWarnedMissingSpawnZone = false;
            return teammateSpawnGridZone;
        }

        if (!_hasWarnedMissingSpawnZone)
        {
            _hasWarnedMissingSpawnZone = true;
            Debug.LogWarning(
                "teammateSpawnGridZone ton tai nhung khong resolve duoc collider/grid algorithm. Kiem tra lai zoneCollider va cau hinh grid.",
                this);
        }

        return null;
    }

    /// <summary>
    /// Lay SpawnManager runtime hien tai de xac nhan scene co service spawn.
    /// </summary>
    private SpawnManager ResolveSpawnManager()
    {
        if (SpawnManager.Instance != null) return SpawnManager.Instance;
        return FindAnyObjectByType<SpawnManager>();
    }

    /// <summary>
    /// Lay Spawnable duoc khai bao trong preset teammate hien tai.
    /// </summary>
    private SpawnableSO ResolveTeammateSpawnable()
    {
        return teammateSpawnPreset != null ? teammateSpawnPreset.spawnable : null;
    }

    /// <summary>
    /// Dam bao batch size luon hop le ngay ca khi scene/prefab bi sua sai du lieu luc runtime.
    /// </summary>
    private int ResolveSafeMaxSpawnPerFrame()
    {
        return Mathf.Max(1, maxSpawnPerFrame);
    }

    /// <summary>
    /// Tao token moi khi can de co the huy SpawnAsync luc Player bi disable.
    /// </summary>
    private CancellationToken ResolveSpawnCancellationToken()
    {
        if (_spawnCancellationSource != null && !_spawnCancellationSource.IsCancellationRequested)
            return _spawnCancellationSource.Token;

        _spawnCancellationSource?.Dispose();
        _spawnCancellationSource = new CancellationTokenSource();
        return _spawnCancellationSource.Token;
    }

    /// <summary>
    /// Huy request SpawnAsync dang cho va giai phong token cu.
    /// </summary>
    private void CancelSpawnRequests()
    {
        if (_spawnCancellationSource == null) return;

        if (!_spawnCancellationSource.IsCancellationRequested)
            _spawnCancellationSource.Cancel();

        _spawnCancellationSource.Dispose();
        _spawnCancellationSource = null;
    }
}
