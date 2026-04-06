using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.ScriptableObjects;
using Vit.SpawnKit.Services;

[DisallowMultipleComponent]
public sealed class BulletSpawner : MonoBehaviour
{
    private const string PlayerTag = "Player";
    private const string HomeTag = "Home";
    private const string EnemyTag = "Enemy";
    private static readonly Vector3 BulletDirection = Vector3.forward;

    [Header("References")]
    [SerializeField] private SpawnableSO bulletSpawnable;
    [SerializeField] private SpawnGridQueue teammateGridSource;
    [SerializeField] private Transform bulletParent;
    [SerializeField] private Collider homeCollider;
    [SerializeField] private Collider bulletSpawnAreaCollider;
    [SerializeField] private Transform[] muzzles;

    [Header("Fire")]
    [SerializeField, Min(0.01f)] private float fireInterval = 0.15f;
    [SerializeField, Min(1)] private int bulletsPerTeammate = 1;
    [SerializeField, Min(0)] private int maxBulletsPerVolley;
    [SerializeField, Min(1)] private int maxActiveBullets = 128;
    [SerializeField, Min(0.01f)] private float bulletLifetime = 2f;
    [SerializeField] private bool prewarmPoolOnStart = true;
    [SerializeField] private bool clampVolleyToSustainableCadence = true;

    [Header("Spawn Area")]
    [SerializeField] private bool useFormationSpawnColliderAsSpawnArea = true;
    [SerializeField, Min(0f)] private float spawnAreaWidthPerTeammate = 0.08f;
    [SerializeField, Min(0f)] private float spawnAreaHeightPerTeammate = 0f;
    [SerializeField, Min(0f)] private float maxSpawnAreaWidth = 4f;
    [SerializeField, Min(0f)] private float maxSpawnAreaHeight = 1.5f;

    [Header("Hit Detection")]
    [SerializeField] private LayerMask targetLayers = 1 << 6;
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
    private int _preparedPoolSize;
    private SpawnableSO _preparedPoolSpawnable;
    private SpawnManager _cachedSpawnManager;

    private bool _hasWarnedMissingSpawnable;
    private bool _hasWarnedMissingTeammateGrid;
    private bool _hasWarnedMissingHomeCollider;
    private bool _hasWarnedMissingHomeTag;
    private bool _hasWarnedHomeNotTrigger;
    private bool _hasWarnedMissingSpawnManager;
    private bool _hasWarnedMissingMuzzles;
    private bool _hasWarnedMissingBulletComponent;

    private BoxCollider _cachedSpawnAreaBoxCollider;
    private Vector3 _spawnAreaBaseSize;
    private Vector3 _spawnAreaBaseCenter;
    private int _lastSpawnAreaTeammateCount = -1;

    private struct ActiveBulletRuntime
    {
        public Bullet bullet;
        public Transform cachedTransform;
        public Vector3 position;
        public float speed;
        public float remainingLifetime;
        public float hitRadius;
    }

    private void Reset()
    {
        AutoAssignHomeCollider();
        AutoAssignBulletSpawnAreaCollider();
    }

    private void Awake()
    {
        AutoAssignHomeCollider();
        EnsureRuntimeCaches();
        EnsureHomeTriggerRelays();
        RefreshHomeOccupants();
    }

    private void OnEnable()
    {
        EnsureHomeTriggerRelays();
        RefreshHomeOccupants();
        _nextFireTime = Time.time;
    }

