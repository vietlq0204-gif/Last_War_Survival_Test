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

/// <summary>
/// Điều phối luồng spawn teammate cho Player bằng SpawnAsync và grid zone.
/// Hiện tại class này vừa nghe event, vừa quản lý queue, vừa chuẩn bị pool nên vẫn còn gom nhiều trách nhiệm.
/// </summary>
public class Player : CoreEventBase
{
    /// <summary>
    /// Preset spawn teammate được dùng để xác định Spawnable, seed và lifecycle override.
    /// Trường này chưa có fallback an toàn nếu preset đổi sang spawnable không tương thích lúc runtime.
    /// </summary>
    [Header("Cấu hình spawn teammate")]
    [Tooltip("Preset dùng để spawn teammate. Cần có Spawnable hợp lệ để Player có thể tạo request SpawnAsync.")]
    [SerializeField] private SpawnPresetSO teammateSpawnPreset;

    /// <summary>
    /// Parent để chứa teammate sau khi spawn.
    /// Nếu để trống, code sẽ fallback sang transform của Player thay vì tạo root riêng.
    /// </summary>
    [Tooltip("Parent của teammate sau khi spawn. Nếu để trống, Player sẽ dùng chính transform của mình.")]
    [SerializeField] private Transform teammateSpawnParent;

    /// <summary>
    /// Zone grid dùng để phân bố teammate xung quanh Player.
    /// Nếu để trống, Player sẽ thử tìm trong child hierarchy; cách này tiện setup nhưng chưa tối ưu cho hierarchy sâu.
    /// </summary>
    [Tooltip("Zone grid dùng để phân bố teammate. Nếu để trống, Player sẽ thử tìm một ColliderSurfaceGridZone trong child.")]
    [SerializeField] private ColliderSurfaceGridZone teammateSpawnGridZone;

    [Header("Formation arrangement")]
    [Tooltip("Select how teammates appear before they settle into their assigned formation slots.")]
    [SerializeField] private FormationSpawnInitialPoseMode teammateInitialSpawnMode =
        FormationSpawnInitialPoseMode.OwnSlot;

    [Tooltip("Used when teammateInitialSpawnMode is FixedTransform.")]
    [SerializeField] private Transform teammateFormationSpawnTransform;

    [Tooltip("Used when teammateInitialSpawnMode is AuxiliaryColliderScatter.")]
    [SerializeField] private Collider teammateFormationSpawnCollider;

    /// <summary>
    /// Số teammate tối đa được spawn trong một frame.
    /// Giá trị này hiện đang canh chỉnh thủ công, chưa tự động tính theo cấu hình máy hay độ nặng prefab.
    /// </summary>
    [Tooltip("Số teammate tối đa được spawn mỗi frame. Tăng giá trị này sẽ spawn nhanh hơn nhưng dễ gây spike frame.")]
    [SerializeField, Min(1)] private int maxSpawnPerFrame = 4;

    /// <summary>
    /// Số slot pool dự phòng để giảm tần suất pool phải mở rộng.
    /// Hiện tại đây là hệ số cố định, chưa được estimate theo kích thước zone hoặc tốc độ spawn thực tế.
    /// </summary>
    [Tooltip("Số slot pool dự phòng để giảm việc pool phải mở rộng. Giá trị này hiện phải chỉnh tay.")]
    [SerializeField, Min(0)] private int poolSizePadding = 8;

    /// <summary>
    /// Xác định có prewarm pool teammate ngay khi Start hay không.
    /// Bật tùy chọn này giảm chi phí spawn lần đầu nhưng tăng chi phí khởi tạo scene và bộ nhớ lúc đầu.
    /// </summary>
    [Tooltip("Nếu bật, Player sẽ prewarm pool teammate từ Start. Giảm chi phí spawn lần đầu nhưng tốn thêm bộ nhớ lúc khởi tạo.")]
    [SerializeField] private bool prewarmPoolOnStart = true;

