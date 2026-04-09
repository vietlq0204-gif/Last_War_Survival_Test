using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;

/// <summary>
/// Basic teammate object spawned from pool and animated into formation slots.
/// </summary>
public class Teammate : ObjectSpawned, IFormationSlotSpawnReceiver
{
    private const string FlowLogPrefix = "[HomeDamageFlow][Teammate]";
    private static readonly HashSet<Teammate> ActivePickupCollectors = new HashSet<Teammate>();

    [SerializeField, Min(0f)] private float defaultMoveToSlotDuration = 0.25f;
    [SerializeField, Min(0f)] private float slotPositionTolerance = 0.01f;
    [SerializeField, Min(0f)] private float slotRotationTolerance = 0.5f;
    [SerializeField] private AnimationCurve moveToSlotCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool debugDamageLifecycleLogs = true;

    private TeammateSpawner _owningSpawner;
    private ColliderSurfaceGridAlgorithm _assignedSlotAlgorithm;
    private Renderer[] _cachedRenderers;
    private bool[] _defaultRendererStates;
    private Transform _runtimeWeaponVisualRoot;
    private GameObject _runtimeWeaponVisualInstance;
    private Renderer[] _runtimeWeaponVisualRenderers;
    private bool _hideDefaultRenderersForWeaponVisual;
    private long _assignedSlotKey;
    private Vector3 _lastResolvedSlotPosition;
    private Quaternion _lastResolvedSlotRotation;
    private bool _isQueuedForDamageDespawn;

    public bool HasAssignedFormationSlot => _assignedSlotAlgorithm != null;
    public Vector3 AssignedFormationSlotPosition => _lastResolvedSlotPosition;
    public Quaternion AssignedFormationSlotRotation => _lastResolvedSlotRotation;
    public bool CanCollectPickups => !_isQueuedForDamageDespawn && gameObject.activeInHierarchy;

    protected override void Awake()
    {
        base.Awake();
        CacheRenderers();
    }

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
        _isQueuedForDamageDespawn = false;
        ClearWeaponVisual();
        RestoreDefaultRendererStates();
        ActivePickupCollectors.Add(this);
        base.OnSpawnedFromPool();

