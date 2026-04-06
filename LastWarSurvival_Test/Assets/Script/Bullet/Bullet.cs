using UnityEngine;

[DisallowMultipleComponent]
public sealed class Bullet : ObjectSpawned
{
    [SerializeField] private BulletSO bulletData;
    [SerializeField, Min(0f)] private float moveSpeed = 24f;
    [SerializeField, Min(0f)] private float hitRadius = 0.12f;

    private Transform _visualTransform;
    private TrailRenderer[] _trailRenderers;

    public BulletSO BulletData => bulletData;
    public int Damage => bulletData != null ? bulletData.Damage : 0;
    public float MoveSpeed => moveSpeed;
    public float HitRadius => hitRadius;

    protected override void Awake()
    {
        base.Awake();
        _visualTransform = CachedTransform != null ? CachedTransform : transform;
        _trailRenderers = GetComponentsInChildren<TrailRenderer>(true);
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetVisualState();
    }

    public override void OnDespawnedToPool()
    {
        ResetVisualState();
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

    private void ResetVisualState()
    {
        if (_trailRenderers == null)
            return;

        for (int i = 0; i < _trailRenderers.Length; i++)
        {
            _trailRenderers[i]?.Clear();
        }
    }
}
