using UnityEngine;
using Vit.SpawnKit.Algorithms;

public enum ObstacleSlotSelectionMode
{
    EmptyOnly = 0,
    OccupiedOnly = 1,
    AnyUnlocked = 2,
}

public class Enemy : ObjectSpawned
{
    private const string ObstacleLayerName = "Obstacle";
    private const string FlowLogPrefix = "[HomeDamageFlow][Enemy]";

    [SerializeField, Min(1), Tooltip("Luong mau toi da cua enemy.")]
    private int maxHealth = 1;
    [SerializeField, Min(0), Tooltip("Luong damage enemy gay ra khi cham vao Home.")]
    private int contactDamage = 1;

    [SerializeField, Tooltip("Enemy co kha nang Despawn khi tuong tac voi cac layer duoc chon hay khong")]
    private bool canDespawnWithLayers = true;
    [SerializeField, Tooltip("Layer ma enemy co the Despawn.")]
    private LayerMask DespawnLayers = ~0;

    [SerializeField, Tooltip("Enemy co kha nang gay damage cho cac layer duoc chon hay khong")]
    private bool canTakeDamageWithLayers = true;
    [SerializeField, Tooltip("Layer ma enemy co the gay damage.")]
    private LayerMask TakeDamageLayers = ~0;

    [Header("Obstacle Avoidance")]
    [SerializeField, Tooltip("Enemy co kha nang tranh ne cac layer duoc chon hay khong")]
    private bool canObstacleAvoidanceWithLayers = true;
    [SerializeField, Tooltip("Layer duoc xem la obstacle de enemy ne tranh va xu ly va cham.")]
    private LayerMask AvoidanceLayers;
    [SerializeField, Tooltip("Cach chon o luoi moi khi enemy can doi vi tri de tranh obstacle.")]
    private ObstacleSlotSelectionMode obstacleSlotSelectionMode = ObstacleSlotSelectionMode.EmptyOnly;
    [SerializeField, Min(0.05f), Tooltip("Toc do di chuyen cua enemy khi dang doi sang o moi de tranh obstacle.")]
    private float obstacleRepositionSpeed = 5f;
    [SerializeField, Min(0.05f), Tooltip("Khoang cach toi thieu de tim o moi khi enemy bi chan boi obstacle.")]
    private float obstacleSlotDistance = 0.75f;
    [SerializeField, Min(0.05f), Tooltip("Khoang cach coi nhu da den dich khi enemy di chuyen vao o tranh obstacle.")]
    private float obstacleArrivalDistance = 0.15f;

    [Header("Debug")]
    [SerializeField, Tooltip("Bat log debug cho cac moc spawn, cham Home, defeat va despawn cua enemy.")]
    private bool debugLifecycleLogs = true;

    private EnemySpawner _owningSpawner;
    private Renderer[] _cachedRenderers;
    private bool[] _defaultRendererStates;
    private int _currentHealth;
    private bool _isDead;
    private bool _isMovingToObstacleSlot;
    private Vector3 _obstacleTargetLocalPosition;
    private Quaternion _obstacleTargetLocalRotation;
    private CapsuleCollider _cachedObstacleProbeCapsule;
    private Collider _cachedObstacleProbeCollider;
    private Collider _lastHandledObstacle;
    private int _lastHandledObstacleFrame = int.MinValue;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => _currentHealth;
    public int ContactDamage => Mathf.Max(0, contactDamage);
    public bool IsAlive => !_isDead;
    public bool CanInteractWithHomeCollider => true;

    protected override void Awake()
    {
        base.Awake();
        EnsureObstacleAvoidanceLayer();
        CacheRenderers();
    }

    private void OnValidate()
    {
        obstacleRepositionSpeed = Mathf.Max(0.05f, obstacleRepositionSpeed);
        obstacleSlotDistance = Mathf.Max(0.05f, obstacleSlotDistance);
        obstacleArrivalDistance = Mathf.Max(0.05f, obstacleArrivalDistance);
        EnsureObstacleAvoidanceLayer();
    }

    private void Update()
    {
        if (_isMovingToObstacleSlot)
            TickObstacleSlotMove(Time.deltaTime);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        _isDead = false;
        _isMovingToObstacleSlot = false;
        _obstacleTargetLocalPosition = Vector3.zero;
        _obstacleTargetLocalRotation = Quaternion.identity;
        _lastHandledObstacle = null;
        _lastHandledObstacleFrame = int.MinValue;
        _currentHealth = Mathf.Max(1, maxHealth);
        EnsureObstacleAvoidanceLayer();
        RestoreDefaultRendererStates();
        SetControlledCollisionEnabled(true);

        _owningSpawner = ResolveOwningSpawner();
        _owningSpawner?.RegisterSpawnedEnemy(this);
        EnemyHomeTargetService.EnsureInitialized();
    }

