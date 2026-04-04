using System;
using UnityEngine;
using Object = UnityEngine.Object;

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

        try
        {
            var instance = Object.Instantiate((Object)_prefab, position, rotation, parent);
            if (instance is GameObject gameObject) return gameObject;
            if (instance is Component component) return component.gameObject;

            Debug.LogError($"PrefabSpawnFactory instantiated unsupported type '{instance?.GetType().Name ?? "null"}' from prefab '{_prefab.name}'.", _prefab);
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"PrefabSpawnFactory failed to instantiate prefab '{_prefab.name}'. {ex}", _prefab);
            return null;
        }
    }

    public void Dispose()
    {
    }
}
}
