using System;
using UnityEngine;

namespace Vit.SpawnKit.Data
{
/// <summary>
/// How a spawned object is returned to the pool.
/// </summary>
public enum SpawnReleaseMode
{
    Manual = 0,
    OnDisable = 1,
    AfterSeconds = 2
}

/// <summary>
/// Default lifecycle policy for spawned instances.
/// </summary>
[Serializable]
public struct SpawnLifecycle
{
    public SpawnReleaseMode mode;
    [Min(0f)] public float delaySeconds;
    public bool useUnscaledTime;

    public bool IsTimed => mode == SpawnReleaseMode.AfterSeconds && delaySeconds > 0f;

    public static SpawnLifecycle Manual => new SpawnLifecycle
    {
        mode = SpawnReleaseMode.Manual,
        delaySeconds = 0f,
        useUnscaledTime = false
    };

    public void Sanitize()
    {
        if (delaySeconds < 0f) delaySeconds = 0f;
        if (mode != SpawnReleaseMode.AfterSeconds) delaySeconds = 0f;
    }
}
}