    /// <summary>
    /// Coroutine duy nhất được phép xử lý queue spawn teammate.
    /// Kiểu đồng bộ bằng coroutine này dễ đọc nhưng chưa tối ưu nếu sau này cần ưu tiên nhiều queue khác nhau.
    /// </summary>
    private Coroutine _spawnTeammateRoutine;

    /// <summary>
    /// Tổng số teammate còn chờ được spawn từ các event đã nhận.
    /// Biến này đang là counter đơn giản, chưa lưu metadata theo từng event spawn.
    /// </summary>
    private int _pendingTeammateSpawnCount;

    /// <summary>
    /// Kích thước pool lớn nhất đã được chuẩn bị cho spawnable hiện tại.
    /// Chỉ số này chỉ dùng cho một spawnable tại một thời điểm, chưa bao quát trường hợp đổi preset liên tục.
    /// </summary>
    private int _preparedPoolSize;

    /// <summary>
    /// Spawnable được dùng để cache thông tin pool.
    /// Cache này đơn giản và được reset khi thay spawnable, chưa lưu lịch sử cho nhiều loại teammate.
    /// </summary>
    private SpawnableSO _preparedPoolSpawnable;

    /// <summary>
    /// Chặn warning thiếu preset bị log lặp lại.
    /// Các cờ warning này chỉ giảm spam log, chưa tổng hợp thành một hệ thống validate tập trung.
    /// </summary>
    private bool _hasWarnedMissingSpawnPreset;

    /// <summary>
    /// Chặn warning thiếu grid zone bị log lặp lại.
    /// Cờ này không ghi nhớ nguyên nhân cụ thể, chỉ đánh dấu đã cảnh báo hay chưa.
    /// </summary>
    private bool _hasWarnedMissingSpawnZone;

    /// <summary>
    /// Chặn warning thiếu SpawnManager bị log lặp lại.
    /// Cách này đơn giản nhưng chưa biết được scene đã khởi tạo manager trễ hay thật sự thiếu.
    /// </summary>
    private bool _hasWarnedMissingSpawnManager;
    private bool _hasWarnedMissingFormationSpawnTransform;
    private bool _hasWarnedMissingFormationSpawnCollider;

    /// <summary>
    /// Token source dùng để hủy request SpawnAsync đang chờ khi Player bị disable.
    /// Hiện tại chỉ quản lý một token source chung cho cả queue spawn teammate.
    /// </summary>
    private CancellationTokenSource _spawnCancellationSource;

    /// <summary>
    /// Khởi tạo pool teammate sớm nếu người dùng bật prewarm.
    /// Bước này giúp giảm chi phí lúc event spawn đến lần đầu.
    /// </summary>
    private void Start()
    {
        if (prewarmPoolOnStart)
            PrepareTeammatePool(ResolveSafeMaxSpawnPerFrame());
    }

    /// <summary>
    /// Dừng coroutine spawn và hủy các request SpawnAsync đang chờ khi Player bị tắt.
    /// Hiện tại queue pending sẽ bị reset về 0 thay vì được khôi phục khi Player bật lại.
    /// </summary>
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

    /// <summary>
    /// Đăng ký event collision để Player có thể nhận trigger spawn teammate.
    /// Hiện tại class phụ thuộc trực tiếp vào CoreEvents.collition thay vì interface hóa event source.
    /// </summary>
    public override void SubscribeEvents()
    {
        CoreEvents.collition.Subscribe(HandleCollitionEvent, Binder);
    }

