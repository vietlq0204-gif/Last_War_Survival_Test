using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Data;

namespace Vit.SpawnKit.ScriptableObjects
{
public enum MultiVolumeCountMode
{
    SharedAcrossVolumes = 0,
    CountPerVolume = 1,
}

[CreateAssetMenu(menuName = "SpawnKit/Spawn Preset", fileName = "_SpawnPreset")]
public class SpawnPresetSO : ScriptableObject
{
    [Header("Target")]
    [FormerlySerializedAs("spawnable")]
    [SerializeField, HideInInspector] private SpawnableSO legacySpawnable;
    [Tooltip("Spawnables used when gameplay calls SpawnKit with this preset. The legacy single spawnable is migrated into this list automatically.")]
    public SpawnableSO[] spawnables;

    [Header("Batch")]
    [Tooltip("Maximum number of objects spawned for one volume. -1 means no cap when any SpawnableSO uses explicit counts.")]
    [Min(-1)] public int MaxCount = -1;
    [Tooltip("Fixed seed for this preset. Set 0 to auto-resolve from SpawnableSO.defaultSeedMode.")]
    [Min(0)] public uint seed = 0;
    [Tooltip("When multiple volumes are passed: SharedAcrossVolumes uses one combined cap; CountPerVolume applies MaxCount to each volume.")]
    public MultiVolumeCountMode multiVolumeCountMode = MultiVolumeCountMode.SharedAcrossVolumes;

    [Header("Lifecycle")]
    [Tooltip("Enable if this preset should override the SpawnableSO default lifecycle.")]
    public bool overrideLifecycle;
    [Tooltip("Lifecycle used when overrideLifecycle is enabled.")]
    public SpawnLifecycle lifecycle = SpawnLifecycle.Manual;

    [Header("Volume Algorithm")]
    [Tooltip("Maximum tries used to find a valid point for each object when a volume is provided.")]
    [Min(1)] public int maxTryPerPoint = 24;
    [Tooltip("Candidates scored per try. Higher values improve spacing quality but cost more CPU.")]
    [Min(1)] public int candidatesPerPoint = 16;
    [Tooltip("Desired minimum distance between spawned objects in the same batch.")]
    [Min(0f)] public float minDistance = 0.5f;
    [Tooltip("Internal placement buffer size used by volume algorithms.")]
    [Min(1)] public int placementBufferCapacity = 256;

    public SpawnableSO spawnable => GetPrimarySpawnable();
    public bool HasSpawnables => CountResolvedSpawnables() > 0;

    private void OnValidate()
    {
        if (spawnables == null)
            spawnables = Array.Empty<SpawnableSO>();

        MigrateLegacySpawnable();

        if (MaxCount < -1) MaxCount = -1;
        if (maxTryPerPoint < 1) maxTryPerPoint = 1;
        if (candidatesPerPoint < 1) candidatesPerPoint = 1;
        if (placementBufferCapacity < 1) placementBufferCapacity = 1;
        if (minDistance < 0f) minDistance = 0f;
        lifecycle.Sanitize();
    }

    public SpawnRequest CreateRequest(Collider volume, Transform parent = null)
    {
        return CreateRequest(volume != null ? new[] { volume } : null, parent);
    }

    public SpawnRequest CreateRequest(Collider[] volumes, Transform parent = null)
    {
        int validVolumeCount = CountValidVolumes(volumes);
        Transform resolvedParent = parent != null
            ? parent
            : FindFirstVolumeTransform(volumes);

        int perVolumeCount = ResolveSpawnCount(MaxCount);
        int repeatCount = validVolumeCount > 1 && multiVolumeCountMode == MultiVolumeCountMode.CountPerVolume
            ? validVolumeCount
            : 1;

        ISpawnAlgorithm algorithm;
        if (validVolumeCount > 0)
        {
            algorithm = CreateVolumeAlgorithm(volumes, validVolumeCount, perVolumeCount);
        }
        else
        {
            Vector3 position = resolvedParent != null ? resolvedParent.position : Vector3.zero;
            Quaternion rotation = resolvedParent != null ? resolvedParent.rotation : Quaternion.identity;
            algorithm = new FixedPoseAlgorithm(position, rotation);
        }

        return CreateRequestInternal(MaxCount, repeatCount, resolvedParent, algorithm);
    }

    public SpawnRequest CreateRequest(Transform parent = null)
    {
        return CreateRequest((Collider[])null, parent);
    }

    public SpawnRequest CreateRequest(int maxCount, ISpawnAlgorithm algorithm, Transform parent = null)
    {
        return CreateRequestInternal(maxCount, 1, parent, algorithm);
    }

    public SpawnableSO GetPrimarySpawnable()
    {
        if (legacySpawnable != null)
            return legacySpawnable;

        if (spawnables == null) return null;

        for (int i = 0; i < spawnables.Length; i++)
        {
            if (spawnables[i] != null)
                return spawnables[i];
        }

        return null;
    }

