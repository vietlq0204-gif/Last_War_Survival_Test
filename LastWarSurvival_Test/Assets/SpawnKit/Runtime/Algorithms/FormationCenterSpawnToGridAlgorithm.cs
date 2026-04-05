using System.Collections.Generic;
using UnityEngine;

namespace Vit.SpawnKit.Algorithms
{
public enum FormationSpawnInitialPoseMode
{
    CenterSlot = 1,
    OwnSlot = 0,
    RandomEmptySlot = 2,
    FixedTransform = 3,
    AuxiliaryColliderScatter = 4,
    NearestEmptySlot = 5,
}

/// <summary>
/// Spawns objects using a configurable initial pose, then binds them to reserved grid slots.
/// </summary>
public sealed class FormationSpawnToGridAlgorithm : ITrySpawnAlgorithm, ISpawnBatchReset, ISpawnResultCallback
{
    private readonly ColliderSurfaceGridAlgorithm _slotAlgorithm;
    private readonly int _spawnCount;
    private readonly FormationSpawnInitialPoseMode _initialPoseMode;
    private readonly ISpawnAlgorithm _initialPoseAlgorithm;
    private readonly Vector3 _fallbackSpawnPosition;
    private readonly Quaternion _fallbackSpawnRotation;

    private readonly Dictionary<int, ColliderSurfaceGridPlacement> _targetPlacements =
        new Dictionary<int, ColliderSurfaceGridPlacement>(32);

    private readonly Dictionary<int, ColliderSurfaceGridPlacement> _randomStartPlacements =
        new Dictionary<int, ColliderSurfaceGridPlacement>(32);

    private readonly HashSet<long> _targetPlacementKeys = new HashSet<long>();
    private readonly HashSet<long> _randomStartExcludedKeys = new HashSet<long>();

    private bool _isInitialized;

    public FormationSpawnToGridAlgorithm(
        ColliderSurfaceGridAlgorithm slotAlgorithm,
        int spawnCount,
        FormationSpawnInitialPoseMode initialPoseMode,
        ISpawnAlgorithm initialPoseAlgorithm,
        Vector3 fallbackSpawnPosition,
        Quaternion fallbackSpawnRotation)
    {
        _slotAlgorithm = slotAlgorithm;
        _spawnCount = Mathf.Max(0, spawnCount);
        _initialPoseMode = initialPoseMode;
        _initialPoseAlgorithm = initialPoseAlgorithm;
        _fallbackSpawnPosition = fallbackSpawnPosition;
        _fallbackSpawnRotation = fallbackSpawnRotation;
    }

    public void ResetPlaced()
    {
        _isInitialized = false;
        _targetPlacements.Clear();
        _randomStartPlacements.Clear();
        _targetPlacementKeys.Clear();
        _randomStartExcludedKeys.Clear();

        _slotAlgorithm?.ResetPlaced();

        if (_initialPoseAlgorithm is ISpawnBatchReset initialPoseReset)
            initialPoseReset.ResetPlaced();
    }

    public bool TryGetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
    {
        position = _fallbackSpawnPosition;
        rotation = _fallbackSpawnRotation;

        if (!EnsureInitialized())
            return false;

        if (!_targetPlacements.TryGetValue(index, out var targetPlacement))
            return false;

        switch (_initialPoseMode)
        {
            case FormationSpawnInitialPoseMode.OwnSlot:
                position = targetPlacement.Position;
                rotation = targetPlacement.Rotation;
                return true;

            case FormationSpawnInitialPoseMode.RandomEmptySlot:
                return TryGetRandomStartPose(index, seed, targetPlacement, out position, out rotation);

            case FormationSpawnInitialPoseMode.NearestEmptySlot:
                return TryGetNearestStartPose(index, targetPlacement, out position, out rotation);

            case FormationSpawnInitialPoseMode.FixedTransform:
            case FormationSpawnInitialPoseMode.AuxiliaryColliderScatter:
                if (TryGetCustomStartPose(index, seed, out position, out rotation))
                    return true;

                position = targetPlacement.Position;
                rotation = targetPlacement.Rotation;
                return true;

            case FormationSpawnInitialPoseMode.CenterSlot:
            default:
                if (_slotAlgorithm != null && _slotAlgorithm.TryGetClosestPlacement(out var centerPlacement))
                {
                    position = centerPlacement.Position;
                    rotation = centerPlacement.Rotation;
                    return true;
                }

                position = _fallbackSpawnPosition;
                rotation = _fallbackSpawnRotation;
                return true;
        }
    }