    /// <summary>
    /// Lọc CollitionEvent hợp lệ rồi đưa vào queue spawn teammate.
    /// Hàm này đang kiểm tra điều kiện bằng nhiều if sớm, dễ đọc nhưng chưa gom thành rule validate tái sử dụng.
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
    /// Cộng dồn teammate cần spawn vào queue và đảm bảo coroutine xử lý đang chạy.
    /// Mỗi event đều có thể gọi prewarm lại pool, cách này an toàn nhưng chưa tối ưu khi event đến liên tục với tần suất cao.
    /// </summary>
    private void QueueSpawnTeammates(CardAddQuantitySO cardData)
    {
        if (cardData == null || !cardData.HasValidData) return;
        if (!CanSpawnTeammates()) return;

        // Cộng dồn queue để nhiều event liên tiếp không tạo nhiều coroutine spawn song song.
        _pendingTeammateSpawnCount += cardData.TeammateSpawnCount;

        var gridZone = ResolveTeammateSpawnGridZone();
        int occupiedSlots = gridZone != null ? gridZone.OccupiedSlotCount : 0;

        // Chuẩn bị pool theo tổng slot đã chiếm và số object đang chờ spawn.
        PrepareTeammatePool(occupiedSlots + _pendingTeammateSpawnCount);

        Debug.Log(
            $"Player '{name}' nhận CollitionEvent với data '{cardData.name}', queue spawn thêm {cardData.TeammateSpawnCount} teammate. Pending: {_pendingTeammateSpawnCount}.",
            this);

        // Chỉ cho phép một coroutine xử lý queue spawn tại một thời điểm.
        if (_spawnTeammateRoutine == null)
            _spawnTeammateRoutine = StartCoroutine(SpawnTeammatesRoutine());
    }

    /// <summary>
    /// Xử lý queue spawn teammate bằng SpawnAsync và grid slot stateful.
    /// Khi zone đầy, coroutine sẽ poll mỗi frame bằng yield return null; cách này đơn giản nhưng chưa tối ưu cho queue rất dài.
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

            // Luồng chính: chỉ spawn vào các slot còn trống, nếu hết slot thì chờ frame sau.
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

            int requestCount = Mathf.Min(_pendingTeammateSpawnCount, spawnCapacity);
            PrepareTeammatePool(gridZone.OccupiedSlotCount + requestCount);

            // SpawnAsync sẽ gọi grid algorithm theo từng object và giữ slot đã đặt để tránh spawn chồng lên nhau.
            Task<SpawnHandle> spawnTask = SpawnKit.SpawnAsync(
                CreateTeammateSpawnRequest(requestCount, gridZone, gridAlgorithm),
                ResolveSafeMaxSpawnPerFrame(),
                ResolveSpawnCancellationToken());

