using UnityEngine;

[CreateAssetMenu(menuName = "Game/Obstacle Data", fileName = "Obstacle_Data")]
public sealed class ObstacleSO : ScriptableObject
{
    [SerializeField, Min(1)] private int health = 1;
    [SerializeField] private GameObject itemReward;

    public int Health => health;
    public GameObject ItemReward => itemReward;
    public bool HasItemReward => itemReward != null;

    private void OnValidate()
    {
        if (health < 1)
            health = 1;
    }
}
