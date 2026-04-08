using UnityEngine;

[CreateAssetMenu(menuName = "Gameplay/Reward Data", fileName = "RewardSO")]
public sealed class RewardSO : ScriptableObject
{
    [SerializeField] private ItemSO item;
    [SerializeField, Min(1)] private int count = 1;

    public ItemSO Item => item;
    public int Count => Mathf.Max(1, count);
    public bool HasValidItem => item != null && item.HasValidPrefab;

    public int ResolveTotalItemCount()
    {
        if (!HasValidItem)
            return 0;

        long total = (long)Count * item.Count;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private void OnValidate()
    {
        if (count < 1)
            count = 1;
    }
}