    public override void OnDespawnedToPool()
    {
        _owningSpawner?.NotifyEnemyDespawned(this);
        _owningSpawner = null;
        _isDead = false;
        _isMovingToObstacleSlot = false;
        _obstacleTargetLocalPosition = Vector3.zero;
        _obstacleTargetLocalRotation = Quaternion.identity;
        _lastHandledObstacle = null;
        _lastHandledObstacleFrame = int.MinValue;
        _currentHealth = Mathf.Max(1, maxHealth);
        RestoreDefaultRendererStates();
        base.OnDespawnedToPool();
    }

    public bool ApplyDamage(int damage)
    {
        if (_isDead || damage <= 0)
            return false;

        _currentHealth = Mathf.Max(0, _currentHealth - damage);
        if (_currentHealth > 0)
            return true;

        HandleDefeat();
        return true;
    }

    public void BeginQueuedDespawn()
    {
        if (_isDead)
            return;

        HandleDefeat();
    }

    public void SnapToGridSlot(Vector3 localPosition, Quaternion localRotation)
    {
        _isMovingToObstacleSlot = false;
        _obstacleTargetLocalPosition = localPosition;
        _obstacleTargetLocalRotation = localRotation;
        (_owningSpawner != null ? _owningSpawner : ResolveOwningSpawner())?.ReleaseBlockedSlotForEnemy(this);
        transform.localPosition = localPosition;
        transform.localRotation = localRotation;
    }

    public void DespawnForRecycle(string reason = "Recycle")
    {
        if (_isDead || !gameObject.activeInHierarchy)
            return;

        LogLifecycleDebug(
            $"[{reason}] Enemy is being queued for pool despawn. worldPos={transform.position}, localPos={transform.localPosition}, parent='{transform.parent?.name ?? "<null>"}'.",
            warning: true);
        _isDead = true;
        _currentHealth = 0;
        _isMovingToObstacleSlot = false;
        ReleaseBlockedSlotClaim();
        ReleaseGridReservation();

        EnemySpawner owningSpawner = _owningSpawner != null ? _owningSpawner : ResolveOwningSpawner();
        owningSpawner?.NotifyEnemyExitedGrid(this);
        _owningSpawner = null;

        SetControlledCollisionEnabled(false);
        SetRenderersVisible(false);
        BufferedPoolDespawnQueue.Queue(this);
    }

    public bool TryHandleObstacleProbe(Collider obstacle, int frameCount, int cooldownFrames)
    {
        if (_isDead || obstacle == null || !IsObstacleLayer(obstacle.gameObject.layer))
            return false;

        if (obstacle.transform.IsChildOf(transform))
            return false;

        int safeCooldownFrames = Mathf.Max(0, cooldownFrames);
        if (_lastHandledObstacle == obstacle && frameCount - _lastHandledObstacleFrame <= safeCooldownFrames)
            return false;

        _lastHandledObstacle = obstacle;
        _lastHandledObstacleFrame = frameCount;
        BeginOrRefreshObstacleReposition();
        return true;
    }

    public bool TryGetObstacleProbeCapsule(float extraRadius, out Vector3 point0, out Vector3 point1, out float radius)
    {
        point0 = transform.position;
        point1 = transform.position;
        radius = 0f;

        EnsureObstacleProbeColliderCache();

        if (_cachedObstacleProbeCapsule != null)
        {
            Transform targetTransform = _cachedObstacleProbeCapsule.transform;
            Vector3 lossyScale = targetTransform.lossyScale;
            Vector3 absLossyScale = new Vector3(
                Mathf.Abs(lossyScale.x),
                Mathf.Abs(lossyScale.y),
                Mathf.Abs(lossyScale.z));

            int direction = _cachedObstacleProbeCapsule.direction;
            float axisScale = direction == 0
                ? absLossyScale.x
                : direction == 1
                    ? absLossyScale.y
                    : absLossyScale.z;
            float radialScale = direction == 0
                ? Mathf.Max(absLossyScale.y, absLossyScale.z)
                : direction == 1
                    ? Mathf.Max(absLossyScale.x, absLossyScale.z)
                    : Mathf.Max(absLossyScale.x, absLossyScale.y);

            radius = Mathf.Max(0.01f, _cachedObstacleProbeCapsule.radius * radialScale + Mathf.Max(0f, extraRadius));

            float scaledHeight = Mathf.Max(_cachedObstacleProbeCapsule.height * axisScale, radius * 2f);
            float axialExtent = Mathf.Max(0f, scaledHeight * 0.5f - radius);
            Vector3 axis = direction == 0
                ? targetTransform.right
                : direction == 1
                    ? targetTransform.up
                    : targetTransform.forward;

            Vector3 center = targetTransform.TransformPoint(_cachedObstacleProbeCapsule.center);
            point0 = center + axis * axialExtent;
            point1 = center - axis * axialExtent;
            return true;
        }

        if (_cachedObstacleProbeCollider == null)
            return false;

        Bounds bounds = _cachedObstacleProbeCollider.bounds;
        Vector3 centerPoint = bounds.center;
        float fallbackRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) + Mathf.Max(0f, extraRadius);
        float verticalExtent = Mathf.Max(0f, bounds.extents.y - fallbackRadius);

