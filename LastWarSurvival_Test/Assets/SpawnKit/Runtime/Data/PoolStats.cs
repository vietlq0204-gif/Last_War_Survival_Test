using System;

namespace Vit.SpawnKit.Data
{
/// <summary>
/// Runtime pool stats aggregated per spawnable.
/// </summary>
[Serializable]
public struct PoolStats
{
    public int active;
    public int inactive;
    public int total;
    public int max;

    public PoolStats(int active, int inactive, int total, int max)
    {
        this.active = active;
        this.inactive = inactive;
        this.total = total;
        this.max = max;
    }
}
}
