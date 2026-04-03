using UnityEngine;

namespace Vit.SpawnKit.Factories
{
/// <summary>
/// Factory used by pools to create instances.
/// </summary>
public interface ISpawnFactory
{
    bool IsReady { get; }
    GameObject CreateInstance(Transform parent, Vector3 position, Quaternion rotation);
    void Dispose();
}

/// <summary>
/// Factory for standard prefab instantiation.
/// </summary>
public sealed class PrefabSpawnFactory : ISpawnFactory
{
    private readonly GameObject _prefab;

    public bool IsReady => _prefab != null;

    public PrefabSpawnFactory(GameObject prefab)
    {
        _prefab = prefab;
    }

    public GameObject CreateInstance(Transform parent, Vector3 position, Quaternion rotation)
    {
        if (_prefab == null) return null;
        return Object.Instantiate(_prefab, position, rotation, parent);
    }

    public void Dispose()
    {
    }
}
}