    public int GetSpawnables(List<SpawnableSO> results)
    {
        if (results == null) return 0;

        results.Clear();
        TryAddSpawnable(results, legacySpawnable);

        if (spawnables == null) return results.Count;

        for (int i = 0; i < spawnables.Length; i++)
        {
            TryAddSpawnable(results, spawnables[i]);
        }

        return results.Count;
    }

    public int ResolveSpawnCount()
    {
        return ResolveSpawnCount(MaxCount);
    }

    public int ResolveSpawnCount(int maxCount)
    {
        var resolvedSpawnables = new List<SpawnableSO>(spawnables != null ? Mathf.Max(1, spawnables.Length) : 1);
        GetSpawnables(resolvedSpawnables);
        return ResolveSpawnCount(resolvedSpawnables, maxCount);
    }

    private SpawnRequest CreateRequestInternal(int maxCountPerCycle, int repeatCount, Transform parent, ISpawnAlgorithm algorithm)
    {
        var resolvedSpawnables = new List<SpawnableSO>(spawnables != null ? Mathf.Max(1, spawnables.Length) : 1);
        GetSpawnables(resolvedSpawnables);

        int safeRepeatCount = Mathf.Max(1, repeatCount);
        int perCycleCount = ResolveSpawnCount(resolvedSpawnables, maxCountPerCycle);
        int totalSpawnCount = MultiplyClamped(perCycleCount, safeRepeatCount);
        SpawnLifecycle? lifecycleOverride = overrideLifecycle ? lifecycle : (SpawnLifecycle?)null;

        if (resolvedSpawnables.Count <= 0)
        {
            return new SpawnRequest(
                (SpawnableSO)null,
                totalSpawnCount,
                parent,
                algorithm,
                seed,
                lifecycleOverride);
        }

        if (resolvedSpawnables.Count == 1)
        {
            var singleSpawnable = resolvedSpawnables[0];
            int[] variantPlan = singleSpawnable != null
                ? singleSpawnable.BuildVariantPlan(maxCountPerCycle, safeRepeatCount)
                : null;

            return new SpawnRequest(
                singleSpawnable,
                totalSpawnCount,
                parent,
                algorithm,
                seed,
                lifecycleOverride,
                variantPlan);
        }

        BuildMultiSpawnPlan(
            resolvedSpawnables,
            perCycleCount,
            safeRepeatCount,
            out var resolvedSpawnableArray,
            out var spawnablePlan,
            out var variantPlanForPreset);

        return new SpawnRequest(
            resolvedSpawnableArray,
            totalSpawnCount,
            parent,
            algorithm,
            seed,
            lifecycleOverride,
            spawnablePlan,
            variantPlanForPreset);
    }

    private int ResolveSpawnCount(IReadOnlyList<SpawnableSO> resolvedSpawnables, int maxCount)
    {
        int configuredTotal = GetConfiguredSpawnCount(resolvedSpawnables);
        if (configuredTotal > 0)
        {
            if (maxCount < 0 || maxCount > configuredTotal) return configuredTotal;
            return Mathf.Max(0, maxCount);
        }

        return maxCount > 0 ? maxCount : 0;
    }

    private static int GetConfiguredSpawnCount(IReadOnlyList<SpawnableSO> resolvedSpawnables)
    {
        if (resolvedSpawnables == null) return 0;

        long configuredTotal = 0;
        for (int i = 0; i < resolvedSpawnables.Count; i++)
        {
            var currentSpawnable = resolvedSpawnables[i];
            if (currentSpawnable == null) continue;

            configuredTotal += Mathf.Max(0, currentSpawnable.GetConfiguredObjectCount());
        }

        return configuredTotal > int.MaxValue ? int.MaxValue : (int)configuredTotal;
    }