    private void OnValidate()
    {
        fireInterval = Mathf.Max(0.01f, fireInterval);
        bulletsPerTeammate = Mathf.Max(1, bulletsPerTeammate);
        maxBulletsPerVolley = Mathf.Max(0, maxBulletsPerVolley);
        maxActiveBullets = Mathf.Max(1, maxActiveBullets);
        bulletLifetime = Mathf.Max(0.01f, bulletLifetime);
        spawnAreaWidthPerTeammate = Mathf.Max(0f, spawnAreaWidthPerTeammate);
        spawnAreaHeightPerTeammate = Mathf.Max(0f, spawnAreaHeightPerTeammate);
        maxSpawnAreaWidth = Mathf.Max(0f, maxSpawnAreaWidth);
        maxSpawnAreaHeight = Mathf.Max(0f, maxSpawnAreaHeight);
        hitBufferSize = Mathf.Max(1, hitBufferSize);

        AutoAssignHomeCollider();
        AutoAssignBulletSpawnAreaCollider();
        EnsureRuntimeCaches();
    }

    private void Start()
    {
        EnsureRuntimeCaches();
        EnsureHomeTriggerRelays();
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
        ReleaseHomeTriggerRelays();
        DespawnAllActiveBullets();
        _playersInsideHome.Clear();
        _nextFireTime = 0f;
    }

    private void OnDestroy()
    {
        ReleaseHomeTriggerRelays();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (homeCollider == null || homeCollider.gameObject != gameObject)
            return;

        HandleHomeTriggerEnter(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (homeCollider == null || homeCollider.gameObject != gameObject)
            return;

        HandleHomeTriggerExit(other);
    }

    public void HandleHomeTriggerEnterFromRelay(Collider sourceCollider, Collider other)
    {
        if (!IsManagedHomeCollider(sourceCollider))
            return;

        HandleHomeTriggerEnter(other);
    }

    public void HandleHomeTriggerExitFromRelay(Collider sourceCollider, Collider other)
    {
        if (!IsManagedHomeCollider(sourceCollider))
            return;

        HandleHomeTriggerExit(other);
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
        if (bulletSpawnable == null)
        {
            WarnOnce(ref _hasWarnedMissingSpawnable, "BulletSpawner needs a bulletSpawnable.");
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

        Collider resolvedHomeCollider = ResolveHomeCollider();
        if (resolvedHomeCollider == null)
        {
            WarnOnce(ref _hasWarnedMissingHomeCollider, "BulletSpawner needs a Home collider.");
            return false;
        }

        _hasWarnedMissingHomeCollider = false;

        if (!resolvedHomeCollider.CompareTag(HomeTag))
        {
            WarnOnce(ref _hasWarnedMissingHomeTag, "BulletSpawner homeCollider must use tag 'Home'.");
            return false;
        }

        _hasWarnedMissingHomeTag = false;

        if (!resolvedHomeCollider.isTrigger)
        {
            WarnOnce(ref _hasWarnedHomeNotTrigger, "BulletSpawner homeCollider must be a trigger collider.");
            return false;
        }

        _hasWarnedHomeNotTrigger = false;

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
        int muzzleCount = BuildMuzzleSet();
        Collider spawnAreaCollider = ResolveBulletSpawnAreaCollider();
        if (spawnAreaCollider == null && muzzleCount <= 0)
            return false;

        PreparePool();

        Transform spawnParent = ResolveSpawnParent();
        Vector3 fallbackPosition = spawnParent != null ? spawnParent.position : transform.position;
        _spawnAlgorithm.Configure(spawnAreaCollider, bulletCount, _resolvedMuzzles, muzzleCount, fallbackPosition);

        int spawnedCount = SpawnKit.SpawnNonAlloc(
            bulletSpawnable,
            bulletCount,
            _spawnBuffer,
            spawnParent,
            _spawnAlgorithm);

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
            remainingLifetime = bulletLifetime,
            hitRadius = Mathf.Max(0f, bullet.HitRadius),
        });
    }

