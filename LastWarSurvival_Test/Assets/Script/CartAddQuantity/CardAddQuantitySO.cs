using UnityEngine;

/// <summary>
/// ScriptableObject chua data gameplay cua CardAddQuantity.
/// </summary>
[CreateAssetMenu(menuName = "Game/Card Add Quantity Data", fileName = "CardAddQuantity_Data")]
public class CardAddQuantitySO : ScriptableObject
{
    /// <summary>
    /// So teammate ma card se cong cho Player khi kich hoat hop le.
    /// </summary>
    [SerializeField, Min(1)] private int teammateSpawnCount = 1;

    /// <summary>
    /// So teammate hop le duoc doc boi CardAddQuantity va Player.
    /// </summary>
    public int TeammateSpawnCount => teammateSpawnCount;

    /// <summary>
    /// Data duoc xem la hop le khi so teammate lon hon 0.
    /// </summary>
    public bool HasValidData => teammateSpawnCount > 0;

    private void OnValidate()
    {
        if (teammateSpawnCount < 1)
            teammateSpawnCount = 1;
    }
}
