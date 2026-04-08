using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.ScriptableObjects;
using Vit.SpawnKit.Services;

[DisallowMultipleComponent]
public sealed class BulletSpawner : MonoBehaviour
{
    private const string PlayerTag = "Player";
    private const string HomeTag = "Home";
    private const string EnemyTag = "Enemy";
    private static readonly Vector3 BulletDirection = Vector3.forward;
    private const float DefaultFireInterval = 0.1f;
    private const int DefaultBulletsPerTeammate = 1;
    private const int DefaultMaxBulletsPerVolley = 32;
    private const int DefaultMaxActiveBullets = 256;

    [Header("References")]
    [FormerlySerializedAs("bulletSpawnable")]
    [SerializeField] private SpawnPresetSO bulletSpawnPresetDefault;
    [SerializeField] private SpawnGridQueue teammateGridSource;
    [SerializeField] private Transform bulletParent;
    [SerializeField] private HomeController homeController;
    [SerializeField] private Collider bulletSpawnAreaCollider;
    [SerializeField] private Transform[] muzzles;

    [Header("Runtime")]
    [SerializeField] private bool prewarmPoolOnStart = true;
    [SerializeField] private bool clampVolleyToSustainableCadence = true;

    [Header("Spawn Area")]
    [SerializeField] private bool useFormationSpawnColliderAsSpawnArea = true;
    [SerializeField, Min(0f)] private float spawnAreaWidthPerTeammate = 0.08f;
    [SerializeField, Min(0f)] private float spawnAreaHeightPerTeammate = 0f;
    [SerializeField, Min(0f)] private float maxSpawnAreaWidth = 4f;
    [SerializeField, Min(0f)] private float maxSpawnAreaHeight = 1.5f;

    [Header("Hit Detection")]
    [SerializeField] private LayerMask targetLayers = (1 << 6) | (1 << 7);
    [SerializeField, Min(1)] private int hitBufferSize = 8;

    private readonly List<GameObject> _spawnBuffer = new List<GameObject>(64);
    private readonly List<ActiveBulletRuntime> _activeBullets = new List<ActiveBulletRuntime>(128);
    private readonly Dictionary<EntityId, Transform> _playersInsideHome = new Dictionary<EntityId, Transform>(4);
    private readonly List<EntityId> _playersPendingRemoval = new List<EntityId>(4);

    private readonly BulletSpawnPoseAlgorithm _spawnAlgorithm = new BulletSpawnPoseAlgorithm();

    private Transform[] _resolvedMuzzles = System.Array.Empty<Transform>();
    private RaycastHit[] _hitBuffer = System.Array.Empty<RaycastHit>();
    private Collider[] _homeOverlapBuffer = new Collider[8];

    private float _nextFireTime;
    private SpawnableSO _preparedPoolSpawnable;
    private SpawnManager _cachedSpawnManager;
    private WeaponSO _equippedWeapon;
    private HomeController _subscribedHomeController;

    private bool _hasWarnedMissingSpawnable;
    private bool _hasWarnedMissingTeammateGrid;
    private bool _hasWarnedMissingHomeController;
    private bool _hasWarnedMissingSpawnManager;
    private bool _hasWarnedMissingMuzzles;
    private bool _hasWarnedMissingBulletComponent;

    private BoxCollider _cachedSpawnAreaBoxCollider;
    private Vector3 _spawnAreaBaseSize;
    private Vector3 _spawnAreaBaseCenter;
    private int _lastSpawnAreaTeammateCount = -1;

    public WeaponSO EquippedWeapon => _equippedWeapon;

    private struct ActiveBulletRuntime
    {
        public Bullet bullet;
        public Transform cachedTransform;
        public Vector3 position;
        public float speed;
        public float hitRadius;
    }

    private void Reset()
    {
        AutoAssignHomeController();
        AutoAssignBulletSpawnAreaCollider();
    }

    private void Awake()
    {
        AutoAssignHomeController();
        EnsureRuntimeCaches();
        SubscribeToHomeController();
        RefreshHomeOccupants();
    }

    private void OnEnable()
    {
        SubscribeToHomeController();
        RefreshHomeOccupants();
        _nextFireTime = Time.time;
    }

    private void OnValidate()
    {
        spawnAreaWidthPerTeammate = Mathf.Max(0f, spawnAreaWidthPerTeammate);
        spawnAreaHeightPerTeammate = Mathf.Max(0f, spawnAreaHeightPerTeammate);
        maxSpawnAreaWidth = Mathf.Max(0f, maxSpawnAreaWidth);
        maxSpawnAreaHeight = Mathf.Max(0f, maxSpawnAreaHeight);
        hitBufferSize = Mathf.Max(1, hitBufferSize);

        AutoAssignHomeController();
        AutoAssignBulletSpawnAreaCollider();
        EnsureRuntimeCaches();
    }

