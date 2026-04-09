using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponPickupItem : MonoBehaviour
{
    [SerializeField] private WeaponSO weaponData;
    [SerializeField, Min(0.05f)] private float collectDistance = 0.35f;
    [SerializeField, Min(0.01f)] private float pickupCheckInterval = 0.05f;
    [SerializeField, Min(0.01f)] private float moveToPlayerSpeed = 8f;
    [SerializeField] private string playerTag = "Player";

    private float _nextTargetResolveTime;
    private Transform _cachedPlayerTarget;
    private Teammate _cachedCollector;
    private bool _isCollected;

    public WeaponSO WeaponData => weaponData;
    public bool HasValidWeaponData => weaponData != null && weaponData.IsValid;

    private void OnEnable()
    {
        _nextTargetResolveTime = Time.time;
        _cachedPlayerTarget = null;
        _cachedCollector = null;
        _isCollected = false;
    }

    private void Update()
    {
        if (_isCollected || !HasValidWeaponData)
            return;

        RefreshTargetsIfNeeded();
        if (!TryResolvePickupState(out Teammate collector, out Vector3 targetPosition))
            return;

        float maxStep = Mathf.Max(0.01f, moveToPlayerSpeed) * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, maxStep);

        float collectDistanceSqr = Mathf.Max(0.01f, collectDistance) * Mathf.Max(0.01f, collectDistance);
        if ((transform.position - targetPosition).sqrMagnitude > collectDistanceSqr)
            return;

        _isCollected = true;
        CoreEvents.teammateWeaponPickup.Raise(new TeammateWeaponPickupEvent(
            collector,
            weaponData,
            this));

        Destroy(gameObject);
    }

    private void RefreshTargetsIfNeeded()
    {
        if (Time.time < _nextTargetResolveTime)
            return;

        _nextTargetResolveTime = Time.time + pickupCheckInterval;

        if (!IsTransformActive(_cachedPlayerTarget))
            _cachedPlayerTarget = ResolvePlayerTarget();

        if (_cachedCollector == null || !_cachedCollector.CanCollectPickups)
        {
            if (_cachedPlayerTarget != null)
                Teammate.TryGetClosestPickupCollector(_cachedPlayerTarget.position, out _cachedCollector);

            if (_cachedCollector == null)
                Teammate.TryGetClosestPickupCollector(transform.position, out _cachedCollector);
        }
    }

    private bool TryResolvePickupState(out Teammate collector, out Vector3 targetPosition)
    {
        collector = _cachedCollector;
        targetPosition = transform.position;

        if (collector == null || !collector.CanCollectPickups)
            return false;

        if (IsTransformActive(_cachedPlayerTarget))
        {
            targetPosition = _cachedPlayerTarget.position;
            return true;
        }

        Transform collectorTransform = collector.CachedTransform != null ? collector.CachedTransform : collector.transform;
        if (collectorTransform == null)
            return false;

        targetPosition = collectorTransform.position;
        return true;
    }

    private Transform ResolvePlayerTarget()
    {
        if (string.IsNullOrWhiteSpace(playerTag))
            return null;

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        return playerObject != null ? playerObject.transform : null;
    }

    private static bool IsTransformActive(Transform target)
    {
        return target != null && target.gameObject.activeInHierarchy;
    }
}
