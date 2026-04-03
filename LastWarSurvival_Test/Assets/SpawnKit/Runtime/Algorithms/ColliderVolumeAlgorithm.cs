using UnityEngine;

namespace Vit.SpawnKit.Algorithms
{
/// <summary>
/// Interface for algorithms that need a reset before each batch spawn.
/// </summary>
public interface ISpawnBatchReset
{
    void ResetPlaced();
}

/// <summary>
/// Spawns random points inside a single collider using bounds sampling and inside checks.
/// </summary>
public sealed class ColliderVolumeAlgorithm : ISpawnAlgorithm, ISpawnBatchReset
{
    private readonly Collider _collider;
    private readonly int _maxTryPerPoint;
    private readonly int _candidatesPerPoint;
    private readonly float _minDistance;
    private readonly float _minDistanceSqr;
    private readonly Vector3[] _placed;
    private int _placedCount;

    public ColliderVolumeAlgorithm(
        Collider collider,
        int maxTryPerPoint = 32,
        int candidatesPerPoint = 16,
        float minDistance = 0.5f,
        int maxCount = 256)
    {
        _collider = collider;
        _maxTryPerPoint = Mathf.Max(1, maxTryPerPoint);
        _candidatesPerPoint = Mathf.Max(1, candidatesPerPoint);
        _minDistance = Mathf.Max(0f, minDistance);
        _minDistanceSqr = _minDistance * _minDistance;

        _placed = new Vector3[Mathf.Max(1, maxCount)];
        _placedCount = 0;
    }

    public void ResetPlaced()
    {
        _placedCount = 0;
    }

    public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
    {
        rotation = Quaternion.identity;

        position = _collider != null ? _collider.bounds.center : Vector3.zero;
        if (_collider == null) return;

        var bounds = _collider.bounds;
        if (index >= _placed.Length)
        {
            position = bounds.center;
            return;
        }

        uint state = Hash((uint)(index + 1) ^ seed);
        Vector3 best = bounds.center;
        float bestScore = -1f;

        int totalTries = _maxTryPerPoint;
        while (totalTries-- > 0)
        {
            for (int c = 0; c < _candidatesPerPoint; c++)
            {
                Vector3 sample = SampleInsideBounds(ref state, bounds);

                var closestPoint = _collider.ClosestPoint(sample);
                if ((closestPoint - sample).sqrMagnitude > 1e-6f) continue;

                float nearestSqr = float.PositiveInfinity;
                for (int i = 0; i < _placedCount; i++)
                {
                    float distanceSqr = (sample - _placed[i]).sqrMagnitude;
                    if (distanceSqr < nearestSqr) nearestSqr = distanceSqr;

                    if (_minDistance > 0f && nearestSqr < _minDistanceSqr)
                        break;
                }

                if (_placedCount == 0)
                {
                    best = sample;
                    bestScore = float.PositiveInfinity;
                    goto ACCEPT;
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

    ACCEPT:
        position = best;
        _placed[_placedCount++] = best;
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

    private static float To01(uint x)
    {
        return (x >> 8) * (1f / 16777216f);
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x == 0 ? 1u : x;
    }
}

/// <summary>
/// Spawns random points across a set of colliders using weighted selection.
/// </summary>
public sealed class MultiColliderVolumeAlgorithm : ISpawnAlgorithm, ISpawnBatchReset
{
    private readonly Collider[] _colliders;
    private readonly float[] _weights;
    private readonly int _maxTryPerPoint;
    private readonly int _candidatesPerPoint;
    private readonly float _minDistance;
    private readonly float _minDistanceSqr;
    private readonly Vector3[] _placed;
    private int _placedCount;

    public MultiColliderVolumeAlgorithm(
        Collider[] colliders,
        int maxTryPerPoint = 32,
        int candidatesPerPoint = 16,
        float minDistance = 0.5f,
        int maxCount = 256)
    {
        _maxTryPerPoint = Mathf.Max(1, maxTryPerPoint);
        _candidatesPerPoint = Mathf.Max(1, candidatesPerPoint);
        _minDistance = Mathf.Max(0f, minDistance);
        _minDistanceSqr = _minDistance * _minDistance;
        _placed = new Vector3[Mathf.Max(1, maxCount)];

        if (colliders == null || colliders.Length == 0)
        {
            _colliders = System.Array.Empty<Collider>();
            _weights = System.Array.Empty<float>();
            return;
        }

        int validCount = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) validCount++;
        }

        _colliders = new Collider[validCount];
        _weights = new float[validCount];

        int write = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null) continue;

            _colliders[write] = collider;
            _weights[write] = EstimateBoundsWeight(collider.bounds);
            write++;
        }
    }