    private void Start()
    {
        EnsureRuntimeCaches();
        SubscribeToHomeController();
        RefreshHomeOccupants();

        if (prewarmPoolOnStart)
            PreparePool();
    }

    private void Update()
    {
        UpdateActiveBullets(Time.deltaTime);
        ValidateTrackedPlayersInsideHome();

        if (Time.time < _nextFireTime)
            return;

        if (!CanFire())
            return;

        if (!HasPlayerInsideHome())
            return;

        int teammateCount = ResolveTeammateCount();
        if (teammateCount <= 0)
            return;

        UpdateSpawnAreaSize(teammateCount);

        int bulletCount = ResolveVolleyBulletCount(teammateCount);
        if (bulletCount <= 0)
            return;

        if (FireVolley(bulletCount))
            ScheduleNextFireTime();
    }

    private void OnDisable()
    {
        UnsubscribeFromHomeController();
        DespawnAllActiveBullets();
        _playersInsideHome.Clear();
        _nextFireTime = 0f;
    }

    private void OnDestroy()
    {
        UnsubscribeFromHomeController();
    }

    public void ApplyWeapon(WeaponSO weapon)
    {
        if (weapon != null && !weapon.IsValid)
            return;

        _equippedWeapon = weapon;

        SpawnableSO activeSpawnable = ResolveActiveBulletSpawnable();
        if (_preparedPoolSpawnable != activeSpawnable)
            _preparedPoolSpawnable = activeSpawnable;
    }

    private void HandleHomeInteraction(HomeInteractionEvent interactionEvent)
    {
        if (interactionEvent == null || interactionEvent.controller != ResolveHomeController())
            return;

        if (interactionEvent.isEnter)
            HandleHomeTriggerEnter(interactionEvent.otherCollider);
        else
            HandleHomeTriggerExit(interactionEvent.otherCollider);
    }

    public void HandleHomeTriggerEnterFromRelay(Collider sourceCollider, Collider other)
    {
        HomeController resolvedHomeController = ResolveHomeController();
        if (resolvedHomeController == null || sourceCollider == null || other == null)
            return;

        HandleHomeInteraction(new HomeInteractionEvent(
            resolvedHomeController,
            resolvedHomeController.HomeCollider,
            sourceCollider,
            other,
            HomeInteractionType.Enter));
    }

    public void HandleHomeTriggerExitFromRelay(Collider sourceCollider, Collider other)
    {
        HomeController resolvedHomeController = ResolveHomeController();
        if (resolvedHomeController == null || sourceCollider == null || other == null)
            return;

        HandleHomeInteraction(new HomeInteractionEvent(
            resolvedHomeController,
            resolvedHomeController.HomeCollider,
            sourceCollider,
            other,
            HomeInteractionType.Exit));
    }

    private void HandleHomeTriggerEnter(Collider other)
    {
        Transform playerRoot = ResolveTaggedTransform(other, PlayerTag);
        if (playerRoot == null)
            return;

        RefreshTrackedPlayerState(playerRoot);
    }

    private void HandleHomeTriggerExit(Collider other)
    {
        Transform playerRoot = ResolveTaggedTransform(other, PlayerTag);
        if (playerRoot == null)
            return;

        RefreshTrackedPlayerState(playerRoot);
    }

    private bool CanFire()
    {
        if (ResolveActiveBulletSpawnable() == null)
        {
            WarnOnce(ref _hasWarnedMissingSpawnable, "BulletSpawner needs a valid bulletSpawnPresetDefault or WeaponSO.");
            return false;
        }

        _hasWarnedMissingSpawnable = false;

        if (teammateGridSource == null)
        {
            WarnOnce(ref _hasWarnedMissingTeammateGrid, "BulletSpawner needs a teammateGridSource.");
            return false;
        }

        _hasWarnedMissingTeammateGrid = false;

        if (ResolveSpawnManager() == null)
        {
            WarnOnce(ref _hasWarnedMissingSpawnManager, "BulletSpawner could not find a SpawnManager in the scene.");
            return false;
        }

        _hasWarnedMissingSpawnManager = false;

        HomeController resolvedHomeController = ResolveHomeController();
        Collider resolvedHomeCollider = resolvedHomeController != null ? resolvedHomeController.HomeCollider : null;
        if (resolvedHomeController == null || resolvedHomeCollider == null)
        {
            WarnOnce(ref _hasWarnedMissingHomeController, "BulletSpawner needs a HomeController with a valid Home collider.");
            return false;
        }

        _hasWarnedMissingHomeController = false;

        if (ResolveBulletSpawnAreaCollider() == null && BuildMuzzleSet() <= 0)
        {
            WarnOnce(ref _hasWarnedMissingMuzzles, "BulletSpawner needs a spawn area collider or at least one muzzle transform.");
            return false;
        }

        _hasWarnedMissingMuzzles = false;
        return true;
    }

