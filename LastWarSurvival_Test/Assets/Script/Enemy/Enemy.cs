using UnityEngine;
using Vit.SpawnKit.Algorithms;

public class Enemy : ObjectSpawned
{
    private const string EnemyLayerName = "Enemy";

    [SerializeField, Min(1)] private int maxHealth = 1;
    [SerializeField, Min(0f)] private float homeChaseMoveSpeed = 6f;
    [SerializeField, Min(0f)] private float playerReachStoppingDistance = 0.15f;

    private EnemySpawner _owningSpawner;
    private Renderer[] _cachedRenderers;
    private bool[] _defaultRendererStates;
    private int _currentHealth;
    private bool _isDead;
    [SerializeField] private bool _isChasingPlayer;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => _currentHealth;
    public bool IsAlive => !_isDead;
    public bool IsChasingPlayer => _isChasingPlayer;

    protected override void Awake()
    {
        base.Awake();
        EnsureHitDetectionLayer();
        CacheRenderers();
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        _isDead = false;
        _isChasingPlayer = false;
        _currentHealth = Mathf.Max(1, maxHealth);
        EnsureHitDetectionLayer();
        RestoreDefaultRendererStates();
        SetControlledCollisionEnabled(true);

        _owningSpawner = ResolveOwningSpawner();
        _owningSpawner?.RegisterSpawnedEnemy(this);
        EnemyHomeTargetService.EnsureInitialized();
    }

    public override void OnDespawnedToPool()
    {
        EnemyHomeTargetService.UnregisterChasingEnemy(this);
        _owningSpawner?.NotifyEnemyDespawned(this);
        _owningSpawner = null;
        _isDead = false;
        _isChasingPlayer = false;
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

        HandleDeath();
        return true;
    }

    public void BeginQueuedDespawn()
    {
        if (_isDead)
            return;

        HandleDeath();
    }

    public bool TryBeginHomeTargeting()
    {
        if (_isDead || _isChasingPlayer || !gameObject.activeInHierarchy)
            return false;

        if (!EnemyHomeTargetService.HasPlayerTarget())
            return false;

        if (!TryDetachFromGrid())
            return false;

        if (!EnemyHomeTargetService.RegisterChasingEnemy(this))
            return false;

        _isChasingPlayer = true;
        return true;
    }

    public bool TickChasePlayer(Vector3 targetPosition, float deltaTime)
    {
        if (_isDead || !_isChasingPlayer || deltaTime <= 0f)
            return false;

        Transform targetTransform = CachedTransform != null ? CachedTransform : transform;
        Vector3 currentPosition = targetTransform.position;
        targetPosition.y = currentPosition.y;

        Vector3 toTarget = targetPosition - currentPosition;
        float sqrDistance = toTarget.sqrMagnitude;
        float stopDistance = Mathf.Max(0f, playerReachStoppingDistance);
        if (sqrDistance <= stopDistance * stopDistance)
            return true;

        float moveSpeed = Mathf.Max(0.01f, homeChaseMoveSpeed);
        Vector3 nextPosition = Vector3.MoveTowards(currentPosition, targetPosition, moveSpeed * deltaTime);
        Vector3 direction = targetPosition - currentPosition;
        targetTransform.position = nextPosition;

        if (direction.sqrMagnitude > 1e-6f)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 1e-6f)
                targetTransform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        return true;
    }

    private void HandleDeath()
    {
        if (_isDead)
            return;

        _isDead = true;
        _isChasingPlayer = false;
        _currentHealth = 0;

        EnemyHomeTargetService.UnregisterChasingEnemy(this);
        ReleaseGridReservation();
        SetControlledCollisionEnabled(false);
        SetRenderersVisible(false);
        _owningSpawner?.NotifyEnemyDefeated(this);
        BufferedPoolDespawnQueue.Queue(this);
    }

    private EnemySpawner ResolveOwningSpawner()
    {
        return GetComponentInParent<EnemySpawner>();
    }

    private bool TryDetachFromGrid()
    {
        ReleaseGridReservation();

        EnemySpawner owningSpawner = _owningSpawner != null ? _owningSpawner : ResolveOwningSpawner();
        Transform detachedParent = owningSpawner != null ? owningSpawner.transform.parent : null;
        transform.SetParent(detachedParent, true);

        if (owningSpawner != null)
        {
            owningSpawner.NotifyEnemyExitedGrid(this);
            _owningSpawner = null;
        }

        return true;
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

    private void EnsureHitDetectionLayer()
    {
        int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
        if (enemyLayer < 0)
            return;

        if (gameObject.layer != enemyLayer)
            gameObject.layer = enemyLayer;

        var colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            if (collider.gameObject.layer != enemyLayer)
                collider.gameObject.layer = enemyLayer;
        }
    }
}
