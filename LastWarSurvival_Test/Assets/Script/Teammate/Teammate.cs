using UnityEngine;
using Vit.SpawnKit.Algorithms;

/// <summary>
/// Basic teammate object spawned from pool and animated into formation slots.
/// </summary>
public class Teammate : ObjectSpawned, IFormationSlotSpawnReceiver
{
    [SerializeField, Min(0f)] private float defaultMoveToSlotDuration = 0.25f;
    [SerializeField, Min(0f)] private float slotPositionTolerance = 0.01f;
    [SerializeField, Min(0f)] private float slotRotationTolerance = 0.5f;
    [SerializeField] private AnimationCurve moveToSlotCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private ColliderSurfaceGridAlgorithm _assignedSlotAlgorithm;
    private long _assignedSlotKey;
    private Vector3 _lastResolvedSlotPosition;
    private Quaternion _lastResolvedSlotRotation;

    public bool HasAssignedFormationSlot => _assignedSlotAlgorithm != null;
    public Vector3 AssignedFormationSlotPosition => _lastResolvedSlotPosition;
    public Quaternion AssignedFormationSlotRotation => _lastResolvedSlotRotation;

    protected virtual void Update()
    {
        if (_assignedSlotAlgorithm == null)
            return;

        if (!_assignedSlotAlgorithm.TryGetPlacementPose(_assignedSlotKey, out var slotPosition, out var slotRotation))
            return;

        _lastResolvedSlotPosition = slotPosition;
        _lastResolvedSlotRotation = slotRotation;

        var targetTransform = CachedTransform != null ? CachedTransform : transform;
        Vector3 currentPosition = targetTransform.position;
        Quaternion currentRotation = targetTransform.rotation;

        float positionDeltaSqr = (slotPosition - currentPosition).sqrMagnitude;
        float positionToleranceSqr = slotPositionTolerance * slotPositionTolerance;
        float rotationDelta = Quaternion.Angle(currentRotation, slotRotation);

        if (positionDeltaSqr <= positionToleranceSqr && rotationDelta <= slotRotationTolerance)
            return;

        if (defaultMoveToSlotDuration <= 0f)
        {
            targetTransform.SetPositionAndRotation(slotPosition, slotRotation);
            return;
        }

        float normalizedStep = Mathf.Clamp01(Time.deltaTime / defaultMoveToSlotDuration);
        float easedStep = moveToSlotCurve != null
            ? moveToSlotCurve.Evaluate(normalizedStep)
            : normalizedStep;

        targetTransform.SetPositionAndRotation(
            Vector3.LerpUnclamped(currentPosition, slotPosition, easedStep),
            Quaternion.SlerpUnclamped(currentRotation, slotRotation, easedStep));
    }

    public override void OnSpawnedFromPool()
    {
        ResetAssignedFormationSlot();
        base.OnSpawnedFromPool();
    }

    public override void OnDespawnedToPool()
    {
        ResetAssignedFormationSlot();
        base.OnDespawnedToPool();
    }

    public void AssignFormationSlot(ColliderSurfaceGridAlgorithm slotAlgorithm, long slotKey)
    {
        _assignedSlotAlgorithm = slotAlgorithm;
        _assignedSlotKey = slotKey;

        if (_assignedSlotAlgorithm == null)
            return;

        if (!_assignedSlotAlgorithm.TryGetPlacementPose(_assignedSlotKey, out _lastResolvedSlotPosition, out _lastResolvedSlotRotation))
            return;

        if (defaultMoveToSlotDuration > 0f)
            return;

        var targetTransform = CachedTransform != null ? CachedTransform : transform;
        targetTransform.SetPositionAndRotation(_lastResolvedSlotPosition, _lastResolvedSlotRotation);
    }

    private void ResetAssignedFormationSlot()
    {
        _assignedSlotAlgorithm = null;
        _assignedSlotKey = 0L;
        _lastResolvedSlotPosition = Vector3.zero;
        _lastResolvedSlotRotation = Quaternion.identity;
    }
}
