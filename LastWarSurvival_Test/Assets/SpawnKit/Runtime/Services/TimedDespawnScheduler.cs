using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Pooling;

namespace Vit.SpawnKit.Services
{
/// <summary>
/// Central scheduler to avoid per-instance Update for timed despawns.
/// </summary>
internal sealed class TimedDespawnScheduler
{
    private struct Entry
    {
        public PooledObject pooled;
        public uint version;
        public float despawnAt;
        public bool useUnscaledTime;
    }

    private readonly List<Entry> _entries = new List<Entry>(128);

    public void Schedule(PooledObject pooled, uint version, float despawnAt, bool useUnscaledTime)
    {
        if (pooled == null) return;

        _entries.Add(new Entry
        {
            pooled = pooled,
            version = version,
            despawnAt = despawnAt,
            useUnscaledTime = useUnscaledTime
        });
    }

    public void Tick()
    {
        if (_entries.Count == 0) return;

        float scaledNow = Time.time;
        float unscaledNow = Time.unscaledTime;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            var pooled = entry.pooled;

            if (pooled == null || !pooled.IsSpawned || pooled.ScheduleVersion != entry.version)
            {
                RemoveAtSwapBack(i);
                continue;
            }

            float now = entry.useUnscaledTime ? unscaledNow : scaledNow;
            if (now < entry.despawnAt) continue;

            RemoveAtSwapBack(i);
            pooled.ReturnToPool();
        }
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private void RemoveAtSwapBack(int index)
    {
        int lastIndex = _entries.Count - 1;
        _entries[index] = _entries[lastIndex];
        _entries.RemoveAt(lastIndex);
    }
}
}
