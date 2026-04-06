using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemySpawner))]
public sealed class EnemyGridPathItem : ObjectSpawned
{
    private EnemySpawner _spawner;
    private EnemyGridGroup _owningGroup;

    public EnemySpawner Spawner => _spawner != null ? _spawner : (_spawner = GetComponent<EnemySpawner>());

    public EnemyGridGroup OwningGroup =>
        _owningGroup != null ? _owningGroup : (_owningGroup = GetComponentInParent<EnemyGridGroup>());
}