    private bool FireVolley(int bulletCount)
    {
        SpawnableSO activeSpawnable = ResolveActiveBulletSpawnable();
        if (activeSpawnable == null)
            return false;

        int muzzleCount = BuildMuzzleSet();
        Collider spawnAreaCollider = ResolveBulletSpawnAreaCollider();
        if (spawnAreaCollider == null && muzzleCount <= 0)
            return false;

        PreparePool();

        Transform spawnParent = ResolveSpawnParent();
        Vector3 fallbackPosition = spawnParent != null ? spawnParent.position : transform.position;
        _spawnAlgorithm.Configure(spawnAreaCollider, bulletCount, _resolvedMuzzles, muzzleCount, fallbackPosition);

        SpawnLifecycle? lifecycleOverride = ResolveActiveBulletLifecycleOverride();
        int spawnedCount = SpawnKit.SpawnNonAlloc(
            activeSpawnable,
            bulletCount,
            _spawnBuffer,
            spawnParent,
            _spawnAlgorithm,
            lifecycle: lifecycleOverride);

        if (spawnedCount <= 0)
            return false;

        for (int i = 0; i < spawnedCount; i++)
        {
            GameObject bulletObject = _spawnBuffer[i];
            if (bulletObject == null)
                continue;

            if (!bulletObject.TryGetComponent(out Bullet bullet))
            {
                WarnOnce(
                    ref _hasWarnedMissingBulletComponent,
                    $"Spawned bullet '{bulletObject.name}' is missing a Bullet component.");
                SpawnKit.Despawn(bulletObject);
                continue;
            }

            _hasWarnedMissingBulletComponent = false;

            Transform bulletTransform = bullet.CachedTransform != null ? bullet.CachedTransform : bullet.transform;
            Vector3 startPosition = bulletTransform.position;

            bullet.SetWorldPose(startPosition, BulletDirection);
            RegisterActiveBullet(bullet, bulletTransform, startPosition);
        }

        return true;
    }

    private void RegisterActiveBullet(Bullet bullet, Transform bulletTransform, Vector3 startPosition)
    {
        if (bullet == null || bulletTransform == null)
            return;

        _activeBullets.Add(new ActiveBulletRuntime
        {
            bullet = bullet,
            cachedTransform = bulletTransform,
            position = startPosition,
            speed = Mathf.Max(0f, bullet.MoveSpeed),
            hitRadius = Mathf.Max(0f, bullet.HitRadius),
        });
    }

    private void UpdateActiveBullets(float deltaTime)
    {
        if (_activeBullets.Count == 0 || deltaTime <= 0f)
            return;

        float targetDistanceScale = deltaTime;
        int layerMask = ResolveHitLayerMask();

        for (int i = _activeBullets.Count - 1; i >= 0; i--)
        {
            var runtime = _activeBullets[i];
            if (!IsRuntimeValid(runtime))
            {
                RemoveActiveBulletAt(i);
                continue;
            }

            float stepDistance = runtime.speed * targetDistanceScale;
            if (stepDistance <= 0f)
            {
                _activeBullets[i] = runtime;
                continue;
            }

            if (TryResolveDamageableHit(
                    runtime.position,
                    stepDistance,
                    runtime.hitRadius,
                    layerMask,
                    out Enemy hitEnemy,
                    out Obstacle hitObstacle,
                    out Vector3 hitPoint))
            {
                if (hitEnemy != null)
                    hitEnemy.ApplyDamage(runtime.bullet.Damage);
                else if (hitObstacle != null)
                    hitObstacle.ApplyDamage(runtime.bullet.Damage);

                runtime.bullet.SetWorldPosition(hitPoint);
                QueueRuntimeBulletDespawn(runtime);
                RemoveActiveBulletAt(i);
                continue;
            }

            runtime.position += BulletDirection * stepDistance;
            runtime.bullet.SetWorldPosition(runtime.position);
            _activeBullets[i] = runtime;
        }
    }

