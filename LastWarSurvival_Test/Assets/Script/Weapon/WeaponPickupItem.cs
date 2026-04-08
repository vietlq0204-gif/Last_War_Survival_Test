using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponPickupItem : MonoBehaviour
{
    [SerializeField] private WeaponSO weaponData;
    [SerializeField, Min(0.05f)] private float pickupRadius = 0.75f;
    [SerializeField, Min(0.01f)] private float pickupCheckInterval = 0.05f;

    private float _nextPickupCheckTime;
    private bool _isCollected;

    public WeaponSO WeaponData => weaponData;
    public bool HasValidWeaponData => weaponData != null && weaponData.IsValid;

    private void OnEnable()
    {
        _nextPickupCheckTime = Time.time;
        _isCollected = false;
    }

    private void Update()
    {
        if (_isCollected || !HasValidWeaponData)
            return;

        if (Time.time < _nextPickupCheckTime)
            return;

        _nextPickupCheckTime = Time.time + pickupCheckInterval;

        if (!Teammate.TryGetClosestPickupCollector(transform.position, pickupRadius, out Teammate collector))
            return;

        _isCollected = true;
        CoreEvents.teammateWeaponPickup.Raise(new TeammateWeaponPickupEvent(
            collector,
            weaponData,
            this));

        Destroy(gameObject);
    }
}
