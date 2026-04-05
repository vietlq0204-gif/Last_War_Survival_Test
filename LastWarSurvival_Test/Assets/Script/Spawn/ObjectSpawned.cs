using UnityEngine;
using Vit.SpawnKit.Pooling;

public class ObjectSpawned : MonoBehaviour, ISpawnPoolCallbacks
{
    [SerializeField] private bool disableCollidersWhileControlled = true;

    private EntityId _cachedEntityId;
    private Transform _cachedTransform;
    private Rigidbody _cachedRigidbody;
    private Collider[] _cachedColliders;
    private bool[] _defaultColliderStates;
    private ObjectOnPathController _owningPathController;

    public EntityId CachedEntityId => _cachedEntityId;
    public Transform CachedTransform => _cachedTransform;
    public Rigidbody CachedRigidbody => _cachedRigidbody;

    protected virtual void Awake()
    {
        _cachedEntityId = gameObject.GetEntityId();
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
        ClearPathControllerOwner(null);
    }

    /// <summary>
    /// Gan controller dang quan ly object tren path de co the yeu cau release som khi can.
    /// </summary>
    public void SetPathControllerOwner(ObjectOnPathController owner)
    {
        _owningPathController = owner;
    }

    /// <summary>
    /// Xoa tham chieu controller khi object khong con nam tren path runtime.
    /// </summary>
    public void ClearPathControllerOwner(ObjectOnPathController owner)
    {
        if (owner != null && _owningPathController != owner) return;
        _owningPathController = null;
    }

    /// <summary>
    /// Thu yeu cau controller go object ra khoi stream va tra ve pool ngay.
    /// </summary>
    public bool TryReleaseFromPathController()
    {
        return _owningPathController != null && _owningPathController.TryReleaseSpawnedObject(this);
    }

    public void SetControlledCollisionEnabled(bool enabled)
    {
        if (_cachedColliders == null || _defaultColliderStates == null) return;

        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            var collider = _cachedColliders[i];
            if (collider == null) continue;

            bool targetState = enabled && _defaultColliderStates[i];
            if (collider.enabled != targetState)
                collider.enabled = targetState;
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
        if (_cachedColliders == null || _defaultColliderStates == null) return;

        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            var collider = _cachedColliders[i];
            if (collider == null) continue;

            bool targetState = _defaultColliderStates[i];
            if (collider.enabled != targetState)
                collider.enabled = targetState;
        }
    }

    private void ResetPhysicsState()
    {
        if (_cachedRigidbody == null) return;

        _cachedRigidbody.linearVelocity = Vector3.zero;
        _cachedRigidbody.angularVelocity = Vector3.zero;
    }
}