            // Chờ request hoàn tất để cập nhật queue theo số object spawn thành công thực tế.
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
                    "Player không spawn được teammate vào teammateSpawnGridZone. Kiểm tra lại teammateSpawnPreset, teammateSpawnGridZone hoặc pool config.",
                    this);
                _pendingTeammateSpawnCount = 0;
                break;
            }

            _pendingTeammateSpawnCount = Mathf.Max(0, _pendingTeammateSpawnCount - spawnedCount);

            Debug.Log(
                $"Player '{name}' đã spawn {spawnedCount} teammate vào zone '{gridZone.name}'. Còn lại trong queue: {_pendingTeammateSpawnCount}.",
                this);
        }

        _spawnTeammateRoutine = null;
    }

    /// <summary>
    /// Chuẩn bị pool teammate trước khi spawn để giảm chi phí grow pool trong lúc chơi.
    /// Công thức tính pool hiện tại là heuristic đơn giản, chưa tính đến variation prefab hay nhiều queue khác nhau.
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
    /// Kiểm tra Player đã đủ điều kiện để spawn teammate qua SpawnAsync hay chưa.
    /// Hàm này có thể gọi lookup manager và zone nhiều lần trong một chu kỳ spawn, chưa cache theo frame.
    /// </summary>
    private bool CanSpawnTeammates()
    {
        if (teammateSpawnPreset == null || teammateSpawnPreset.spawnable == null)
        {
            if (!_hasWarnedMissingSpawnPreset)
            {
                _hasWarnedMissingSpawnPreset = true;
                Debug.LogWarning("Player chưa được gán teammateSpawnPreset hợp lệ nên không thể spawn teammate.", this);
            }

            return false;
        }

        _hasWarnedMissingSpawnPreset = false;

        if (ResolveSpawnManager() == null)
        {
            if (!_hasWarnedMissingSpawnManager)
            {
                _hasWarnedMissingSpawnManager = true;
                Debug.LogWarning("Không tìm thấy SpawnManager trong scene nên Player không thể SpawnAsync teammate.", this);
            }

            return false;
        }

        _hasWarnedMissingSpawnManager = false;
        return ResolveTeammateSpawnGridZone() != null
               && ValidateInitialSpawnModeConfiguration();
    }

    private int ResolveSpawnCapacityForMode(int availableSlotCount)
    {
        availableSlotCount = Mathf.Max(0, availableSlotCount);

        switch (teammateInitialSpawnMode)
        {
            case FormationSpawnInitialPoseMode.RandomEmptySlot:
            case FormationSpawnInitialPoseMode.NearestEmptySlot:
                return availableSlotCount / 2;

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
        switch (teammateInitialSpawnMode)
        {
            case FormationSpawnInitialPoseMode.FixedTransform:
                _hasWarnedMissingFormationSpawnCollider = false;

                if (teammateFormationSpawnTransform != null)
                {
                    _hasWarnedMissingFormationSpawnTransform = false;
                    return true;
                }

                if (!_hasWarnedMissingFormationSpawnTransform)
                {
                    _hasWarnedMissingFormationSpawnTransform = true;
                    Debug.LogWarning(
                        "Player needs teammateFormationSpawnTransform when teammateInitialSpawnMode is FixedTransform.",
                        this);
                }

                return false;

            case FormationSpawnInitialPoseMode.AuxiliaryColliderScatter:
                _hasWarnedMissingFormationSpawnTransform = false;

                if (teammateFormationSpawnCollider != null)
                {
                    _hasWarnedMissingFormationSpawnCollider = false;
                    return true;
                }

                if (!_hasWarnedMissingFormationSpawnCollider)
                {
                    _hasWarnedMissingFormationSpawnCollider = true;
                    Debug.LogWarning(
                        "Player needs teammateFormationSpawnCollider when teammateInitialSpawnMode is AuxiliaryColliderScatter.",
                        this);
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

    /// <summary>
    /// Tạo SpawnRequest từ preset hiện tại và grid algorithm đang giữ occupied slot.
    /// Variant plan hiện được lấy trực tiếp từ Spawnable, chưa có chỗ chèn quy tắc ưu tiên variant theo game design.
    /// </summary>
    private SpawnRequest CreateTeammateSpawnRequest(
        int teammateCount,
        ColliderSurfaceGridZone gridZone,
        ColliderSurfaceGridAlgorithm gridAlgorithm)
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
            CreateTeammateSpawnAlgorithm(teammateCount, gridZone, gridAlgorithm),
            teammateSpawnPreset != null ? teammateSpawnPreset.seed : 0,
            lifecycle,
            variantPlan);
    }

    private ISpawnAlgorithm CreateTeammateSpawnAlgorithm(
        int teammateCount,
        ColliderSurfaceGridZone gridZone,
        ColliderSurfaceGridAlgorithm gridAlgorithm)
    {
        if (gridZone == null || gridAlgorithm == null)
            return gridAlgorithm;

        var zoneCollider = gridZone.ZoneCollider;
        Vector3 fallbackPosition = zoneCollider != null ? zoneCollider.bounds.center : ResolveTeammateSpawnParent().position;
        Quaternion fallbackRotation = gridZone.transform.rotation;

        return new FormationSpawnToGridAlgorithm(
            gridAlgorithm,
            teammateCount,
            teammateInitialSpawnMode,
            CreateInitialSpawnPoseAlgorithm(teammateCount),
            fallbackPosition,
            fallbackRotation);
    }

    private ISpawnAlgorithm CreateInitialSpawnPoseAlgorithm(int teammateCount)
    {
        switch (teammateInitialSpawnMode)
        {
            case FormationSpawnInitialPoseMode.FixedTransform:
                return teammateFormationSpawnTransform != null
                    ? new FixedPoseAlgorithm(
                        teammateFormationSpawnTransform.position,
                        teammateFormationSpawnTransform.rotation)
                    : null;

            case FormationSpawnInitialPoseMode.AuxiliaryColliderScatter:
                return teammateFormationSpawnCollider != null
                    ? new ColliderVolumeAlgorithm(
                        teammateFormationSpawnCollider,
                        maxTryPerPoint: 32,
                        candidatesPerPoint: 16,
                        minDistance: 0.5f,
                        maxCount: Mathf.Max(1, teammateCount))
                    : null;

            case FormationSpawnInitialPoseMode.CenterSlot:
            case FormationSpawnInitialPoseMode.OwnSlot:
            case FormationSpawnInitialPoseMode.RandomEmptySlot:
            case FormationSpawnInitialPoseMode.NearestEmptySlot:
            default:
                return null;
        }
    }

    /// <summary>
    /// Lấy parent mặc định để chứa teammate.
    /// Hiện tại không tự tạo root riêng cho teammate, nên việc tổ chức hierarchy vẫn phụ thuộc vào setup của scene.
    /// </summary>
    private Transform ResolveTeammateSpawnParent()
    {
        return teammateSpawnParent != null ? teammateSpawnParent : transform;
    }

    /// <summary>
    /// Khởi tạo hoặc tái sử dụng grid zone cho teammate.
    /// Nếu để Player tự tìm child zone thì cách này tiện setup nhưng chưa tối ưu khi hierarchy sâu hoặc bị thay đổi động.
    /// </summary>
    private ColliderSurfaceGridZone ResolveTeammateSpawnGridZone()
    {
        // Tự động tìm child zone để giảm thao tác setup tay trong Inspector.
        if (teammateSpawnGridZone == null)
            teammateSpawnGridZone = GetComponentInChildren<ColliderSurfaceGridZone>();

        if (teammateSpawnGridZone == null)
        {
            if (!_hasWarnedMissingSpawnZone)
            {
                _hasWarnedMissingSpawnZone = true;
                Debug.LogWarning("Player chưa được gán teammateSpawnGridZone hợp lệ nên không thể phân bố teammate theo grid zone.", this);
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
                "teammateSpawnGridZone tồn tại nhưng không resolve được collider/grid algorithm. Kiểm tra lại zoneCollider và cấu hình grid.",
                this);
        }

        return null;
    }

    /// <summary>
    /// Lấy SpawnManager runtime hiện tại.
    /// Nếu singleton chưa sẵn sàng, hàm sẽ fallback sang FindAnyObjectByType; cách này tiện nhưng có chi phí tìm kiếm trong scene.
    /// </summary>
    private SpawnManager ResolveSpawnManager()
    {
        if (SpawnManager.Instance != null) return SpawnManager.Instance;
        return FindAnyObjectByType<SpawnManager>();
    }

    /// <summary>
    /// Lấy Spawnable được khai báo trong preset teammate hiện tại.
    /// Hàm này chỉ trả về trực tiếp tham chiếu, không có validate sâu hơn cho prefab con trong spawnable.
    /// </summary>
    private SpawnableSO ResolveTeammateSpawnable()
    {
        return teammateSpawnPreset != null ? teammateSpawnPreset.spawnable : null;
    }

    /// <summary>
    /// Đảm bảo batch size hợp lệ ngay cả khi dữ liệu bị sửa sai trong Inspector.
    /// Hàm này chỉ clamp min, chưa có max recommendation theo năng lực thiết bị.
    /// </summary>
    private int ResolveSafeMaxSpawnPerFrame()
    {
        return Mathf.Max(1, maxSpawnPerFrame);
    }

    /// <summary>
    /// Tạo hoặc tái sử dụng cancellation token cho queue SpawnAsync hiện tại.
    /// Hiện tại cả queue dùng chung một token nên chưa tách riêng từng request để debug chi tiết.
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
    /// Hủy request SpawnAsync đang chờ và giải phóng token cũ.
    /// Các request đã hủy hiện chỉ bị reset queue, chưa có cơ chế retry tự động.
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