        _owningSpawner = ResolveOwningSpawner();
        _owningSpawner?.RegisterSpawnedTeammate(this);
    }

    public override void OnDespawnedToPool()
    {
        LogFlow("OnDespawnedToPool invoked.");
        ActivePickupCollectors.Remove(this);
        _owningSpawner?.NotifyTeammateDespawned(this);
        _owningSpawner = null;
        _isQueuedForDamageDespawn = false;
        ResetAssignedFormationSlot();
        ClearWeaponVisual();
        RestoreDefaultRendererStates();
        base.OnDespawnedToPool();
    }

    protected virtual void OnDisable()
    {
        ActivePickupCollectors.Remove(this);
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

    public bool BeginQueuedDamageDespawn()
    {
        if (_isQueuedForDamageDespawn || !gameObject.activeInHierarchy)
        {
            LogFlow(
                $"BeginQueuedDamageDespawn rejected. isQueuedForDamageDespawn={_isQueuedForDamageDespawn} activeInHierarchy={gameObject.activeInHierarchy}.",
                warning: true);
            return false;
        }

        _isQueuedForDamageDespawn = true;
        ActivePickupCollectors.Remove(this);
        ReleaseGridReservation();
        SetControlledCollisionEnabled(false);
        SetRenderersVisible(false);
        LogFlow("BeginQueuedDamageDespawn accepted. Collider disabled and renderers hidden.", warning: true);
        return true;
    }

    public void ApplyWeaponVisual(WeaponSO weapon)
    {
        GameObject visualPrefab = weapon != null ? weapon.TeammateVisualPrefab : null;
        ClearWeaponVisual();

        if (visualPrefab == null)
        {
            RefreshVisualVisibility();
            return;
        }

        Transform visualRoot = ResolveRuntimeWeaponVisualRoot();
        _runtimeWeaponVisualInstance = Instantiate(visualPrefab, visualRoot, false);
        _runtimeWeaponVisualInstance.name = $"{visualPrefab.name}_RuntimeVisual";
        _runtimeWeaponVisualInstance.transform.localPosition = weapon.TeammateVisualLocalPosition;
        _runtimeWeaponVisualInstance.transform.localRotation = weapon.TeammateVisualLocalRotation;
        _runtimeWeaponVisualInstance.transform.localScale = weapon.TeammateVisualLocalScale;
        _hideDefaultRenderersForWeaponVisual = weapon.HideDefaultTeammateModelWhenEquipped;
        _runtimeWeaponVisualRenderers = _runtimeWeaponVisualInstance.GetComponentsInChildren<Renderer>(true);

        SanitizeWeaponVisualInstance(_runtimeWeaponVisualInstance);
        RefreshVisualVisibility();
    }

    public static bool TryGetClosestPickupCollector(Vector3 referencePosition, float maxDistance, out Teammate collector)
    {
        collector = null;
        float maxDistanceSqr = Mathf.Max(0.01f, maxDistance) * Mathf.Max(0.01f, maxDistance);
        float bestDistanceSqr = maxDistanceSqr;
        bool found = false;

        foreach (Teammate teammate in ActivePickupCollectors)
        {
            if (teammate == null || !teammate.CanCollectPickups)
                continue;

            Transform teammateTransform = teammate.CachedTransform != null ? teammate.CachedTransform : teammate.transform;
            float distanceSqr = (teammateTransform.position - referencePosition).sqrMagnitude;
            if (found && distanceSqr >= bestDistanceSqr)
                continue;

            if (!found && distanceSqr > maxDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            collector = teammate;
            found = true;
        }

        return found;
    }

    public static bool TryGetClosestPickupCollector(Vector3 referencePosition, out Teammate collector)
    {
        collector = null;
        float bestDistanceSqr = float.PositiveInfinity;
        bool found = false;

        foreach (Teammate teammate in ActivePickupCollectors)
        {
            if (teammate == null || !teammate.CanCollectPickups)
                continue;

            Transform teammateTransform = teammate.CachedTransform != null ? teammate.CachedTransform : teammate.transform;
            float distanceSqr = (teammateTransform.position - referencePosition).sqrMagnitude;
            if (found && distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            collector = teammate;
            found = true;
        }

        return found;
    }

    private void ResetAssignedFormationSlot()
    {
        _assignedSlotAlgorithm = null;
        _assignedSlotKey = 0L;
        _lastResolvedSlotPosition = Vector3.zero;
        _lastResolvedSlotRotation = Quaternion.identity;
    }

    private TeammateSpawner ResolveOwningSpawner()
    {
        return GetComponentInParent<TeammateSpawner>();
    }

    private void ReleaseGridReservation()
    {
        if (!TryGetComponent(out SpawnGridSlotReservation reservation))
            return;

        reservation.ReleaseReservationNow();
    }

    private void CacheRenderers()
    {
        _cachedRenderers = GetComponentsInChildren<Renderer>(true);
        _defaultRendererStates = new bool[_cachedRenderers.Length];

        for (int i = 0; i < _cachedRenderers.Length; i++)
        {
            _defaultRendererStates[i] = _cachedRenderers[i] != null && _cachedRenderers[i].enabled;
        }
    }

    private void RestoreDefaultRendererStates()
    {
        if (_cachedRenderers == null || _defaultRendererStates == null)
            return;

        for (int i = 0; i < _cachedRenderers.Length; i++)
        {
            Renderer cachedRenderer = _cachedRenderers[i];
            if (cachedRenderer == null)
                continue;

            cachedRenderer.enabled = !_hideDefaultRenderersForWeaponVisual && _defaultRendererStates[i];
        }

        SetRuntimeWeaponVisualVisible(true);
    }

    private void SetRenderersVisible(bool isVisible)
    {
        if (_cachedRenderers == null || _defaultRendererStates == null)
            return;

        for (int i = 0; i < _cachedRenderers.Length; i++)
        {
            Renderer cachedRenderer = _cachedRenderers[i];
            if (cachedRenderer == null)
                continue;

            cachedRenderer.enabled = isVisible && !_hideDefaultRenderersForWeaponVisual && _defaultRendererStates[i];
        }

        SetRuntimeWeaponVisualVisible(isVisible);
    }

    private void RefreshVisualVisibility()
    {
        bool shouldBeVisible = !_isQueuedForDamageDespawn;
        SetRenderersVisible(shouldBeVisible);
    }

    private Transform ResolveRuntimeWeaponVisualRoot()
    {
        if (_runtimeWeaponVisualRoot != null)
            return _runtimeWeaponVisualRoot;

        var visualRootObject = new GameObject("Runtime Weapon Visual Root");
        visualRootObject.transform.SetParent(transform, false);
        _runtimeWeaponVisualRoot = visualRootObject.transform;
        return _runtimeWeaponVisualRoot;
    }

    private void ClearWeaponVisual()
    {
        _hideDefaultRenderersForWeaponVisual = false;
        _runtimeWeaponVisualRenderers = null;

        if (_runtimeWeaponVisualInstance == null)
            return;

        _runtimeWeaponVisualInstance.SetActive(false);
        if (Application.isPlaying)
            Destroy(_runtimeWeaponVisualInstance);
        else
            DestroyImmediate(_runtimeWeaponVisualInstance);

        _runtimeWeaponVisualInstance = null;
    }

    private void SetRuntimeWeaponVisualVisible(bool isVisible)
    {
        if (_runtimeWeaponVisualRenderers == null)
            return;

        for (int i = 0; i < _runtimeWeaponVisualRenderers.Length; i++)
        {
            Renderer runtimeRenderer = _runtimeWeaponVisualRenderers[i];
            if (runtimeRenderer == null)
                continue;

            runtimeRenderer.enabled = isVisible;
        }
    }

    private static void SanitizeWeaponVisualInstance(GameObject visualInstance)
    {
        if (visualInstance == null)
            return;

        var pickupItem = visualInstance.GetComponent<WeaponPickupItem>();
        if (pickupItem != null)
        {
            pickupItem.enabled = false;
            if (Application.isPlaying)
                Destroy(pickupItem);
            else
                DestroyImmediate(pickupItem);
        }

        Collider[] colliders = visualInstance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        Rigidbody[] rigidbodies = visualInstance.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rigidbody = rigidbodies[i];
            if (rigidbody == null)
                continue;

            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
        }
    }

    private void LogFlow(string message, bool warning = false)
    {
        if (!debugDamageLifecycleLogs)
            return;

        string formattedMessage = $"{FlowLogPrefix}[{name}#{CachedEntityId}] {message}";
        if (warning)
            Debug.LogWarning(formattedMessage, this);
        else
            Debug.Log(formattedMessage, this);
    }
}
