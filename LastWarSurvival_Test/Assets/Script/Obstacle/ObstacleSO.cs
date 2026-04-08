using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Game/Obstacle Data", fileName = "Obstacle_Data")]
public sealed class ObstacleSO : ScriptableObject
{
    [SerializeField, Min(1)] private int health = 1;
    [FormerlySerializedAs("itemReward")]
    [SerializeField] private RewardSO reward;

    public int Health => health;
    public RewardSO Reward => reward;
    public bool HasReward => reward != null && reward.HasValidItem;

    private void OnValidate()
    {
        if (health < 1)
            health = 1;
    }
}
