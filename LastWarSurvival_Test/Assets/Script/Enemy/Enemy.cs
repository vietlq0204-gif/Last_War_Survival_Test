using UnityEngine;
using Vit.SpawnKit.Algorithms;

public class Enemy : ObjectSpawned
{
    private const string EnemyLayerName = "Enemy";

    [SerializeField, Min(1)] private int maxHealth = 1;
    [SerializeField, Min(0)] private int contactDamage = 1;
    [SerializeField] private bool canInteractWithHomeCollider = true;
    [SerializeField] private LayerMask despawnCollisionLayers = ~0;

    private EnemySpawner _owningSpawner;
    private Renderer[] _cachedRenderers;
    private bool[] _defaultRendererStates;
    private int _currentHealth;
    private bool _isDead;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => _currentHealth;
    public int ContactDamage => Mathf.Max(0, contactDamage);
    public bool IsAlive => !_isDead;
    public bool CanInteractWithHomeCollider => canInteractWithHomeCollider;

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
        _owningSpawner?.NotifyEnemyDespawned(this);
        _owningSpawner = null;
        _isDead = false;
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

    public bool CanDespawnOnCollisionLayer(int collisionLayer)
    {
        return IsLayerIncluded(despawnCollisionLayers, collisionLayer);
    }

    public bool TryResolveHomeImpact(int collisionLayer)
    {
        if (_isDead || !gameObject.activeInHierarchy || !canInteractWithHomeCollider)
            return false;

        if (!CanDespawnOnCollisionLayer(collisionLayer))
            return false;

        EnemySpawner owningSpawner = _owningSpawner != null ? _owningSpawner : ResolveOwningSpawner();
        owningSpawner?.NotifyEnemyReachedHome(this, collisionLayer);
        HandleHomeImpact(owningSpawner);
        return true;
    }

    private void HandleDefeat()
    {
        if (_isDead)
            return;

        _isDead = true;
        _currentHealth = 0;
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

        _isDead = true;
        _currentHealth = 0;
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

    private static bool IsLayerIncluded(LayerMask layerMask, int layer)
    {
        if (layer < 0 || layer > 31)
            return false;

        return (layerMask.value & (1 << layer)) != 0;
    }
}