        point0 = centerPoint + Vector3.up * verticalExtent;
        point1 = centerPoint - Vector3.up * verticalExtent;
        radius = Mathf.Max(0.01f, fallbackRadius);
        return true;
    }

    public bool CanDespawnOnCollisionLayer(int collisionLayer)
    {
        return IsLayerAllowed(canDespawnWithLayers, DespawnLayers, collisionLayer);
    }

    public bool CanDealDamageOnCollisionLayer(int collisionLayer)
    {
        return IsLayerAllowed(canTakeDamageWithLayers, TakeDamageLayers, collisionLayer);
    }

    public bool TryResolveHomeImpact(int collisionLayer)
    {
        if (_isDead || !gameObject.activeInHierarchy)
        {
            LogFlow($"Rejected home impact because enemy is dead or inactive. collisionLayer={collisionLayer} ('{LayerMask.LayerToName(collisionLayer)}').", warning: true);
            return false;
        }

        if (!CanDealDamageOnCollisionLayer(collisionLayer))
        {
            LogFlow(
                $"Rejected home impact because CanDealDamageOnCollisionLayer=false. collisionLayer={collisionLayer} ('{LayerMask.LayerToName(collisionLayer)}') " +
                $"canTakeDamageWithLayers={canTakeDamageWithLayers} takeDamageLayers={TakeDamageLayers.value}.",
                warning: true);
            return false;
        }

        EnemySpawner owningSpawner = _owningSpawner != null ? _owningSpawner : ResolveOwningSpawner();
        if (owningSpawner != null && !owningSpawner.CanEnemiesInteractWithHomeCollider())
        {
            LogFlow(
                $"Rejected home impact because owning spawner '{owningSpawner.name}' disallows home interaction while using path runtime.",
                owningSpawner,
                warning: true);
            return false;
        }

        LogFlow(
            $"Accepted home impact. collisionLayer={collisionLayer} ('{LayerMask.LayerToName(collisionLayer)}') " +
            $"contactDamage={ContactDamage} canDespawn={CanDespawnOnCollisionLayer(collisionLayer)} " +
            $"worldPos={transform.position} spawner='{owningSpawner?.name ?? "<null>"}'.",
            owningSpawner,
            warning: true);
        owningSpawner?.NotifyEnemyReachedHome(this, collisionLayer);

        if (CanDespawnOnCollisionLayer(collisionLayer))
            HandleHomeImpact(owningSpawner);
        else
            LogFlow(
                $"Enemy dealt damage to Home but will stay alive because CanDespawnOnCollisionLayer=false for layer '{LayerMask.LayerToName(collisionLayer)}'.",
                owningSpawner);

        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleObstacleCollision(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
            return;

        HandleObstacleCollision(collision.collider);
    }

    private void HandleDefeat()
    {
        if (_isDead)
            return;

        _isDead = true;
        _currentHealth = 0;
        _isMovingToObstacleSlot = false;
        ReleaseBlockedSlotClaim();
        ReleaseGridReservation();
        SetControlledCollisionEnabled(false);
        SetRenderersVisible(false);
        _owningSpawner?.NotifyEnemyDefeated(this);
        BufferedPoolDespawnQueue.Queue(this);
    }

    private void HandleHomeImpact(EnemySpawner owningSpawner)
    {
        if (_isDead)
            return;

        LogLifecycleDebug(
            $"[HomeImpact] Enemy is being despawned after hitting Home. worldPos={transform.position}, currentParent='{transform.parent?.name ?? "<null>"}'.",
            warning: true);
        _isDead = true;
        _currentHealth = 0;
        _isMovingToObstacleSlot = false;
        ReleaseBlockedSlotClaim();
        ReleaseGridReservation();

        Transform detachedParent = owningSpawner != null ? owningSpawner.transform.parent : null;
        transform.SetParent(detachedParent, true);

        if (owningSpawner != null)
        {
            owningSpawner.NotifyEnemyExitedGrid(this);
            _owningSpawner = null;
        }

        SetControlledCollisionEnabled(false);
        SetRenderersVisible(false);
        BufferedPoolDespawnQueue.Queue(this);
    }

    private void HandleObstacleCollision(Collider other)
    {
        if (_isDead || other == null || !IsObstacleLayer(other.gameObject.layer))
            return;

        if (other.transform.IsChildOf(transform))
            return;

        BeginOrRefreshObstacleReposition();
    }

    private void BeginOrRefreshObstacleReposition()
    {
        EnemySpawner owningSpawner = _owningSpawner != null ? _owningSpawner : ResolveOwningSpawner();
        if (owningSpawner == null)
            return;

        _owningSpawner = owningSpawner;
        if (transform.parent != owningSpawner.transform)
            transform.SetParent(owningSpawner.transform, true);

        if (!owningSpawner.TryRelocateEnemyToUnblockedSlot(
                this,
                obstacleSlotSelectionMode,
                obstacleSlotDistance,
                out _obstacleTargetLocalPosition,
                out _obstacleTargetLocalRotation))
        {
            _isMovingToObstacleSlot = false;
            return;
        }

        _isMovingToObstacleSlot = true;
    }

    private void TickObstacleSlotMove(float deltaTime)
    {
        if (!_isMovingToObstacleSlot || deltaTime <= 0f)
            return;

        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition,
            _obstacleTargetLocalPosition,
            obstacleRepositionSpeed * deltaTime);

        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation,
            _obstacleTargetLocalRotation,
            obstacleRepositionSpeed * 180f * deltaTime);

        if (Vector3.Distance(transform.localPosition, _obstacleTargetLocalPosition) <= obstacleArrivalDistance)
        {
            transform.localPosition = _obstacleTargetLocalPosition;
            transform.localRotation = _obstacleTargetLocalRotation;
            _isMovingToObstacleSlot = false;
        }
    }

    private EnemySpawner ResolveOwningSpawner()
    {
        return GetComponentInParent<EnemySpawner>();
    }

    private void ReleaseGridReservation()
    {
        if (!TryGetComponent(out SpawnGridSlotReservation reservation))
            return;

        reservation.ReleaseReservationNow();
    }

    private void ReleaseBlockedSlotClaim()
    {
        (_owningSpawner != null ? _owningSpawner : ResolveOwningSpawner())?.ReleaseBlockedSlotForEnemy(this);
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

    private void EnsureObstacleProbeColliderCache()
    {
        if (_cachedObstacleProbeCapsule == null)
            _cachedObstacleProbeCapsule = GetComponent<CapsuleCollider>();

        if (_cachedObstacleProbeCollider == null)
            _cachedObstacleProbeCollider = _cachedObstacleProbeCapsule != null
                ? _cachedObstacleProbeCapsule
                : GetComponent<Collider>();
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

    private void EnsureObstacleAvoidanceLayer()
    {
        if (AvoidanceLayers.value != 0)
            return;

        int obstacleLayer = LayerMask.NameToLayer(ObstacleLayerName);
        if (obstacleLayer < 0)
            return;

        AvoidanceLayers = 1 << obstacleLayer;
    }

    private bool IsObstacleLayer(int layer)
    {
        return IsLayerAllowed(canObstacleAvoidanceWithLayers, AvoidanceLayers, layer);
    }

    private static bool IsLayerAllowed(bool useLayerFilter, LayerMask layerMask, int layer)
    {
        if (!useLayerFilter)
            return false;

        return IsLayerIncluded(layerMask, layer);
    }

    private static bool IsLayerIncluded(LayerMask layerMask, int layer)
    {
        if (layer < 0 || layer > 31)
            return false;

        return (layerMask.value & (1 << layer)) != 0;
    }

    private void LogLifecycleDebug(string message, bool warning = false)
    {
        if (!debugLifecycleLogs)
            return;

        string formattedMessage = $"[Enemy:{name}#{CachedEntityId}] {message}";
        if (warning)
            Debug.LogWarning(formattedMessage, this);
        else
            Debug.Log(formattedMessage, this);
    }

    private void LogFlow(string message, Object context = null, bool warning = false)
    {
        if (!debugLifecycleLogs)
            return;

        string formattedMessage = $"{FlowLogPrefix}[{name}#{CachedEntityId}] {message}";
        Object resolvedContext = context != null ? context : this;
        if (warning)
            Debug.LogWarning(formattedMessage, resolvedContext);
        else
            Debug.Log(formattedMessage, resolvedContext);
    }
}
