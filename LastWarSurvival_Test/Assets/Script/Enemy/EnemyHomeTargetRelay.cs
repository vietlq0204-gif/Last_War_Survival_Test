using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyHomeTargetRelay : MonoBehaviour
{
    private EnemyHomeTargetService _owner;
    private Collider _homeCollider;

    public Collider HomeCollider => _homeCollider;

    public void Initialize(EnemyHomeTargetService owner, Collider homeCollider)
    {
        _owner = owner;
        _homeCollider = homeCollider;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_owner == null || _homeCollider == null || other == null)
            return;

        _owner.HandleHomeTriggerEnterFromRelay(_homeCollider, other);
    }
}
