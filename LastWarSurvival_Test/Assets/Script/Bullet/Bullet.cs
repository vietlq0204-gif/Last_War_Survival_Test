using UnityEngine;

[DisallowMultipleComponent]
public sealed class Bullet : ObjectSpawned
{
    [SerializeField] private BulletSO bulletData;
    [SerializeField, Min(0f)] private float moveSpeed = 24f;
    [SerializeField, Min(0f)] private float hitRadius = 0.12f;

    private Transform _visualTransform;
    private TrailRenderer[] _trailRenderers;
    private Renderer[] _cachedRenderers;
    private bool[] _defaultRendererStates;

    public BulletSO BulletData => bulletData;
    public int Damage => bulletData != null ? bulletData.Damage : 0;
    public float MoveSpeed => moveSpeed;
    public float HitRadius => hitRadius;

    protected override void Awake()
    {
        base.Awake();
        _visualTransform = CachedTransform != null ? CachedTransform : transform;
        _trailRenderers = GetComponentsInChildren<TrailRenderer>(true);
        CacheRenderers();
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetVisualState();
        SetRenderersVisible(true);
        SetControlledCollisionEnabled(true);
    }

    public override void OnDespawnedToPool()
    {
        ResetVisualState();
        SetRenderersVisible(true);
        base.OnDespawnedToPool();
    }

    public void SetWorldPose(Vector3 position, Vector3 forward)
    {
        Quaternion rotation = forward.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : _visualTransform.rotation;

        _visualTransform.SetPositionAndRotation(position, rotation);
    }

    public void SetWorldPosition(Vector3 position)
    {
        _visualTransform.position = position;
    }

    public void BeginQueuedDespawn()
    {
        SetControlledCollisionEnabled(false);
        SetRenderersVisible(false);
    }

    private void ResetVisualState()
    {
        if (_trailRenderers == null)
            return;

        for (int i = 0; i < _trailRenderers.Length; i++)
        {
            _trailRenderers[i]?.Clear();
        }
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
