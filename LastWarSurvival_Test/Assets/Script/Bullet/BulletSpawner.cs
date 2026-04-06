using System.Collections.Generic;
using UnityEngine;
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

    [Header("References")]
    [SerializeField] private SpawnableSO bulletSpawnable;
    [SerializeField] private SpawnGridQueue teammateGridSource;
    [SerializeField] private Transform bulletParent;
    [SerializeField] private Collider homeCollider;
    [SerializeField] private Collider[] bulletSpawnVolumes;

    [Header("Fire")]
    [SerializeField, Min(0.01f)] private float fireInterval = 0.15f;
    [SerializeField, Min(1)] private int bulletsPerTeammate = 1;
    [SerializeField, Min(0)] private int maxBulletsPerVolley;
    [SerializeField, Min(0f)] private float bulletLifetime = 2f;
    [SerializeField, Min(0)] private int poolSizePadding = 8;
    [SerializeField] private bool prewarmPoolOnStart = true;

    [Header("Distribution")]
    [SerializeField, Min(1)] private int maxTryPerBullet = 24;
    [SerializeField, Min(1)] private int candidatesPerBullet = 16;
    [SerializeField, Min(0f)] private float minSpawnDistance = 0.35f;
    [SerializeField, Min(1)] private int placementBufferCapacity = 128;

    [Header("Home Detection")]
    [SerializeField] private LayerMask playerDetectionLayers = ~0;
    [SerializeField, Min(1)] private int overlapBufferSize = 8;

    private readonly List<GameObject> _spawnBuffer = new List<GameObject>(64);

    private RuntimeMultiColliderVolumeAlgorithm _spawnAlgorithm;
    private Collider[] _resolvedSpawnVolumes = System.Array.Empty<Collider>();
    private Collider[] _playerOverlapBuffer = System.Array.Empty<Collider>();

    private float _nextFireTime;
    private int _preparedPoolSize;
    private SpawnableSO _preparedPoolSpawnable;
    private bool _hasWarnedMissingSpawnable;
    private bool _hasWarnedMissingTeammateGrid;
    private bool _hasWarnedMissingHomeCollider;
    private bool _hasWarnedMissingHomeTag;
    private bool _hasWarnedMissingSpawnManager;
    private bool _hasWarnedMissingSpawnVolumes;
    private bool _hasWarnedMissingBulletComponent;

    private void Reset()
    {
        AutoAssignHomeCollider();
    }

    private void Awake()
    {
        AutoAssignHomeCollider();
        EnsureRuntimeCaches();
    }

    private void OnValidate()
    {
        fireInterval = Mathf.Max(0.01f, fireInterval);
        bulletsPerTeammate = Mathf.Max(1, bulletsPerTeammate);
        maxBulletsPerVolley = Mathf.Max(0, maxBulletsPerVolley);
        bulletLifetime = Mathf.Max(0f, bulletLifetime);
        poolSizePadding = Mathf.Max(0, poolSizePadding);
        maxTryPerBullet = Mathf.Max(1, maxTryPerBullet);
        candidatesPerBullet = Mathf.Max(1, candidatesPerBullet);
        minSpawnDistance = Mathf.Max(0f, minSpawnDistance);
        placementBufferCapacity = Mathf.Max(1, placementBufferCapacity);
        overlapBufferSize = Mathf.Max(1, overlapBufferSize);

        AutoAssignHomeCollider();
        EnsureRuntimeCaches();
    }

    private void Start()
    {
        EnsureRuntimeCaches();

        if (prewarmPoolOnStart)
            PreparePool(Mathf.Max(1, ResolveVolleyBulletCount()));
    }

    private void Update()
    {
        if (Time.time < _nextFireTime)
            return;

        if (!CanFire())
            return;

        int bulletCount = ResolveVolleyBulletCount();
        if (bulletCount <= 0)
            return;

        if (!IsPlayerInsideHome())
            return;

        if (FireVolley(bulletCount))
            _nextFireTime = Time.time + fireInterval;
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

        if (!HasSpawnManager())
        {
            WarnOnce(ref _hasWarnedMissingSpawnManager, "BulletSpawner could not find a SpawnManager in the scene.");
            return false;
        }

        _hasWarnedMissingSpawnManager = false;

        var resolvedHomeCollider = ResolveHomeCollider();
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

        if (BuildSpawnVolumeSet(resolvedHomeCollider) <= 0)
        {
            WarnOnce(ref _hasWarnedMissingSpawnVolumes, "BulletSpawner needs at least one valid spawn collider.");
            return false;
        }

        _hasWarnedMissingSpawnVolumes = false;
        return true;
    }

    private bool FireVolley(int bulletCount)
    {
        var resolvedHomeCollider = ResolveHomeCollider();
        if (resolvedHomeCollider == null)
            return false;

        int spawnVolumeCount = BuildSpawnVolumeSet(resolvedHomeCollider);
        if (spawnVolumeCount <= 0)
            return false;

        PreparePool(bulletCount);

        Transform spawnParent = ResolveSpawnParent();
        Vector3 fallbackPosition = spawnParent != null ? spawnParent.position : transform.position;

        _spawnAlgorithm.Configure(
            _resolvedSpawnVolumes,
            spawnVolumeCount,
            fallbackPosition,
            maxTryPerBullet,
            candidatesPerBullet,
            minSpawnDistance,
            Mathf.Max(placementBufferCapacity, bulletCount));

        SpawnLifecycle? lifecycle = bulletLifetime > 0f
            ? new SpawnLifecycle
            {
                mode = SpawnReleaseMode.AfterSeconds,
                delaySeconds = bulletLifetime,
                useUnscaledTime = false
            }
            : (SpawnLifecycle?)null;

        int spawnedCount = SpawnKit.SpawnNonAlloc(
            bulletSpawnable,
            bulletCount,
            _spawnBuffer,
            spawnParent,
            _spawnAlgorithm,
            seed: 0,
            lifecycle: lifecycle);

        if (spawnedCount <= 0)
            return false;

        for (int i = 0; i < spawnedCount; i++)
        {
            var bulletObject = _spawnBuffer[i];
            if (bulletObject == null)
                continue;

            if (bulletObject.TryGetComponent(out Bullet bullet))
            {
                bullet.LaunchForward();
                _hasWarnedMissingBulletComponent = false;
                continue;
            }

            WarnOnce(
                ref _hasWarnedMissingBulletComponent,
                $"Spawned bullet '{bulletObject.name}' is missing a Bullet component.");
        }

        return true;
    }

    private int ResolveVolleyBulletCount()
    {
        if (teammateGridSource == null)
            return 0;

        int teammateCount = Mathf.Max(0, teammateGridSource.GetOccupiedSlotCount());
        if (teammateCount <= 0)
            return 0;

        int bulletCount = MultiplyClamped(teammateCount, bulletsPerTeammate);
        if (maxBulletsPerVolley > 0)
            bulletCount = Mathf.Min(bulletCount, maxBulletsPerVolley);

        return bulletCount;
    }

    private bool IsPlayerInsideHome()
    {
        var resolvedHomeCollider = ResolveHomeCollider();
        if (resolvedHomeCollider == null || !resolvedHomeCollider.CompareTag(HomeTag))
            return false;

        EnsurePlayerOverlapBuffer();

        int hitCount = OverlapHomeVolume(resolvedHomeCollider);
        for (int i = 0; i < hitCount; i++)
        {
            var hitCollider = _playerOverlapBuffer[i];
            if (hitCollider == null || hitCollider == resolvedHomeCollider)
                continue;

            if (HasTagInHierarchy(hitCollider, PlayerTag))
                return true;
        }

        return false;
    }

    private int OverlapHomeVolume(Collider sourceCollider)
    {
        int layerMask = ResolvePlayerDetectionLayerMask();

        if (sourceCollider is BoxCollider boxCollider)
        {
            Vector3 halfExtents = Vector3.Scale(boxCollider.size * 0.5f, Abs(boxCollider.transform.lossyScale));
            Vector3 center = boxCollider.transform.TransformPoint(boxCollider.center);

            return Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _playerOverlapBuffer,
                boxCollider.transform.rotation,
                layerMask,
                QueryTriggerInteraction.Collide);
        }

        if (sourceCollider is SphereCollider sphereCollider)
        {
            Vector3 center = sphereCollider.transform.TransformPoint(sphereCollider.center);
            float radius = sphereCollider.radius * MaxAbs(sphereCollider.transform.lossyScale);

            return Physics.OverlapSphereNonAlloc(
                center,
                radius,
                _playerOverlapBuffer,
                layerMask,
                QueryTriggerInteraction.Collide);
        }

        if (sourceCollider is CapsuleCollider capsuleCollider)
        {
            Vector3 lossyScale = Abs(capsuleCollider.transform.lossyScale);
            Vector3 center = capsuleCollider.transform.TransformPoint(capsuleCollider.center);
            Vector3 direction = ResolveCapsuleDirection(capsuleCollider.transform, capsuleCollider.direction);
            float radius = capsuleCollider.radius * ResolveCapsuleRadiusScale(lossyScale, capsuleCollider.direction);
            float height = Mathf.Max(
                capsuleCollider.height * ResolveCapsuleHeightScale(lossyScale, capsuleCollider.direction),
                radius * 2f);
            float halfSegment = Mathf.Max(0f, (height * 0.5f) - radius);
            Vector3 segmentOffset = direction * halfSegment;

            return Physics.OverlapCapsuleNonAlloc(
                center + segmentOffset,
                center - segmentOffset,
                radius,
                _playerOverlapBuffer,
                layerMask,
                QueryTriggerInteraction.Collide);
        }

        Bounds bounds = sourceCollider.bounds;
        return Physics.OverlapBoxNonAlloc(
            bounds.center,
            bounds.extents,
            _playerOverlapBuffer,
            Quaternion.identity,
            layerMask,
            QueryTriggerInteraction.Collide);
    }

    private void PreparePool(int bulletCount)
    {
        if (bulletSpawnable == null || bulletCount <= 0)
            return;

        if (_preparedPoolSpawnable != bulletSpawnable)
        {
            _preparedPoolSpawnable = bulletSpawnable;
            _preparedPoolSize = 0;
        }

        int concurrentVolleys = bulletLifetime > 0f && fireInterval > 0f
            ? Mathf.Max(1, Mathf.CeilToInt(bulletLifetime / fireInterval))
            : 2;

        int desiredPoolSize = MultiplyClamped(bulletCount, concurrentVolleys);
        desiredPoolSize = Mathf.Max(1, desiredPoolSize + poolSizePadding);

        if (desiredPoolSize <= _preparedPoolSize)
            return;

        int prewarmCount = Mathf.Min(desiredPoolSize, MultiplyClamped(bulletCount, Mathf.Min(concurrentVolleys, 2)) + poolSizePadding);
        int growStep = Mathf.Max(1, bulletCount);

        if (!SpawnKit.EnsurePoolCapacity(bulletSpawnable, desiredPoolSize, prewarmCount, growStep, allowGrow: true))
            return;

        _preparedPoolSize = desiredPoolSize;
    }

    private Transform ResolveSpawnParent()
    {
        return bulletParent != null ? bulletParent : transform;
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

    private int BuildSpawnVolumeSet(Collider fallbackCollider)
    {
        EnsureRuntimeCaches();

        int count = 0;
        if (bulletSpawnVolumes != null && bulletSpawnVolumes.Length > 0)
        {
            EnsureSpawnVolumeBuffer(bulletSpawnVolumes.Length);

            for (int i = 0; i < bulletSpawnVolumes.Length; i++)
            {
                var volume = bulletSpawnVolumes[i];
                if (volume == null)
                    continue;

                _resolvedSpawnVolumes[count++] = volume;
            }
        }

        if (count == 0 && fallbackCollider != null)
        {
            EnsureSpawnVolumeBuffer(1);
            _resolvedSpawnVolumes[0] = fallbackCollider;
            count = 1;
        }

        return count;
    }

    private void EnsureRuntimeCaches()
    {
        if (_spawnAlgorithm == null)
            _spawnAlgorithm = new RuntimeMultiColliderVolumeAlgorithm();

        EnsureSpawnVolumeBuffer(Mathf.Max(1, bulletSpawnVolumes != null ? bulletSpawnVolumes.Length : 1));
        EnsurePlayerOverlapBuffer();
    }

    private void EnsureSpawnVolumeBuffer(int requiredSize)
    {
        requiredSize = Mathf.Max(1, requiredSize);

        if (_resolvedSpawnVolumes.Length >= requiredSize)
            return;

        _resolvedSpawnVolumes = new Collider[requiredSize];
    }

    private void EnsurePlayerOverlapBuffer()
    {
        int desiredSize = Mathf.Max(1, overlapBufferSize);

        if (_playerOverlapBuffer.Length == desiredSize)
            return;

        _playerOverlapBuffer = new Collider[desiredSize];
    }

    private int ResolvePlayerDetectionLayerMask()
    {
        return playerDetectionLayers.value != 0 ? playerDetectionLayers.value : Physics.AllLayers;
    }

    private bool HasSpawnManager()
    {
        return SpawnManager.Instance != null || FindAnyObjectByType<SpawnManager>() != null;
    }

    private static bool HasTagInHierarchy(Collider other, string requiredTag)
    {
        if (other == null)
            return false;

        if (other.CompareTag(requiredTag))
            return true;

        var attachedRigidbody = other.attachedRigidbody;
        if (attachedRigidbody != null && attachedRigidbody.CompareTag(requiredTag))
            return true;

        Transform root = other.transform.root;
        return root != null && root.CompareTag(requiredTag);
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(
            Mathf.Abs(value.x),
            Mathf.Abs(value.y),
            Mathf.Abs(value.z));
    }

    private static float MaxAbs(Vector3 value)
    {
        return Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static Vector3 ResolveCapsuleDirection(Transform targetTransform, int direction)
    {
        switch (direction)
        {
            case 0:
                return targetTransform.right;
            case 1:
                return targetTransform.up;
            default:
                return targetTransform.forward;
        }
    }

    private static float ResolveCapsuleRadiusScale(Vector3 lossyScale, int direction)
    {
        switch (direction)
        {
            case 0:
                return Mathf.Max(lossyScale.y, lossyScale.z);
            case 1:
                return Mathf.Max(lossyScale.x, lossyScale.z);
            default:
                return Mathf.Max(lossyScale.x, lossyScale.y);
        }
    }

    private static float ResolveCapsuleHeightScale(Vector3 lossyScale, int direction)
    {
        switch (direction)
        {
            case 0:
                return lossyScale.x;
            case 1:
                return lossyScale.y;
            default:
                return lossyScale.z;
        }
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

    private sealed class RuntimeMultiColliderVolumeAlgorithm : ISpawnAlgorithm, ISpawnBatchReset
    {
        private Collider[] _colliders = System.Array.Empty<Collider>();
        private float[] _weights = System.Array.Empty<float>();
        private Vector3[] _placed = System.Array.Empty<Vector3>();

        private int _colliderCount;
        private int _placedCount;
        private int _maxTryPerPoint;
        private int _candidatesPerPoint;
        private float _minDistance;
        private float _minDistanceSqr;
        private float _weightSum;
        private Vector3 _fallbackPosition;

        public void Configure(
            Collider[] colliders,
            int colliderCount,
            Vector3 fallbackPosition,
            int maxTryPerPoint,
            int candidatesPerPoint,
            float minDistance,
            int maxCount)
        {
            _fallbackPosition = fallbackPosition;
            _maxTryPerPoint = Mathf.Max(1, maxTryPerPoint);
            _candidatesPerPoint = Mathf.Max(1, candidatesPerPoint);
            _minDistance = Mathf.Max(0f, minDistance);
            _minDistanceSqr = _minDistance * _minDistance;

            EnsureColliderCapacity(Mathf.Max(1, colliderCount));

            _colliderCount = 0;
            _weightSum = 0f;

            for (int i = 0; i < colliderCount; i++)
            {
                var collider = colliders[i];
                if (collider == null)
                    continue;

                _colliders[_colliderCount] = collider;

                float weight = EstimateBoundsWeight(collider.bounds);
                _weights[_colliderCount] = weight;
                _weightSum += weight;
                _colliderCount++;
            }

            if (_colliderCount > 0)
                _fallbackPosition = _colliders[0].bounds.center;

            EnsurePlacedCapacity(Mathf.Max(1, maxCount));
            _placedCount = 0;
        }

        public void ResetPlaced()
        {
            _placedCount = 0;
        }

        public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            position = _fallbackPosition;

            if (_colliderCount <= 0 || _placed.Length <= 0)
                return;

            uint state = Hash((uint)(index + 1) ^ seed);
            Vector3 best = position;
            float bestScore = -1f;
            int totalTries = _maxTryPerPoint;

            while (totalTries-- > 0)
            {
                for (int candidateIndex = 0; candidateIndex < _candidatesPerPoint; candidateIndex++)
                {
                    var collider = PickCollider(ref state);
                    if (collider == null)
                        continue;

                    Vector3 sample = SampleInsideBounds(ref state, collider.bounds);
                    Vector3 closestPoint = collider.ClosestPoint(sample);
                    if ((closestPoint - sample).sqrMagnitude > 1e-6f)
                        continue;

                    float nearestSqr = float.PositiveInfinity;
                    for (int placedIndex = 0; placedIndex < _placedCount; placedIndex++)
                    {
                        float distanceSqr = (sample - _placed[placedIndex]).sqrMagnitude;
                        if (distanceSqr < nearestSqr)
                            nearestSqr = distanceSqr;

                        if (_minDistance > 0f && nearestSqr < _minDistanceSqr)
                            break;
                    }

                    if (_placedCount == 0)
                    {
                        best = sample;
                        bestScore = float.PositiveInfinity;
                        goto Accept;
                    }

                    if (_minDistance > 0f && nearestSqr < _minDistanceSqr)
                        continue;

                    if (nearestSqr > bestScore)
                    {
                        bestScore = nearestSqr;
                        best = sample;
                    }
                }

                if (bestScore >= 0f)
                    break;
            }

        Accept:
            position = best;

            if (_placedCount < _placed.Length)
                _placed[_placedCount++] = best;
        }

        private Collider PickCollider(ref uint state)
        {
            if (_colliderCount <= 0)
                return null;

            if (_colliderCount == 1 || _weightSum <= 0f)
                return _colliders[0];

            float pick = To01(Next(ref state)) * _weightSum;
            float cumulative = 0f;

            for (int i = 0; i < _colliderCount; i++)
            {
                cumulative += _weights[i];
                if (pick <= cumulative)
                    return _colliders[i];
            }

            return _colliders[_colliderCount - 1];
        }

        private void EnsureColliderCapacity(int requiredSize)
        {
            if (_colliders.Length < requiredSize)
                _colliders = new Collider[requiredSize];

            if (_weights.Length < requiredSize)
                _weights = new float[requiredSize];
        }

        private void EnsurePlacedCapacity(int requiredSize)
        {
            if (_placed.Length >= requiredSize)
                return;

            _placed = new Vector3[requiredSize];
        }

        private static float EstimateBoundsWeight(Bounds bounds)
        {
            Vector3 size = bounds.size;
            float volume = Mathf.Abs(size.x * size.y * size.z);
            return volume > 1e-6f ? volume : 1f;
        }

        private static Vector3 SampleInsideBounds(ref uint state, Bounds bounds)
        {
            float rx = To01(Next(ref state));
            float ry = To01(Next(ref state));
            float rz = To01(Next(ref state));

            return new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, rx),
                Mathf.Lerp(bounds.min.y, bounds.max.y, ry),
                Mathf.Lerp(bounds.min.z, bounds.max.z, rz));
        }

        private static uint Next(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return state;
        }

        private static float To01(uint value)
        {
            return (value >> 8) * (1f / 16777216f);
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value == 0 ? 1u : value;
        }
    }
}