    public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
    {
        if (!TryGetPose(index, seed, out position, out rotation))
        {
            position = _fallbackSpawnPosition;
            rotation = _fallbackSpawnRotation;
        }
    }

    public void OnSpawnSucceeded(int index, GameObject instance)
    {
        if (!_targetPlacements.TryGetValue(index, out var placement))
            return;

        _targetPlacements.Remove(index);
        _randomStartPlacements.Remove(index);

        if (instance == null)
        {
            _slotAlgorithm?.ReleaseReservation(placement.Key);
            return;
        }

        _slotAlgorithm?.BindReservation(instance, in placement);

        var receiver = instance.GetComponent<IFormationSlotSpawnReceiver>();
        if (receiver != null)
        {
            receiver.AssignFormationSlot(_slotAlgorithm, placement.Key);
            return;
        }

        if (_slotAlgorithm != null && _slotAlgorithm.TryGetPlacementPose(placement.Key, out var slotPosition, out var slotRotation))
        {
            instance.transform.SetPositionAndRotation(slotPosition, slotRotation);
            return;
        }

        instance.transform.SetPositionAndRotation(placement.Position, placement.Rotation);
    }

    public void OnSpawnFailed(int index)
    {
        if (_targetPlacements.TryGetValue(index, out var placement))
            _slotAlgorithm?.ReleaseReservation(placement.Key);

        _targetPlacements.Remove(index);
        _randomStartPlacements.Remove(index);
    }

    private bool EnsureInitialized()
    {
        if (_isInitialized)
            return true;

        if (_slotAlgorithm == null || _spawnCount <= 0)
            return false;

        _targetPlacements.Clear();
        _randomStartPlacements.Clear();
        _targetPlacementKeys.Clear();
        _randomStartExcludedKeys.Clear();

        for (int i = 0; i < _spawnCount; i++)
        {
            if (!_slotAlgorithm.TryReserveNextPlacement(out var targetPlacement))
                return false;

            _targetPlacements[i] = targetPlacement;
            _targetPlacementKeys.Add(targetPlacement.Key);
        }

        _randomStartExcludedKeys.UnionWith(_targetPlacementKeys);
        _isInitialized = true;
        return true;
    }

    private bool TryGetRandomStartPose(
        int index,
        uint seed,
        in ColliderSurfaceGridPlacement targetPlacement,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = targetPlacement.Position;
        rotation = targetPlacement.Rotation;

        if (_randomStartPlacements.TryGetValue(index, out var randomPlacement))
        {
            position = randomPlacement.Position;
            rotation = randomPlacement.Rotation;
            return true;
        }

        if (_slotAlgorithm == null)
            return false;

        if (!_slotAlgorithm.TryGetRandomAvailablePlacement(
                seed,
                (uint)(index + 1),
                _randomStartExcludedKeys,
                out randomPlacement))
            return false;

        _randomStartPlacements[index] = randomPlacement;
        _randomStartExcludedKeys.Add(randomPlacement.Key);
        position = randomPlacement.Position;
        rotation = randomPlacement.Rotation;
        return true;
    }

    private bool TryGetNearestStartPose(
        int index,
        in ColliderSurfaceGridPlacement targetPlacement,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = targetPlacement.Position;
        rotation = targetPlacement.Rotation;

        if (_randomStartPlacements.TryGetValue(index, out var nearestPlacement))
        {
            position = nearestPlacement.Position;
            rotation = nearestPlacement.Rotation;
            return true;
        }

        if (_slotAlgorithm == null)
            return false;

        if (!_slotAlgorithm.TryGetNearestAvailablePlacement(
                targetPlacement.Position,
                _randomStartExcludedKeys,
                out nearestPlacement))
            return false;

        _randomStartPlacements[index] = nearestPlacement;
        _randomStartExcludedKeys.Add(nearestPlacement.Key);
        position = nearestPlacement.Position;
        rotation = nearestPlacement.Rotation;
        return true;
    }

    private bool TryGetCustomStartPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
    {
        position = _fallbackSpawnPosition;
        rotation = _fallbackSpawnRotation;

        if (_initialPoseAlgorithm == null)
            return false;

        if (_initialPoseAlgorithm is ITrySpawnAlgorithm tryInitialPose)
            return tryInitialPose.TryGetPose(index, seed, out position, out rotation);

        _initialPoseAlgorithm.GetPose(index, seed, out position, out rotation);
        return true;
    }
}

public interface IFormationSlotSpawnReceiver
{
    void AssignFormationSlot(ColliderSurfaceGridAlgorithm slotAlgorithm, long slotKey);
}
}
