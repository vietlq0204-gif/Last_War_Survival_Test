using UnityEngine;

namespace Vit.SpawnKit.Algorithms
{
/// <summary>
/// Algorithm that provides a spawn pose.
/// </summary>
public interface ISpawnAlgorithm
{
    void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation);
}

/// <summary>
/// Simple circular placement around an origin.
/// </summary>
public sealed class SimplePointAlgorithm : ISpawnAlgorithm
{
    private readonly Vector3 _origin;
    private readonly float _radius;

    public SimplePointAlgorithm(Vector3 origin, float radius)
    {
        _origin = origin;
        _radius = radius;
    }

    public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
    {
        uint x = (uint)(index + 1) * 747796405u + seed * 2891336453u;
        x ^= x >> 16;
        float t = (x & 0xFFFF) / 65535f;

        float angle = t * Mathf.PI * 2f;
        position = _origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _radius;
        rotation = Quaternion.identity;
    }
}

/// <summary>
/// Fixed pose algorithm used when spawning a single object at a known pose.
/// </summary>
public sealed class FixedPoseAlgorithm : ISpawnAlgorithm
{
    private readonly Vector3 _position;
    private readonly Quaternion _rotation;

    public FixedPoseAlgorithm(Vector3 position, Quaternion rotation)
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

/// <summary>
/// Auto-seed mode when the request seed is 0.
/// </summary>
public enum SeedMode
{
    RuntimeRandom = 0,
    SessionStable = 1,
    ContextStable = 2
}

/// <summary>
/// Resolves the effective seed for a spawn request.
/// </summary>
public static class SeedResolver
{
    private static uint _sessionSeed;
    private static uint _counter;

    public static void SetSessionSeed(uint sessionSeed)
    {
        _sessionSeed = sessionSeed == 0 ? 1u : sessionSeed;
        _counter = 0;
    }

    public static uint Resolve(uint seed, SeedMode mode, int keyHash, int contextHash = 0)
    {
        if (seed != 0) return seed;

        if (_sessionSeed == 0)
        {
            uint t = (uint)System.Environment.TickCount;
            _sessionSeed = Hash(t ^ 0x9E3779B9u);
            if (_sessionSeed == 0) _sessionSeed = 1u;
            _counter = 0;
        }

        switch (mode)
        {
            case SeedMode.RuntimeRandom:
            {
                uint t = (uint)System.Environment.TickCount;
                return NonZero(Hash(_sessionSeed ^ t ^ (++_counter) ^ (uint)keyHash ^ (uint)contextHash));
            }

            case SeedMode.ContextStable:
                return NonZero(Hash(_sessionSeed ^ (uint)keyHash ^ (uint)contextHash));

            case SeedMode.SessionStable:
            default:
                return NonZero(Hash(_sessionSeed ^ (++_counter) ^ (uint)keyHash ^ (uint)contextHash));
        }
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    private static uint NonZero(uint x) => x == 0 ? 1u : x;
}
}