    public void ResetPlaced()
    {
        _placedCount = 0;
    }

    public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        position = _colliders.Length > 0 ? _colliders[0].bounds.center : Vector3.zero;
        if (_colliders.Length == 0) return;

        if (index >= _placed.Length)
        {
            position = _colliders[0].bounds.center;
            return;
        }

        uint state = Hash((uint)(index + 1) ^ seed);
        Vector3 best = position;
        float bestScore = -1f;

        int totalTries = _maxTryPerPoint;
        while (totalTries-- > 0)
        {
            for (int c = 0; c < _candidatesPerPoint; c++)
            {
                var collider = PickCollider(ref state);
                if (collider == null) continue;

                Vector3 sample = SampleInsideBounds(ref state, collider.bounds);
                var closestPoint = collider.ClosestPoint(sample);
                if ((closestPoint - sample).sqrMagnitude > 1e-6f) continue;

                float nearestSqr = float.PositiveInfinity;
                for (int i = 0; i < _placedCount; i++)
                {
                    float distanceSqr = (sample - _placed[i]).sqrMagnitude;
                    if (distanceSqr < nearestSqr) nearestSqr = distanceSqr;
                    if (_minDistance > 0f && nearestSqr < _minDistanceSqr)
                        break;
                }

                if (_placedCount == 0)
                {
                    best = sample;
                    bestScore = float.PositiveInfinity;
                    goto ACCEPT;
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

    ACCEPT:
        position = best;
        _placed[_placedCount++] = best;
    }

    private Collider PickCollider(ref uint state)
    {
        if (_colliders.Length == 1) return _colliders[0];

        int index = WeightedPicker.PickIndex(_weights, Next(ref state), 0xA511E9B3u);
        if (index < 0 || index >= _colliders.Length) index = 0;
        return _colliders[index];
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

    private static float To01(uint x)
    {
        return (x >> 8) * (1f / 16777216f);
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x == 0 ? 1u : x;
    }
}

/// <summary>
/// Spawns full count on each collider instead of sharing count across the whole set.
/// </summary>
public sealed class PerVolumeColliderVolumeAlgorithm : ISpawnAlgorithm, ISpawnBatchReset
{
    private readonly ColliderVolumeAlgorithm[] _algorithms;
    private readonly int _countPerVolume;

    public PerVolumeColliderVolumeAlgorithm(
        Collider[] colliders,
        int countPerVolume,
        int maxTryPerPoint = 32,
        int candidatesPerPoint = 16,
        float minDistance = 0.5f,
        int maxCount = 256)
    {
        _countPerVolume = Mathf.Max(1, countPerVolume);

        if (colliders == null || colliders.Length == 0)
        {
            _algorithms = System.Array.Empty<ColliderVolumeAlgorithm>();
            return;
        }

        int validCount = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) validCount++;
        }

        _algorithms = new ColliderVolumeAlgorithm[validCount];

        int write = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null) continue;

            _algorithms[write] = new ColliderVolumeAlgorithm(
                collider,
                maxTryPerPoint,
                candidatesPerPoint,
                minDistance,
                maxCount);
            write++;
        }
    }

    public void ResetPlaced()
    {
        for (int i = 0; i < _algorithms.Length; i++)
        {
            _algorithms[i]?.ResetPlaced();
        }
    }

    public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        position = Vector3.zero;

        if (_algorithms.Length == 0) return;

        int volumeIndex = Mathf.Clamp(index / _countPerVolume, 0, _algorithms.Length - 1);
        int localIndex = index % _countPerVolume;
        uint volumeSeed = seed ^ ((uint)(volumeIndex + 1) * 0x9E3779B9u);

        _algorithms[volumeIndex].GetPose(localIndex, volumeSeed, out position, out rotation);
    }
}
}
