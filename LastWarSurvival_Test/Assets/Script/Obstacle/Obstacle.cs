using UnityEngine;
using Vit.SpawnKit.Api;
using TMPro;

public sealed class Obstacle : ObjectSpawned
{
    [Header("Data")]
    [SerializeField] private ObstacleSO obstacleData;
    [SerializeField] private TMP_Text healthText;

    [Header("Slot Motion")]
    [SerializeField, Min(0f)] private float moveToSlotDuration = 0.2f;
    [SerializeField, Min(0f)] private float slotPositionTolerance = 0.01f;
    [SerializeField, Min(0f)] private float slotRotationTolerance = 0.5f;
    [SerializeField] private AnimationCurve moveToSlotCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private ObstacleSpawner _owningSpawner;
    private Transform _assignedSlot;
    private Renderer[] _cachedRenderers;
    private bool[] _defaultRendererStates;
    private DamageShakeFeedback _damageShakeFeedback;
    private int _currentHealth;
    private int _slotIndex = -1;
    private bool _isDespawning;
    private bool _dropRewardOnDespawn;
    private bool _raiseDespawnEvent;
    private Vector3 _despawnEventWorldPosition;

    public ObstacleSO Data => obstacleData;
    public int MaxHealth => ResolveStartingHealth();
    public int CurrentHealth => _currentHealth;
    public int SlotIndex => _slotIndex;

    protected override void Awake()
    {
        base.Awake();
        ResolveHealthText();
        CacheRenderers();
        _damageShakeFeedback = GetComponentInChildren<DamageShakeFeedback>(true);
    }

    private void OnValidate()
    {
        ResolveHealthText();
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
        _dropRewardOnDespawn = false;
        _raiseDespawnEvent = false;
        _despawnEventWorldPosition = Vector3.zero;
        _currentHealth = ResolveStartingHealth();
        RestoreDefaultRendererStates();
        RefreshHealthUi();
        base.OnSpawnedFromPool();
    }

    public override void OnDespawnedToPool()
    {
        if (_raiseDespawnEvent)
        {
            CoreEvents.obstacleDespawned.Raise(new ObstacleDespawnedEvent(
                this,
                _owningSpawner,
                _despawnEventWorldPosition,
                _slotIndex,
                _dropRewardOnDespawn));
        }

        if (_dropRewardOnDespawn)
            SpawnRewardIfNeeded();

        _owningSpawner?.NotifyObstacleDespawned(this);
        _owningSpawner = null;
        _assignedSlot = null;
        _slotIndex = -1;
        _isDespawning = false;
        _dropRewardOnDespawn = false;
        _raiseDespawnEvent = false;
        _despawnEventWorldPosition = Vector3.zero;
        _currentHealth = ResolveStartingHealth();
        RestoreDefaultRendererStates();
        RefreshHealthUi();
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

        TriggerDamageShake();
        _currentHealth = Mathf.Max(0, _currentHealth - damage);
        RefreshHealthUi();
        if (_currentHealth > 0)
            return true;

        _dropRewardOnDespawn = true;
        return Despawn();
    }

    public bool Despawn()
    {
        if (_isDespawning || !gameObject.activeInHierarchy)
            return false;

        Transform targetTransform = CachedTransform != null ? CachedTransform : transform;
        _isDespawning = true;
        _raiseDespawnEvent = true;
        _despawnEventWorldPosition = targetTransform.position;
        SetControlledCollisionEnabled(false);
        SetRenderersVisible(false);

        if (SpawnKit.Despawn(gameObject))
            return true;

        _isDespawning = false;
        _dropRewardOnDespawn = false;
        _raiseDespawnEvent = false;
        _despawnEventWorldPosition = Vector3.zero;
        SetRenderersVisible(true);
        SetControlledCollisionEnabled(true);
        RefreshHealthUi();
        return false;
    }

    private int ResolveStartingHealth()
    {
        return obstacleData != null ? Mathf.Max(1, obstacleData.Health) : 1;
    }

    private void ResolveHealthText()
    {
        if (healthText != null)
            return;

        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        TMP_Text firstAvailable = null;

        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text candidate = texts[i];
            if (candidate == null)
                continue;

            if (firstAvailable == null)
                firstAvailable = candidate;

            string candidateName = candidate.name.ToLowerInvariant();
            if (candidateName.Contains("heath") || candidateName.Contains("health"))
            {
                healthText = candidate;
                return;
            }
        }

        healthText = firstAvailable;
    }

    private void RefreshHealthUi()
    {
        if (healthText == null)
            ResolveHealthText();

        if (healthText == null)
            return;

        healthText.SetText("{0}", Mathf.Max(0, _currentHealth));
    }

    private void SpawnRewardIfNeeded()
    {
        RewardSO reward = obstacleData != null ? obstacleData.Reward : null;
        if (reward == null || !reward.HasValidItem)
            return;

        Transform targetTransform = CachedTransform != null ? CachedTransform : transform;
        GameObject rewardPrefab = reward.Item != null ? reward.Item.PrefabItem : null;
        int rewardCount = reward.ResolveTotalItemCount();
        if (rewardPrefab == null || rewardCount <= 0)
            return;

        for (int i = 0; i < rewardCount; i++)
        {
            Vector3 spawnOffset = ResolveRewardSpawnOffset(i, rewardCount);
            Instantiate(
                rewardPrefab,
                targetTransform.position + spawnOffset,
                targetTransform.rotation);
        }
    }

    private static Vector3 ResolveRewardSpawnOffset(int index, int totalCount)
    {
        if (totalCount <= 1)
            return Vector3.zero;

        float angle = index * Mathf.PI * 2f / totalCount;
        float radius = Mathf.Min(0.35f, 0.08f * Mathf.Max(1, totalCount - 1));
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
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

    private void TriggerDamageShake()
    {
        if (_damageShakeFeedback == null)
            _damageShakeFeedback = GetComponentInChildren<DamageShakeFeedback>(true);

        _damageShakeFeedback?.PlayShake();
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