    private bool TryResolveDamageableHit(
        Vector3 origin,
        float distance,
        float hitRadius,
        int layerMask,
        out Enemy hitEnemy,
        out Obstacle hitObstacle,
        out Vector3 hitPoint)
    {
        hitEnemy = null;
        hitObstacle = null;
        hitPoint = origin + BulletDirection * distance;
        int hitCount = hitRadius > 0f
            ? Physics.SphereCastNonAlloc(
                origin,
                hitRadius,
                BulletDirection,
                _hitBuffer,
                distance,
                layerMask,
                QueryTriggerInteraction.Collide)
            : Physics.RaycastNonAlloc(
                origin,
                BulletDirection,
                _hitBuffer,
                distance,
                layerMask,
                QueryTriggerInteraction.Collide);

        if (hitCount <= 0)
            return false;

        float nearestDistance = float.PositiveInfinity;
        bool foundEnemy = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hitBuffer[i];
            Collider hitCollider = hit.collider;
            if (hit.distance >= nearestDistance)
                continue;

            Enemy resolvedEnemy = ResolveEnemyFromCollider(hitCollider);
            if (resolvedEnemy != null && resolvedEnemy.IsAlive)
            {
                nearestDistance = hit.distance;
                hitPoint = hit.point;
                hitEnemy = resolvedEnemy;
                hitObstacle = null;
                foundEnemy = true;
                continue;
            }

            Obstacle resolvedObstacle = ResolveObstacleFromCollider(hitCollider);
            if (resolvedObstacle == null)
                continue;

            nearestDistance = hit.distance;
            hitPoint = hit.point;
            hitEnemy = null;
            hitObstacle = resolvedObstacle;
            foundEnemy = true;
        }

