using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class WeaponPickupItem : MonoBehaviour
{
    [SerializeField] private WeaponSO weaponData;
    [FormerlySerializedAs("pickupRadius")]
    [SerializeField, Min(0.05f)] private float collectDistance = 0.35f;
    [SerializeField, Min(0.01f)] private float pickupCheckInterval = 0.05f;
    [SerializeField, Min(0.01f)] private float moveToPlayerSpeed = 8f;
    [SerializeField, Min(0f)] private float moveArcHeight = 1.25f;
    [SerializeField] private string playerTag = "Player";

    private float _nextTargetResolveTime;
    private Transform _cachedPlayerTarget;
    private Teammate _cachedCollector;
    private Vector3 _arcStartPosition;
    private float _arcTravelProgress;
    private bool _hasActiveArc;
    private bool _isCollected;

    public WeaponSO WeaponData => weaponData;
    public bool HasValidWeaponData => weaponData != null && weaponData.IsValid;

    private void OnEnable()
    {
        _nextTargetResolveTime = Time.time;
        _cachedPlayerTarget = null;
        _cachedCollector = null;
        _arcStartPosition = transform.position;
        _arcTravelProgress = 0f;
        _hasActiveArc = false;
        _isCollected = false;
    }

    private void Update()
    {
        if (_isCollected || !HasValidWeaponData)
            return;

        RefreshTargetsIfNeeded();
        if (!TryResolvePickupState(out Teammate collector, out Vector3 targetPosition))
            return;

        if (!_hasActiveArc)
        {
            _arcStartPosition = transform.position;
            _arcTravelProgress = 0f;
            _hasActiveArc = true;
        }

        float totalDistance = Mathf.Max(0.01f, Vector3.Distance(_arcStartPosition, targetPosition));
        _arcTravelProgress = Mathf.Clamp01(_arcTravelProgress + (Mathf.Max(0.01f, moveToPlayerSpeed) * Time.deltaTime / totalDistance));

        Vector3 linearPosition = Vector3.LerpUnclamped(_arcStartPosition, targetPosition, _arcTravelProgress);
        float arcOffset = Mathf.Max(0f, moveArcHeight) * 4f * _arcTravelProgress * (1f - _arcTravelProgress);
        transform.position = linearPosition + Vector3.up * arcOffset;

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
