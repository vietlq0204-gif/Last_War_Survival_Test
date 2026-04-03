using System.Collections.Generic;
using UnityEngine;

namespace Vit.SpawnKit.ScriptableObjects
{
/// <summary>
/// Catalog of spawnables used for auto-registration.
/// </summary>
[CreateAssetMenu(menuName = "SpawnKit/Spawn Catalog", fileName = "_SpawnCatalog")]
public class SpawnCatalogSO : ScriptableObject
{
    [Tooltip("Spawnables that SpawnManager auto-registers at startup or when this catalog is prewarmed.")]
    public List<SpawnableSO> items = new List<SpawnableSO>();

    private void OnValidate()
    {
        if (items == null) return;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
            {
                items[i].key.RecomputeHash();
                items[i].poolConfig?.Sanitize();
                items[i].defaultLifecycle.Sanitize();
            }
        }
    }
}
}
