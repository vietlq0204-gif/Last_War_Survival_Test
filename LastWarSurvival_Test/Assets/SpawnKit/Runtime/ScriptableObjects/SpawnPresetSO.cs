using UnityEngine;
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
    [Tooltip("Spawnable used when gameplay calls SpawnKit with this preset.")]
    public SpawnableSO spawnable;

    [Header("Batch")]
    [Tooltip("Maximum number of objects spawned for one volume. -1 means no cap when SpawnableSO uses explicit counts.")]
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

    private void OnValidate()
    {
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

        int perVolumeCount = ResolvePerVolumeSpawnCount();
        int repeatCount = validVolumeCount > 1 && multiVolumeCountMode == MultiVolumeCountMode.CountPerVolume
            ? validVolumeCount
            : 1;
        int totalSpawnCount = MultiplyClamped(perVolumeCount, repeatCount);
        int[] variantPlan = spawnable != null ? spawnable.BuildVariantPlan(MaxCount, repeatCount) : null;

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

        return new SpawnRequest(
            spawnable,
            totalSpawnCount,
            resolvedParent,
            algorithm,
            seed,
            overrideLifecycle ? lifecycle : (SpawnLifecycle?)null,
            variantPlan);
    }

    public SpawnRequest CreateRequest(Transform parent = null)
    {
        return CreateRequest((Collider[])null, parent);
    }

    private int ResolvePerVolumeSpawnCount()
    {
        if (spawnable == null)
            return MaxCount > 0 ? MaxCount : 0;

        return spawnable.ResolveSpawnCount(MaxCount);
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
}
}
