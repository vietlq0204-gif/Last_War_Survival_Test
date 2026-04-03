using System;
using UnityEngine;

namespace Vit.SpawnKit.Data
{
/// <summary>
/// Pool configuration for a spawnable.
/// </summary>
[Serializable]
public class PoolConfig
{
    [Tooltip("Number of instances created up front when the pool is initialized.")]
    [Min(0)] public int prewarmCount = 0;

    [Tooltip("Maximum number of instances the pool can hold.")]
    [Min(1)] public int maxSize = 64;

    [Tooltip("Number of instances created per growth step.")]
    [Min(1)] public int growStep = 8;

    [Tooltip("Allow the pool to grow when demand exceeds the current capacity.")]
    public bool allowGrow = true;

    public void Sanitize()
    {
        if (maxSize < 1) maxSize = 1;
        if (growStep < 1) growStep = 1;
        if (prewarmCount < 0) prewarmCount = 0;
    }
}
}