        return foundEnemy;
    }

    private bool IsRuntimeValid(ActiveBulletRuntime runtime)
    {
        return runtime.bullet != null
               && runtime.cachedTransform != null
               && runtime.bullet.gameObject.activeInHierarchy;
    }

    private void QueueRuntimeBulletDespawn(ActiveBulletRuntime runtime)
    {
        if (runtime.bullet == null)
            return;

        runtime.bullet.BeginQueuedDespawn();
        BufferedPoolDespawnQueue.Queue(runtime.bullet);
    }

    private static void ForceDespawnRuntimeBullet(ActiveBulletRuntime runtime)
    {
        if (runtime.bullet == null)
            return;

        SpawnKit.Despawn(runtime.bullet.gameObject);
    }

    private void DespawnAllActiveBullets()
    {
        for (int i = _activeBullets.Count - 1; i >= 0; i--)
        {
            ForceDespawnRuntimeBullet(_activeBullets[i]);
        }

        _activeBullets.Clear();
    }

    private void RemoveActiveBulletAt(int index)
    {
        int lastIndex = _activeBullets.Count - 1;
        if (index < 0 || index > lastIndex)
            return;

        _activeBullets[index] = _activeBullets[lastIndex];
        _activeBullets.RemoveAt(lastIndex);
    }

    private int ResolveVolleyBulletCount(int teammateCount)
    {
        if (teammateCount <= 0)
            return 0;

        int bulletCount = MultiplyClamped(teammateCount, ResolveActiveBulletsPerTeammate());
        int maxBulletsPerVolleyOverride = ResolveActiveMaxBulletsPerVolley();
        if (maxBulletsPerVolleyOverride > 0)
            bulletCount = Mathf.Min(bulletCount, maxBulletsPerVolleyOverride);

        if (clampVolleyToSustainableCadence)
            bulletCount = Mathf.Min(bulletCount, ResolveSustainableVolleyCount());

        int availableSlots = Mathf.Max(0, ResolveActiveMaxActiveBullets() - _activeBullets.Count);
        if (availableSlots <= 0)
            return 0;

        return Mathf.Min(bulletCount, availableSlots);
    }

    private bool HasPlayerInsideHome()
    {
        return _playersInsideHome.Count > 0;
    }

    private int ResolveTeammateCount()
    {
        return teammateGridSource != null ? Mathf.Max(0, teammateGridSource.GetOccupiedSlotCount()) : 0;
    }

    private SpawnPresetSO ResolveActiveBulletSpawnPreset()
    {
        if (_equippedWeapon != null
            && _equippedWeapon.IsValid
            && _equippedWeapon.BulletSpawnPreset != null
            && _equippedWeapon.BulletSpawnPreset.HasSpawnables)
            return _equippedWeapon.BulletSpawnPreset;

        return bulletSpawnPresetDefault != null && bulletSpawnPresetDefault.HasSpawnables
            ? bulletSpawnPresetDefault
            : null;
    }

    private SpawnableSO ResolveActiveBulletSpawnable()
    {
        SpawnPresetSO activePreset = ResolveActiveBulletSpawnPreset();
        return activePreset != null ? activePreset.GetPrimarySpawnable() : null;
    }

    private SpawnLifecycle? ResolveActiveBulletLifecycleOverride()
    {
        SpawnPresetSO activePreset = ResolveActiveBulletSpawnPreset();
        if (activePreset == null || !activePreset.overrideLifecycle)
            return null;

        return activePreset.lifecycle;
    }

    private float ResolveActiveFireInterval()
    {
        return _equippedWeapon != null && _equippedWeapon.IsValid
            ? _equippedWeapon.FireInterval
            : DefaultFireInterval;
    }

    private int ResolveActiveBulletsPerTeammate()
    {
        return _equippedWeapon != null && _equippedWeapon.IsValid
            ? _equippedWeapon.BulletsPerTeammate
            : DefaultBulletsPerTeammate;
    }

    private int ResolveActiveMaxBulletsPerVolley()
    {
        return _equippedWeapon != null && _equippedWeapon.IsValid
            ? _equippedWeapon.MaxBulletsPerVolley
            : DefaultMaxBulletsPerVolley;
    }

    private int ResolveActiveMaxActiveBullets()
    {
        return _equippedWeapon != null && _equippedWeapon.IsValid
            ? _equippedWeapon.MaxActiveBullets
            : DefaultMaxActiveBullets;
    }

    private float ResolveActiveBulletTimedLifetime()
    {
        SpawnPresetSO activePreset = ResolveActiveBulletSpawnPreset();
        if (activePreset == null)
            return 0f;

        if (activePreset.overrideLifecycle)
            return activePreset.lifecycle.IsTimed ? activePreset.lifecycle.delaySeconds : 0f;

        SpawnableSO activeSpawnable = activePreset.GetPrimarySpawnable();
        return activeSpawnable != null && activeSpawnable.defaultLifecycle.IsTimed
            ? activeSpawnable.defaultLifecycle.delaySeconds
            : 0f;
    }

    private void PreparePool()
    {
        SpawnableSO activeSpawnable = ResolveActiveBulletSpawnable();
        if (activeSpawnable == null)
            return;

        if (_preparedPoolSpawnable != activeSpawnable)
            _preparedPoolSpawnable = activeSpawnable;

        if (prewarmPoolOnStart)
            SpawnKit.Prewarm(activeSpawnable);
    }

    private int ResolveSafeMaxVolleyCount()
    {
        int maxActiveBulletCount = ResolveActiveMaxActiveBullets();
        int maxBulletsPerVolleyOverride = ResolveActiveMaxBulletsPerVolley();
        int safeMaxVolley = maxBulletsPerVolleyOverride > 0
            ? Mathf.Min(maxBulletsPerVolleyOverride, maxActiveBulletCount)
            : Mathf.Max(1, maxActiveBulletCount);

        if (clampVolleyToSustainableCadence)
            safeMaxVolley = Mathf.Min(safeMaxVolley, ResolveSustainableVolleyCount());

        return Mathf.Max(1, safeMaxVolley);
    }

    private int ResolveSustainableVolleyCount()
    {
        int maxActiveBulletCount = ResolveActiveMaxActiveBullets();
        if (maxActiveBulletCount <= 0)
            return 0;

        float safeInterval = ResolveActiveFireInterval();
        float safeLifetime = ResolveActiveBulletTimedLifetime();
        if (safeLifetime <= 0f)
            return maxActiveBulletCount;

        int concurrentVolleyCount = Mathf.Max(1, Mathf.CeilToInt(safeLifetime / safeInterval));
        return Mathf.Max(1, maxActiveBulletCount / concurrentVolleyCount);
    }

    private void ScheduleNextFireTime()
    {
        float now = Time.time;
        float activeFireInterval = ResolveActiveFireInterval();
        if (_nextFireTime <= 0f)
        {
            _nextFireTime = now + activeFireInterval;
            return;
        }

        _nextFireTime += activeFireInterval;
        if (_nextFireTime < now)
            _nextFireTime = now;
    }

    private Transform ResolveSpawnParent()
    {
        return bulletParent != null ? bulletParent : transform;
    }

    private SpawnManager ResolveSpawnManager()
    {
        if (_cachedSpawnManager != null)
            return _cachedSpawnManager;

        _cachedSpawnManager = SpawnManager.Instance != null
            ? SpawnManager.Instance
            : FindAnyObjectByType<SpawnManager>();
        return _cachedSpawnManager;
    }

    private HomeController ResolveHomeController()
    {
        if (homeController != null)
            return homeController;

        AutoAssignHomeController();
        return homeController;
    }

    private void AutoAssignHomeController()
    {
        if (homeController != null)
            return;

        homeController = GetComponentInParent<HomeController>();
        if (homeController != null)
            return;

        homeController = FindAnyObjectByType<HomeController>();
    }

    private void AutoAssignBulletSpawnAreaCollider()
    {
        if (bulletSpawnAreaCollider != null)
        {
            CacheSpawnAreaShape();
            return;
        }

        if (useFormationSpawnColliderAsSpawnArea && teammateGridSource != null)
        {
            bulletSpawnAreaCollider = teammateGridSource.GetFormationSpawnCollider();
            if (bulletSpawnAreaCollider != null)
            {
                CacheSpawnAreaShape();
                return;
            }
        }

        Transform searchRoot = transform.parent != null ? transform.parent : transform;
        Collider firstEligibleCollider = null;
        var colliders = searchRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];
            if (!IsEligibleSpawnAreaCollider(candidate))
                continue;

            string candidateName = candidate.name.ToLowerInvariant();
            if (candidateName.Contains("head") || candidateName.Contains("heat"))
            {
                bulletSpawnAreaCollider = candidate;
                CacheSpawnAreaShape();
                return;
            }

            if (firstEligibleCollider == null)
                firstEligibleCollider = candidate;
        }

        bulletSpawnAreaCollider = firstEligibleCollider;
        CacheSpawnAreaShape();
    }

    private void SubscribeToHomeController()
    {
        HomeController resolvedHomeController = ResolveHomeController();
        if (resolvedHomeController == null)
            return;

        if (_subscribedHomeController == resolvedHomeController)
            return;

        UnsubscribeFromHomeController();
        resolvedHomeController.Interaction += HandleHomeInteraction;
        _subscribedHomeController = resolvedHomeController;
    }

    private void UnsubscribeFromHomeController()
    {
        if (_subscribedHomeController == null)
            return;

        _subscribedHomeController.Interaction -= HandleHomeInteraction;
        _subscribedHomeController = null;
    }

    private void RefreshHomeOccupants()
    {
        _playersInsideHome.Clear();

        HomeController resolvedHomeController = ResolveHomeController();
        Collider resolvedHomeCollider = resolvedHomeController != null ? resolvedHomeController.HomeCollider : null;
        if (resolvedHomeCollider == null
            || !resolvedHomeCollider.enabled
            || !resolvedHomeCollider.gameObject.activeInHierarchy)
            return;

        EnsureHomeOverlapBuffer(8);
        Physics.SyncTransforms();

        int overlapCount = CollectHomeOverlaps(resolvedHomeCollider);
        if (overlapCount >= _homeOverlapBuffer.Length)
        {
            EnsureHomeOverlapBuffer(_homeOverlapBuffer.Length * 2);
            overlapCount = CollectHomeOverlaps(resolvedHomeCollider);
        }

        for (int i = 0; i < overlapCount; i++)
        {
            HandleHomeTriggerEnter(_homeOverlapBuffer[i]);
            _homeOverlapBuffer[i] = null;
        }
    }

    private void ValidateTrackedPlayersInsideHome()
    {
        if (_playersInsideHome.Count <= 0)
            return;

        _playersPendingRemoval.Clear();

        foreach (var entry in _playersInsideHome)
        {
            if (IsPlayerRootInsideHome(entry.Value))
                continue;

            _playersPendingRemoval.Add(entry.Key);
        }

        for (int i = 0; i < _playersPendingRemoval.Count; i++)
        {
            _playersInsideHome.Remove(_playersPendingRemoval[i]);
        }
    }

    private void RefreshTrackedPlayerState(Transform playerRoot)
    {
        if (playerRoot == null)
            return;

        EntityId playerId = playerRoot.gameObject.GetEntityId();
        if (IsPlayerRootInsideHome(playerRoot))
        {
            _playersInsideHome[playerId] = playerRoot;
            return;
        }

        _playersInsideHome.Remove(playerId);
    }

    private bool IsPlayerRootInsideHome(Transform playerRoot)
    {
        HomeController resolvedHomeController = ResolveHomeController();
        if (resolvedHomeController == null || playerRoot == null || !playerRoot.gameObject.activeInHierarchy)
            return false;

        return resolvedHomeController.IsInside(playerRoot);
    }

    private int CollectHomeOverlaps(Collider sourceCollider)
    {
        Bounds bounds = sourceCollider.bounds;
        Vector3 halfExtents = bounds.extents;
        halfExtents.x = Mathf.Max(halfExtents.x, 0.01f);
        halfExtents.y = Mathf.Max(halfExtents.y, 0.01f);
        halfExtents.z = Mathf.Max(halfExtents.z, 0.01f);

        return Physics.OverlapBoxNonAlloc(
            bounds.center,
            halfExtents,
            _homeOverlapBuffer,
            Quaternion.identity,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);
    }

    private Collider ResolveBulletSpawnAreaCollider()
    {
        if (bulletSpawnAreaCollider == null)
            AutoAssignBulletSpawnAreaCollider();

        CacheSpawnAreaShape();
        return bulletSpawnAreaCollider;
    }

    private void CacheSpawnAreaShape()
    {
        if (!(bulletSpawnAreaCollider is BoxCollider boxCollider))
        {
            _cachedSpawnAreaBoxCollider = null;
            _lastSpawnAreaTeammateCount = -1;
            return;
        }

        if (_cachedSpawnAreaBoxCollider == boxCollider)
            return;

        _cachedSpawnAreaBoxCollider = boxCollider;
        _spawnAreaBaseSize = boxCollider.size;
        _spawnAreaBaseCenter = boxCollider.center;
        _lastSpawnAreaTeammateCount = -1;
    }

    private void UpdateSpawnAreaSize(int teammateCount)
    {
        Collider spawnAreaCollider = ResolveBulletSpawnAreaCollider();
        if (!(spawnAreaCollider is BoxCollider boxCollider))
            return;

        CacheSpawnAreaShape();
        if (_cachedSpawnAreaBoxCollider != boxCollider || teammateCount == _lastSpawnAreaTeammateCount)
            return;

        boxCollider.center = _spawnAreaBaseCenter;
        boxCollider.size = new Vector3(
            ResolveScaledSpawnAxis(_spawnAreaBaseSize.x, spawnAreaWidthPerTeammate, maxSpawnAreaWidth, teammateCount),
            ResolveScaledSpawnAxis(_spawnAreaBaseSize.y, spawnAreaHeightPerTeammate, maxSpawnAreaHeight, teammateCount),
            _spawnAreaBaseSize.z);

        _lastSpawnAreaTeammateCount = teammateCount;
    }

    private int BuildMuzzleSet()
    {
        EnsureMuzzleBuffer(Mathf.Max(1, muzzles != null ? muzzles.Length : 1));

        int count = 0;
        if (muzzles != null && muzzles.Length > 0)
        {
            for (int i = 0; i < muzzles.Length; i++)
            {
                Transform muzzle = muzzles[i];
                if (muzzle == null)
                    continue;

                _resolvedMuzzles[count++] = muzzle;
            }
        }

        if (count == 0)
        {
            _resolvedMuzzles[0] = ResolveSpawnParent();
            count = _resolvedMuzzles[0] != null ? 1 : 0;
        }

        return count;
    }

    private void EnsureRuntimeCaches()
    {
        AutoAssignBulletSpawnAreaCollider();
        EnsureMuzzleBuffer(Mathf.Max(1, muzzles != null ? muzzles.Length : 1));
        EnsureHitBuffer();
    }

    private void EnsureMuzzleBuffer(int requiredSize)
    {
        requiredSize = Mathf.Max(1, requiredSize);
        if (_resolvedMuzzles.Length >= requiredSize)
            return;

        _resolvedMuzzles = new Transform[requiredSize];
    }

    private void EnsureHitBuffer()
    {
        int desiredSize = Mathf.Max(1, hitBufferSize);
        if (_hitBuffer.Length == desiredSize)
            return;

        _hitBuffer = new RaycastHit[desiredSize];
    }

    private void EnsureHomeOverlapBuffer(int requiredSize)
    {
        requiredSize = Mathf.Max(1, requiredSize);
        if (_homeOverlapBuffer.Length >= requiredSize)
            return;

        _homeOverlapBuffer = new Collider[requiredSize];
    }

    private int ResolveHitLayerMask()
    {
        return targetLayers.value != 0 ? targetLayers.value : Physics.AllLayers;
    }

    private static Transform ResolveTaggedTransform(Collider other, string requiredTag)
    {
        if (other == null)
            return null;

        if (other.CompareTag(requiredTag))
            return other.transform;

        var attachedRigidbody = other.attachedRigidbody;
        if (attachedRigidbody != null && attachedRigidbody.CompareTag(requiredTag))
            return attachedRigidbody.transform;

        Transform current = other.transform;
        while (current != null)
        {
            if (current.CompareTag(requiredTag))
                return current;

            current = current.parent;
        }

        Transform root = other.transform.root;
        return root != null && root.CompareTag(requiredTag) ? root : null;
    }

    private static Enemy ResolveEnemyFromCollider(Collider other)
    {
        if (other == null)
            return null;

        Enemy enemy = other.GetComponentInParent<Enemy>();
        if (enemy != null)
            return enemy;

        Transform enemyRoot = ResolveTaggedTransform(other, EnemyTag);
        return enemyRoot != null ? enemyRoot.GetComponentInParent<Enemy>() : null;
    }

    private static Obstacle ResolveObstacleFromCollider(Collider other)
    {
        if (other == null)
            return null;

        return other.GetComponentInParent<Obstacle>();
    }

    private static Collider ResolvePrimaryPlayerCollider(Transform playerRoot)
    {
        if (playerRoot == null)
            return null;

        if (playerRoot.TryGetComponent(out Collider rootCollider))
            return rootCollider;

        return playerRoot.GetComponentInChildren<Collider>(true);
    }

    private bool IsEligibleSpawnAreaCollider(Collider candidate)
    {
        return candidate != null
               && candidate != (ResolveHomeController() != null ? ResolveHomeController().HomeCollider : null)
               && candidate.enabled
               && !candidate.isTrigger
               && candidate.gameObject != gameObject;
    }

    private static float ResolveScaledSpawnAxis(float baseValue, float perTeammate, float maxValue, int teammateCount)
    {
        float value = baseValue + Mathf.Max(0, teammateCount - 1) * perTeammate;
        if (maxValue > 0f)
            value = Mathf.Min(value, Mathf.Max(baseValue, maxValue));

        return Mathf.Max(0.01f, value);
    }

    private static int MultiplyClamped(int value, int multiplier)
    {
        if (value <= 0 || multiplier <= 0)
            return 0;

        long total = (long)value * multiplier;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private void WarnOnce(ref bool hasWarned, string message)
    {
        if (hasWarned)
            return;

        hasWarned = true;
        Debug.LogWarning(message, this);
    }

    private sealed class BulletSpawnPoseAlgorithm : ISpawnAlgorithm
    {
        private Collider _spawnAreaCollider;
        private int _spawnCount;
        private Transform[] _muzzles = System.Array.Empty<Transform>();
        private int _muzzleCount;
        private Vector3 _fallbackPosition;

        public void Configure(
            Collider spawnAreaCollider,
            int spawnCount,
            Transform[] muzzles,
            int muzzleCount,
            Vector3 fallbackPosition)
        {
            _spawnAreaCollider = spawnAreaCollider;
            _spawnCount = Mathf.Max(1, spawnCount);
            _muzzles = muzzles ?? System.Array.Empty<Transform>();
            _muzzleCount = Mathf.Max(0, muzzleCount);
            _fallbackPosition = fallbackPosition;
        }

        public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            if (TryGetSpawnAreaPose(index, out position))
                return;

            if (_muzzleCount <= 0)
            {
                position = _fallbackPosition;
                return;
            }

            Transform muzzle = _muzzles[index % _muzzleCount];
            if (muzzle == null)
            {
                position = _fallbackPosition;
                return;
            }

            position = muzzle.position;
        }

        private bool TryGetSpawnAreaPose(int index, out Vector3 position)
        {
            position = _fallbackPosition;
            if (_spawnAreaCollider == null)
                return false;

            if (_spawnAreaCollider is BoxCollider boxCollider)
            {
                position = ResolveBoxColliderFacePosition(boxCollider, index, _spawnCount);
                return true;
            }

            Bounds bounds = _spawnAreaCollider.bounds;
            position = ResolveBoundsFacePosition(bounds, index, _spawnCount);
            return true;
        }

        private static Vector3 ResolveBoxColliderFacePosition(BoxCollider boxCollider, int index, int spawnCount)
        {
            Vector3 localPoint = ResolveFaceLocalPoint(
                boxCollider.center,
                boxCollider.size.x,
                boxCollider.size.y,
                boxCollider.size.z,
                index,
                spawnCount);

            return boxCollider.transform.TransformPoint(localPoint);
        }

        private static Vector3 ResolveBoundsFacePosition(Bounds bounds, int index, int spawnCount)
        {
            ResolveGridCoordinates(index, spawnCount, bounds.size.x, bounds.size.y, out int row, out int column, out int columnsInRow, out int rowCount);

            float x = ResolveAxisOffset(column, columnsInRow, bounds.size.x);
            float y = ResolveVerticalOffset(row, rowCount, bounds.size.y);

            return new Vector3(
                bounds.center.x + x,
                bounds.center.y + y,
                bounds.max.z);
        }

        private static Vector3 ResolveFaceLocalPoint(
            Vector3 localCenter,
            float width,
            float height,
            float depth,
            int index,
            int spawnCount)
        {
            ResolveGridCoordinates(index, spawnCount, width, height, out int row, out int column, out int columnsInRow, out int rowCount);

            return localCenter + new Vector3(
                ResolveAxisOffset(column, columnsInRow, width),
                ResolveVerticalOffset(row, rowCount, height),
                depth * 0.5f);
        }

        private static void ResolveGridCoordinates(
            int index,
            int spawnCount,
            float width,
            float height,
            out int row,
            out int column,
            out int columnsInRow,
            out int rowCount)
        {
            float safeWidth = Mathf.Max(width, 0.01f);
            float safeHeight = Mathf.Max(height, 0.01f);
            float aspect = safeWidth / safeHeight;

            int columnCount = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(spawnCount * aspect)));
            rowCount = Mathf.Max(1, Mathf.CeilToInt(spawnCount / (float)columnCount));

            row = Mathf.Clamp(index / columnCount, 0, rowCount - 1);
            bool isLastRow = row == rowCount - 1;
            int remainder = spawnCount % columnCount;
            columnsInRow = isLastRow && remainder > 0 ? remainder : columnCount;

            int rowStartIndex = row * columnCount;
            column = Mathf.Clamp(index - rowStartIndex, 0, Mathf.Max(0, columnsInRow - 1));
        }

        private static float ResolveAxisOffset(int index, int count, float extent)
        {
            if (count <= 1)
                return 0f;

            float step = extent / count;
            return -extent * 0.5f + step * (index + 0.5f);
        }

        private static float ResolveVerticalOffset(int row, int rowCount, float extent)
        {
            if (rowCount <= 1)
                return 0f;

            float step = extent / rowCount;
            return extent * 0.5f - step * (row + 0.5f);
        }
    }
}