    private void BuildMultiSpawnPlan(
        List<SpawnableSO> resolvedSpawnables,
        int perCycleCount,
        int repeatCount,
        out SpawnableSO[] resolvedSpawnableArray,
        out int[] spawnablePlan,
        out int[] variantPlan)
    {
        resolvedSpawnableArray = resolvedSpawnables.ToArray();
        if (perCycleCount <= 0 || resolvedSpawnableArray.Length == 0)
        {
            spawnablePlan = Array.Empty<int>();
            variantPlan = Array.Empty<int>();
            return;
        }

        int configuredTotal = GetConfiguredSpawnCount(resolvedSpawnables);
        int[] cycleSpawnablePlan;
        int[] cycleVariantPlan = null;

        if (configuredTotal > 0)
        {
            cycleSpawnablePlan = new int[perCycleCount];
            cycleVariantPlan = new int[perCycleCount];

            int write = 0;
            for (int spawnableIndex = 0; spawnableIndex < resolvedSpawnableArray.Length && write < perCycleCount; spawnableIndex++)
            {
                var currentSpawnable = resolvedSpawnableArray[spawnableIndex];
                if (currentSpawnable == null) continue;

                int configuredCount = Mathf.Max(0, currentSpawnable.GetConfiguredObjectCount());
                if (configuredCount <= 0) continue;

                int[] currentVariantPlan = currentSpawnable.BuildVariantPlan(configuredCount);
                for (int itemIndex = 0; itemIndex < configuredCount && write < perCycleCount; itemIndex++)
                {
                    cycleSpawnablePlan[write] = spawnableIndex;
                    cycleVariantPlan[write] = currentVariantPlan != null && itemIndex < currentVariantPlan.Length
                        ? currentVariantPlan[itemIndex]
                        : 0;
                    write++;
                }
            }
        }
        else
        {
            cycleSpawnablePlan = new int[perCycleCount];
            int startIndex = resolvedSpawnableArray.Length > 0
                ? (int)(seed % (uint)resolvedSpawnableArray.Length)
                : 0;

            for (int i = 0; i < perCycleCount; i++)
            {
                cycleSpawnablePlan[i] = (startIndex + i) % resolvedSpawnableArray.Length;
            }
        }

        spawnablePlan = RepeatPlan(cycleSpawnablePlan, repeatCount);
        variantPlan = cycleVariantPlan != null
            ? RepeatPlan(cycleVariantPlan, repeatCount)
            : null;
    }

    private ISpawnAlgorithm CreateVolumeAlgorithm(Collider[] volumes, int validCount, int perVolumeCount)
    {
        Collider first = null;
        for (int i = 0; i < volumes.Length; i++)
        {
            if (volumes[i] == null) continue;
            first = volumes[i];
            break;
        }

        int capacity = Mathf.Max(placementBufferCapacity, Mathf.Max(1, perVolumeCount));
        if (validCount <= 1)
        {
            return new ColliderVolumeAlgorithm(first, maxTryPerPoint, candidatesPerPoint, minDistance, capacity);
        }

        if (multiVolumeCountMode == MultiVolumeCountMode.CountPerVolume)
        {
            return new PerVolumeColliderVolumeAlgorithm(volumes, perVolumeCount, maxTryPerPoint, candidatesPerPoint, minDistance, capacity);
        }

        return new MultiColliderVolumeAlgorithm(volumes, maxTryPerPoint, candidatesPerPoint, minDistance, capacity);
    }

    private static int[] RepeatPlan(int[] cyclePlan, int repeatCount)
    {
        if (cyclePlan == null) return null;
        if (repeatCount <= 1) return cyclePlan;

        var result = new int[cyclePlan.Length * repeatCount];
        for (int i = 0; i < repeatCount; i++)
        {
            Array.Copy(cyclePlan, 0, result, i * cyclePlan.Length, cyclePlan.Length);
        }

        return result;
    }

    private static int MultiplyClamped(int value, int multiplier)
    {
        if (value <= 0 || multiplier <= 0) return 0;

        long total = (long)value * multiplier;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private static int CountValidVolumes(Collider[] volumes)
    {
        if (volumes == null) return 0;

        int validCount = 0;
        for (int i = 0; i < volumes.Length; i++)
        {
            if (volumes[i] != null) validCount++;
        }

        return validCount;
    }

    private static Transform FindFirstVolumeTransform(Collider[] volumes)
    {
        if (volumes == null) return null;

        for (int i = 0; i < volumes.Length; i++)
        {
            if (volumes[i] != null) return volumes[i].transform;
        }

        return null;
    }

    private int CountResolvedSpawnables()
    {
        int count = 0;
        if (legacySpawnable != null)
            count++;

        if (spawnables == null) return count;

        for (int i = 0; i < spawnables.Length; i++)
        {
            var currentSpawnable = spawnables[i];
            if (currentSpawnable == null) continue;
            if (currentSpawnable == legacySpawnable) continue;
            count++;
        }

        return count;
    }

    private void MigrateLegacySpawnable()
    {
        if (legacySpawnable == null)
            return;

        if (spawnables == null)
            spawnables = Array.Empty<SpawnableSO>();

        if (Array.IndexOf(spawnables, legacySpawnable) < 0)
        {
            var migratedSpawnables = new SpawnableSO[spawnables.Length + 1];
            migratedSpawnables[0] = legacySpawnable;
            Array.Copy(spawnables, 0, migratedSpawnables, 1, spawnables.Length);
            spawnables = migratedSpawnables;
        }

        legacySpawnable = null;
    }

    private static void TryAddSpawnable(List<SpawnableSO> results, SpawnableSO candidate)
    {
        if (candidate == null) return;
        if (results.Contains(candidate)) return;
        results.Add(candidate);
    }
}
}
