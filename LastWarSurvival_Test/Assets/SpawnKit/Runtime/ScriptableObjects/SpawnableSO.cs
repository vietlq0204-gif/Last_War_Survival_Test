using System;
using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Data;

namespace Vit.SpawnKit.ScriptableObjects
{
/// <summary>
/// Data that defines a spawnable object set.
/// </summary>
[CreateAssetMenu(menuName = "SpawnKit/Spawnable", fileName = "_Spawnable")]
public class SpawnableSO : ScriptableObject
{
    [Header("Key")]
    [Tooltip("Stable logical identifier used for lookup and fallback.")]
    public SpawnKey key;

    [Header("Source")]
    [Tooltip("Instance source. Prefab is the default; Addressables requires project support.")]
    public SpawnSourceType sourceType = SpawnSourceType.Prefab;

    [Tooltip("Prefab entries that can be spawned. If all entry counts are 0, weight-based random will be used.")]
    public List<PrefabVariant> prefabVariants = new List<PrefabVariant>();

    [Tooltip("Addressable entries that can be spawned. If all entry counts are 0, weight-based random will be used.")]
    public List<AddressableVariant> addressVariants = new List<AddressableVariant>();

    [Header("Pool")]
    [Tooltip("Default pool config for this spawnable.")]
    public PoolConfig poolConfig = new PoolConfig();

    [Header("Lifecycle")]
    [Tooltip("Default lifecycle used unless preset or request overrides it.")]
    public SpawnLifecycle defaultLifecycle = new SpawnLifecycle
    {
        mode = SpawnReleaseMode.Manual,
        delaySeconds = 0f,
        useUnscaledTime = false
    };

    [Header("Random")]
    [Tooltip("How seed = 0 is resolved when the request does not provide one.")]
    public SeedMode defaultSeedMode = SeedMode.SessionStable;

    private void OnValidate()
    {
        key.RecomputeHash();
        poolConfig?.Sanitize();
        defaultLifecycle.Sanitize();
        SanitizeVariants();
    }

    public int GetConfiguredObjectCount()
    {
        int total = 0;

        switch (sourceType)
        {
            case SpawnSourceType.Prefab:
                if (prefabVariants == null) return 0;
                for (int i = 0; i < prefabVariants.Count; i++)
                {
                    total += Mathf.Max(0, prefabVariants[i].count);
                }
                return total;

            case SpawnSourceType.Addressables:
#if USE_ADDRESSABLES
                if (addressVariants == null) return 0;
                for (int i = 0; i < addressVariants.Count; i++)
                {
                    total += Mathf.Max(0, addressVariants[i].count);
                }
#endif
                return total;

            default:
                return 0;
        }
    }

    public int ResolveSpawnCount(int maxCount)
    {
        int configuredTotal = GetConfiguredObjectCount();
        if (configuredTotal > 0)
        {
            if (maxCount < 0 || maxCount > configuredTotal) return configuredTotal;
            return Mathf.Max(0, maxCount);
        }

        return maxCount > 0 ? maxCount : 0;
    }

    public int[] BuildVariantPlan(int maxCount, int repeatCount = 1)
    {
        if (repeatCount <= 0) return Array.Empty<int>();

        int configuredTotal = GetConfiguredObjectCount();
        if (configuredTotal <= 0) return null;

        int perCycleCount = ResolveSpawnCount(maxCount);
        if (perCycleCount <= 0) return Array.Empty<int>();

        int[] cyclePlan = BuildSingleCyclePlan(perCycleCount);
        if (repeatCount == 1) return cyclePlan;

        var result = new int[cyclePlan.Length * repeatCount];
        for (int i = 0; i < repeatCount; i++)
        {
            Array.Copy(cyclePlan, 0, result, i * cyclePlan.Length, cyclePlan.Length);
        }

        return result;
    }

    private int[] BuildSingleCyclePlan(int count)
    {
        var plan = new int[count];
        int write = 0;

        switch (sourceType)
        {
            case SpawnSourceType.Prefab:
                if (prefabVariants == null) return Array.Empty<int>();
                for (int i = 0; i < prefabVariants.Count && write < count; i++)
                {
                    int itemCount = Mathf.Max(0, prefabVariants[i].count);
                    for (int copy = 0; copy < itemCount && write < count; copy++)
                    {
                        plan[write++] = i;
                    }
                }
                break;

            case SpawnSourceType.Addressables:
#if USE_ADDRESSABLES
                if (addressVariants == null) return Array.Empty<int>();
                for (int i = 0; i < addressVariants.Count && write < count; i++)
                {
                    int itemCount = Mathf.Max(0, addressVariants[i].count);
                    for (int copy = 0; copy < itemCount && write < count; copy++)
                    {
                        plan[write++] = i;
                    }
                }
#endif
                break;
        }

        if (write == plan.Length) return plan;
        Array.Resize(ref plan, write);
        return plan;
    }

    private void SanitizeVariants()
    {
        if (prefabVariants != null)
        {
            for (int i = 0; i < prefabVariants.Count; i++)
            {
                var variant = prefabVariants[i];
                variant.count = Mathf.Max(0, variant.count);
                variant.weight = Mathf.Max(0f, variant.weight);
                prefabVariants[i] = variant;
            }
        }

        if (addressVariants != null)
        {
            for (int i = 0; i < addressVariants.Count; i++)
            {
                var variant = addressVariants[i];
                variant.count = Mathf.Max(0, variant.count);
                variant.weight = Mathf.Max(0f, variant.weight);
                addressVariants[i] = variant;
            }
        }
    }

    [Serializable]
    public struct PrefabVariant
    {
        [Tooltip("Prefab instantiated and managed by the pool.")]
        public GameObject prefab;
        [Tooltip("How many copies of this prefab should be spawned before moving to the next entry. Set all counts to 0 to use weight-based random.")]
        [Min(0)] public int count;
        [Tooltip("Random weight used only when total configured count is 0.")]
        [Min(0f)] public float weight;
    }

    [Serializable]
    public struct AddressableVariant
    {
        [Tooltip("Address key loaded when sourceType = Addressables.")]
        public string addressKey;
        [Tooltip("How many copies of this entry should be spawned before moving to the next entry. Set all counts to 0 to use weight-based random.")]
        [Min(0)] public int count;
        [Tooltip("Random weight used only when total configured count is 0.")]
        [Min(0f)] public float weight;
    }
}
}