    private void UpdateActiveBullets(float deltaTime)
    {
        if (_activeBullets.Count == 0 || deltaTime <= 0f)
            return;

        float targetDistanceScale = deltaTime;
        int layerMask = ResolveTargetLayerMask();

        for (int i = _activeBullets.Count - 1; i >= 0; i--)
        {
            var runtime = _activeBullets[i];
            if (!IsRuntimeValid(runtime))
            {
                RemoveActiveBulletAt(i);
                continue;
            }

            runtime.remainingLifetime -= deltaTime;
            if (runtime.remainingLifetime <= 0f)
            {
                DespawnRuntimeBullet(runtime);
                RemoveActiveBulletAt(i);
                continue;
            }

            float stepDistance = runtime.speed * targetDistanceScale;
            if (stepDistance <= 0f)
            {
                _activeBullets[i] = runtime;
                continue;
            }

            if (TryResolveEnemyHit(runtime.position, stepDistance, runtime.hitRadius, layerMask, out Vector3 hitPoint))
            {
                runtime.bullet.SetWorldPosition(hitPoint);
                DespawnRuntimeBullet(runtime);
                RemoveActiveBulletAt(i);
                continue;
            }

            runtime.position += BulletDirection * stepDistance;
            runtime.bullet.SetWorldPosition(runtime.position);
            _activeBullets[i] = runtime;
        }
    }

