using UnityEngine;
using Vit.SpawnKit.Api;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public sealed class Bullet : ObjectSpawned
{
    private const string EnemyTag = "Enemy";

    [SerializeField] private BulletSO bulletData;
    [SerializeField, Min(0f)] private float moveSpeed = 24f;

    private Vector3 _travelDirection = Vector3.forward;
    private bool _isLaunched;

    public BulletSO BulletData => bulletData;
    public int Damage => bulletData != null ? bulletData.Damage : 0;

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        _travelDirection = Vector3.forward;
        _isLaunched = false;
        SetControlledCollisionEnabled(true);

        if (CachedRigidbody != null)
            CachedRigidbody.linearVelocity = Vector3.zero;
    }

    public override void OnDespawnedToPool()
    {
        _travelDirection = Vector3.forward;
        _isLaunched = false;
        base.OnDespawnedToPool();
    }

    public void LaunchForward()
    {
        Launch(Vector3.forward);
    }

    public void Launch(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude <= 1e-6f)
            worldDirection = Vector3.forward;

        _travelDirection = worldDirection.normalized;
        _isLaunched = true;

        var targetTransform = CachedTransform != null ? CachedTransform : transform;
        targetTransform.rotation = Quaternion.LookRotation(_travelDirection, Vector3.up);

        if (CanUseRigidbodyMotion())
            CachedRigidbody.linearVelocity = _travelDirection * moveSpeed;
    }

    private void Update()
    {
        if (!_isLaunched || moveSpeed <= 0f || CanUseRigidbodyMotion())
            return;

        var targetTransform = CachedTransform != null ? CachedTransform : transform;
        targetTransform.position += _travelDirection * (moveSpeed * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleHit(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleHit(collision != null ? collision.collider : null);
    }

    private void HandleHit(Collider other)
    {
        if (!_isLaunched || !HasTagInHierarchy(other, EnemyTag))
            return;

        SpawnKit.Despawn(gameObject);
    }

    private bool CanUseRigidbodyMotion()
    {
        return CachedRigidbody != null && !CachedRigidbody.isKinematic;
    }

    private static bool HasTagInHierarchy(Collider other, string requiredTag)
    {
        if (other == null)
            return false;

        if (other.CompareTag(requiredTag))
            return true;

        var attachedRigidbody = other.attachedRigidbody;
        if (attachedRigidbody != null && attachedRigidbody.CompareTag(requiredTag))
            return true;

        Transform root = other.transform.root;
        return root != null && root.CompareTag(requiredTag);
    }
}
