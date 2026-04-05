using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Api;
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
    /// Buffer tai su dung de nhan ket qua SpawnNonAlloc, tranh tao List moi moi batch.
    /// </summary>
    private readonly List<GameObject> _spawnResultsBuffer = new List<GameObject>(32);

    /// <summary>
    /// Thuat toan pose don gian de spawn teammate ngay tai vi tri hien tai cua Player.
    /// </summary>
    private readonly TeammateSpawnPoseAlgorithm _spawnPoseAlgorithm = new TeammateSpawnPoseAlgorithm();

    /// <summary>
    /// Coroutine dang xu ly queue spawn teammate nhieu frame.
    /// </summary>
    private Coroutine _spawnTeammateRoutine;

    /// <summary>
    /// Tong so teammate con cho duoc spawn tu cac event da nhan.
    /// </summary>
    private int _pendingTeammateSpawnCount;

    /// <summary>
    /// Tong so teammate da spawn thanh cong trong runtime hien tai.
    /// </summary>
    private int _spawnedTeammateCount;

    /// <summary>
    /// Kich thuoc pool lon nhat da duoc chuan bi cho spawnable hien tai.
    /// </summary>
    private int _preparedPoolSize;

    /// <summary>
    /// Spawnable da duoc dung de cache thong tin pool.
    /// </summary>
    private SpawnableSO _preparedPoolSpawnable;

    /// <summary>
    /// Chan warning thieu preset hoac spawnable bi log lap lai.
    /// </summary>
    private bool _hasWarnedMissingSpawnPreset;

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

        _pendingTeammateSpawnCount = 0;
        _spawnResultsBuffer.Clear();
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
        PrepareTeammatePool(_spawnedTeammateCount + _pendingTeammateSpawnCount);

        Debug.Log(
            $"Player '{name}' nhan CollitionEvent voi data '{cardData.name}', queue spawn them {cardData.TeammateSpawnCount} teammate. Pending: {_pendingTeammateSpawnCount}.",
            this);

        if (_spawnTeammateRoutine == null)
            _spawnTeammateRoutine = StartCoroutine(SpawnTeammatesRoutine());
    }

    /// <summary>
    /// Spawn teammate theo batch qua nhieu frame de tranh spike.
    /// </summary>
    private IEnumerator SpawnTeammatesRoutine()
    {
        while (_pendingTeammateSpawnCount > 0)
        {
            int batchCount = Mathf.Min(ResolveSafeMaxSpawnPerFrame(), _pendingTeammateSpawnCount);
            int spawnedCount = SpawnTeammateBatch(batchCount);

            if (spawnedCount <= 0)
            {
                Debug.LogWarning("Player khong spawn duoc teammate. Kiem tra lai teammateSpawnPreset hoac pool config.", this);
                _pendingTeammateSpawnCount = 0;
                break;
            }

            _pendingTeammateSpawnCount = Mathf.Max(0, _pendingTeammateSpawnCount - spawnedCount);
            _spawnedTeammateCount += spawnedCount;

            Debug.Log(
                $"Player '{name}' da spawn {spawnedCount} teammate tu preset '{teammateSpawnPreset.name}'. Con lai trong queue: {_pendingTeammateSpawnCount}.",
                this);

            if (_pendingTeammateSpawnCount > 0)
                yield return null;
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
    /// Xac dinh Player da co cau hinh preset va spawnable can thiet de spawn teammate hay chua.
    /// </summary>
    private bool CanSpawnTeammates()
    {
        if (teammateSpawnPreset != null && teammateSpawnPreset.spawnable != null) return true;
        if (_hasWarnedMissingSpawnPreset) return false;

        _hasWarnedMissingSpawnPreset = true;
        Debug.LogWarning("Player chua duoc gan teammateSpawnPreset hop le nen khong the spawn teammate.", this);
        return false;
    }

    /// <summary>
    /// Tao SpawnRequest tu preset hien tai nhung ghi de count bang so teammate can spawn trong batch.
    /// </summary>
    private SpawnRequest CreateTeammateSpawnRequest(int teammateCount)
    {
        _spawnPoseAlgorithm.SetPose(transform.position, transform.rotation);

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
            _spawnPoseAlgorithm,
            teammateSpawnPreset != null ? teammateSpawnPreset.seed : 0,
            lifecycle,
            variantPlan);
    }

    /// <summary>
    /// Spawn mot batch teammate thong qua SpawnRequest duoc tao tu SpawnPresetSO.
    /// </summary>
    private int SpawnTeammateBatch(int batchCount)
    {
        var spawnManager = ResolveSpawnManager();
        if (spawnManager == null) return 0;

        _spawnResultsBuffer.Clear();
        return spawnManager.SpawnNonAlloc(CreateTeammateSpawnRequest(batchCount), _spawnResultsBuffer);
    }

    /// <summary>
    /// Parent mac dinh de chua teammate neu nguoi dung chua gan rieng.
    /// </summary>
    private Transform ResolveTeammateSpawnParent()
    {
        return teammateSpawnParent != null ? teammateSpawnParent : transform;
    }

    /// <summary>
    /// Lay SpawnManager runtime hien tai de gui SpawnRequest khong alloc.
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
    /// Thuat toan pose toi gian de tai su dung cung mot diem spawn tai vi tri hien tai cua Player.
    /// </summary>
    private sealed class TeammateSpawnPoseAlgorithm : ISpawnAlgorithm
    {
        private Vector3 _position;
        private Quaternion _rotation = Quaternion.identity;

        public void SetPose(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
        }

        public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
        {
            position = _position;
            rotation = _rotation;
        }
    }
}
