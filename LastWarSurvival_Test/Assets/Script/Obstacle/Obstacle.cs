using UnityEngine;
using Vit.SpawnKit.Api;

public sealed class Obstacle : ObjectSpawned
{
    [Header("Data")]
    [SerializeField] private ObstacleSO obstacleData;

    [Header("Slot Motion")]
    [SerializeField, Min(0f)] private float moveToSlotDuration = 0.2f;
    [SerializeField, Min(0f)] private float slotPositionTolerance = 0.01f;
    [SerializeField, Min(0f)] private float slotRotationTolerance = 0.5f;
    [SerializeField] private AnimationCurve moveToSlotCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private ObstacleSpawner _owningSpawner;
    private Transform _assignedSlot;
    private Renderer[] _cachedRenderers;
    private bool[] _defaultRendererStates;
    private int _currentHealth;
    private int _slotIndex = -1;
    private bool _isDespawning;

    public ObstacleSO Data => obstacleData;
    public int CurrentHealth => _currentHealth;
    public int SlotIndex => _slotIndex;

    protected override void Awake()
    {
        base.Awake();
        CacheRenderers();
    }

    private void Update()
    {
        if (_assignedSlot == null)
            return;

        Transform targetTransform = CachedTransform != null ? CachedTransform : transform;
        Vector3 targetPosition = _assignedSlot.position;
        Quaternion targetRotation = _assignedSlot.rotation;

        float positionToleranceSqr = slotPositionTolerance * slotPositionTolerance;
        float positionDeltaSqr = (targetPosition - targetTransform.position).sqrMagnitude;
        float rotationDelta = Quaternion.Angle(targetTransform.rotation, targetRotation);

        if (positionDeltaSqr <= positionToleranceSqr && rotationDelta <= slotRotationTolerance)
            return;

        if (moveToSlotDuration <= 0f)
        {
            targetTransform.SetPositionAndRotation(targetPosition, targetRotation);
            return;
        }

        float normalizedStep = Mathf.Clamp01(Time.deltaTime / moveToSlotDuration);
        float easedStep = moveToSlotCurve != null
            ? moveToSlotCurve.Evaluate(normalizedStep)
            : normalizedStep;

        targetTransform.SetPositionAndRotation(
            Vector3.LerpUnclamped(targetTransform.position, targetPosition, easedStep),
            Quaternion.SlerpUnclamped(targetTransform.rotation, targetRotation, easedStep));
    }

    public override void OnSpawnedFromPool()
    {
        _isDespawning = false;
        _currentHealth = ResolveStartingHealth();
        RestoreDefaultRendererStates();
        base.OnSpawnedFromPool();
    }

    public override void OnDespawnedToPool()
    {
        _owningSpawner?.NotifyObstacleDespawned(this);
        _owningSpawner = null;
        _assignedSlot = null;
        _slotIndex = -1;
        _isDespawning = false;
        _currentHealth = ResolveStartingHealth();
        RestoreDefaultRendererStates();
        base.OnDespawnedToPool();
    }

    public void Bind(ObstacleSpawner owningSpawner, int slotIndex, Transform slot)
    {
        _owningSpawner = owningSpawner;
        _slotIndex = slotIndex;
        _assignedSlot = slot;
    }

    public bool ApplyDamage(int damage)
    {
        if (_isDespawning || damage <= 0)
            return false;

        _currentHealth = Mathf.Max(0, _currentHealth - damage);
        if (_currentHealth > 0)
            return true;

        return Despawn();
    }

    public bool Despawn()
    {
        if (_isDespawning || !gameObject.activeInHierarchy)
            return false;

        _isDespawning = true;
        SetControlledCollisionEnabled(false);
        SetRenderersVisible(false);

        if (SpawnKit.Despawn(gameObject))
            return true;

        _isDespawning = false;
        SetRenderersVisible(true);
        SetControlledCollisionEnabled(true);
        return false;
    }

    private int ResolveStartingHealth()
    {
        return obstacleData != null ? Mathf.Max(1, obstacleData.Health) : 1;
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

            cachedRenderer.enabled = _defaultRendererStates[i];
        }
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

            cachedRenderer.enabled = isVisible && _defaultRendererStates[i];
        }
    }
}