    private bool TryResolveEnemyHit(
        Vector3 origin,
        float distance,
        float hitRadius,
        int layerMask,
        out Vector3 hitPoint)
    {
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
            if (ResolveTaggedTransform(hitCollider, EnemyTag) == null)
                continue;

            if (hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            hitPoint = hit.point;
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

    private void DespawnRuntimeBullet(ActiveBulletRuntime runtime)
    {
        if (runtime.bullet == null)
            return;

        SpawnKit.Despawn(runtime.bullet.gameObject);
    }

    private void DespawnAllActiveBullets()
    {
        for (int i = _activeBullets.Count - 1; i >= 0; i--)
        {
            DespawnRuntimeBullet(_activeBullets[i]);
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

        int bulletCount = MultiplyClamped(teammateCount, bulletsPerTeammate);
        if (maxBulletsPerVolley > 0)
            bulletCount = Mathf.Min(bulletCount, maxBulletsPerVolley);

        if (clampVolleyToSustainableCadence)
            bulletCount = Mathf.Min(bulletCount, ResolveSustainableVolleyCount());

        int availableSlots = Mathf.Max(0, maxActiveBullets - _activeBullets.Count);
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

    private void PreparePool()
    {
        if (bulletSpawnable == null)
            return;

        if (_preparedPoolSpawnable != bulletSpawnable)
        {
            _preparedPoolSpawnable = bulletSpawnable;
            _preparedPoolSize = 0;
        }

        int desiredPoolSize = Mathf.Max(1, maxActiveBullets);
        if (desiredPoolSize <= _preparedPoolSize)
            return;

        int prewarmCount = prewarmPoolOnStart ? desiredPoolSize : Mathf.Min(desiredPoolSize, Mathf.Max(1, ResolveSafeMaxVolleyCount()));
        int growStep = Mathf.Max(1, ResolveSafeMaxVolleyCount());

        if (!SpawnKit.EnsurePoolCapacity(
                bulletSpawnable,
                desiredPoolSize,
                prewarmCount,
                growStep,
                allowGrow: false))
            return;

        _preparedPoolSize = desiredPoolSize;
    }

    private int ResolveSafeMaxVolleyCount()
    {
        int safeMaxVolley = maxBulletsPerVolley > 0
            ? Mathf.Min(maxBulletsPerVolley, maxActiveBullets)
            : Mathf.Max(1, maxActiveBullets);

        if (clampVolleyToSustainableCadence)
            safeMaxVolley = Mathf.Min(safeMaxVolley, ResolveSustainableVolleyCount());

        return Mathf.Max(1, safeMaxVolley);
    }

    private int ResolveSustainableVolleyCount()
    {
        if (maxActiveBullets <= 0)
            return 0;

        float safeInterval = Mathf.Max(0.01f, fireInterval);
        float safeLifetime = Mathf.Max(0.01f, bulletLifetime);
        int concurrentVolleyCount = Mathf.Max(1, Mathf.CeilToInt(safeLifetime / safeInterval));
        return Mathf.Max(1, maxActiveBullets / concurrentVolleyCount);
    }

    private void ScheduleNextFireTime()
    {
        float now = Time.time;
        if (_nextFireTime <= 0f)
        {
            _nextFireTime = now + fireInterval;
            return;
        }

        _nextFireTime += fireInterval;
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

    private Collider ResolveHomeCollider()
    {
        if (homeCollider != null)
            return homeCollider;

        AutoAssignHomeCollider();
        return homeCollider;
    }

    private void AutoAssignHomeCollider()
    {
        if (homeCollider != null)
            return;

        var colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            var candidate = colliders[i];
            if (candidate == null)
                continue;

            if (candidate.CompareTag(HomeTag))
            {
                homeCollider = candidate;
                return;
            }
        }

        if (TryGetComponent(out Collider localCollider))
        {
            homeCollider = localCollider;
            return;
        }

        if (colliders.Length > 0)
            homeCollider = colliders[0];
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

    private void EnsureHomeTriggerRelays()
    {
        Collider resolvedHomeCollider = ResolveHomeCollider();
        if (resolvedHomeCollider == null)
            return;

        RegisterHomeTriggerRelay(resolvedHomeCollider);

        var colliders = resolvedHomeCollider.transform.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            var candidate = colliders[i];
            RegisterHomeTriggerRelay(candidate);
        }
    }

    private void ReleaseHomeTriggerRelays()
    {
        Collider resolvedHomeCollider = ResolveHomeCollider();
        if (resolvedHomeCollider == null)
            return;

        UnregisterHomeTriggerRelay(resolvedHomeCollider);

        var colliders = resolvedHomeCollider.transform.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            UnregisterHomeTriggerRelay(colliders[i]);
        }
    }

    private void RegisterHomeTriggerRelay(Collider candidate)
    {
        if (candidate == null || candidate.gameObject == gameObject)
            return;

        if (!IsManagedHomeCollider(candidate))
            return;

        if (!candidate.TryGetComponent(out BulletSpawnerHomeRelay relay))
            relay = candidate.gameObject.AddComponent<BulletSpawnerHomeRelay>();

        relay.Register(this, candidate);
    }

    private void UnregisterHomeTriggerRelay(Collider candidate)
    {
        if (candidate == null || candidate.gameObject == gameObject)
            return;

        if (!candidate.TryGetComponent(out BulletSpawnerHomeRelay relay))
            return;

        relay.Unregister(this, candidate);
    }

    private void RefreshHomeOccupants()
    {
        _playersInsideHome.Clear();

        Collider resolvedHomeCollider = ResolveHomeCollider();
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
        Collider resolvedHomeCollider = ResolveHomeCollider();
        if (resolvedHomeCollider == null || playerRoot == null || !playerRoot.gameObject.activeInHierarchy)
            return false;

        Collider playerCollider = ResolvePrimaryPlayerCollider(playerRoot);
        if (playerCollider == null)
            return resolvedHomeCollider.bounds.Contains(playerRoot.position);

        return Physics.ComputePenetration(
            resolvedHomeCollider,
            resolvedHomeCollider.transform.position,
            resolvedHomeCollider.transform.rotation,
            playerCollider,
            playerCollider.transform.position,
            playerCollider.transform.rotation,
            out _,
            out _);
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

    private bool IsManagedHomeCollider(Collider candidate)
    {
        if (candidate == null)
            return false;

        if (candidate == homeCollider)
            return true;

        if (candidate.CompareTag(HomeTag))
            return true;

        return homeCollider != null && candidate.transform.IsChildOf(homeCollider.transform);
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

    private int ResolveTargetLayerMask()
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
               && candidate != homeCollider
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
