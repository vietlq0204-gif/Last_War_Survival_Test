using UnityEngine;

[CreateAssetMenu(menuName = "Gameplay/Item Data", fileName = "ItemSO")]
public sealed class ItemSO : ScriptableObject
{
    [SerializeField] private GameObject prefabItem;
    [SerializeField, Min(1)] private int count = 1;

    public GameObject PrefabItem => prefabItem;
    public int Count => Mathf.Max(1, count);
    public bool HasValidPrefab => prefabItem != null;

    private void OnValidate()
    {
        if (count < 1)
            count = 1;
    }
}
