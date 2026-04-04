using UnityEngine;
using Vit.SpawnKit.Pooling;

public class ObjectSpawned : MonoBehaviour, ISpawnPoolCallbacks
{
    [SerializeField] private bool disableCollidersWhileControlled = true;

    private Transform _cachedTransform;
    private Rigidbody _cachedRigidbody;
    private Collider[] _cachedColliders;
    private bool[] _defaultColliderStates;

    public Transform CachedTransform => _cachedTransform;
    public Rigidbody CachedRigidbody => _cachedRigidbody;

    protected virtual void Awake()
    {
        _cachedTransform = transform;
        _cachedRigidbody = GetComponent<Rigidbody>();
        CacheColliders();
    }

    public virtual void OnSpawnedFromPool()
    {
        ResetPhysicsState();
        SetControlledCollisionEnabled(!disableCollidersWhileControlled);
    }

    public virtual void OnDespawnedToPool()
    {
        ResetPhysicsState();
        RestoreDefaultColliderStates();
    }

    public void SetControlledCollisionEnabled(bool enabled)
    {
        if (_cachedColliders == null) return;

        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            var collider = _cachedColliders[i];
            if (collider == null) continue;

            collider.enabled = enabled && _defaultColliderStates[i];
        }
    }

    private void CacheColliders()
    {
        _cachedColliders = GetComponentsInChildren<Collider>(true);
        _defaultColliderStates = new bool[_cachedColliders.Length];

        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            _defaultColliderStates[i] = _cachedColliders[i] != null && _cachedColliders[i].enabled;
        }
    }

    private void RestoreDefaultColliderStates()
    {
        if (_cachedColliders == null) return;

        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            var collider = _cachedColliders[i];
            if (collider == null) continue;

            collider.enabled = _defaultColliderStates[i];
        }
    }

    private void ResetPhysicsState()
    {
        if (_cachedRigidbody == null) return;

        _cachedRigidbody.linearVelocity = Vector3.zero;
        _cachedRigidbody.angularVelocity = Vector3.zero;
    }
}
