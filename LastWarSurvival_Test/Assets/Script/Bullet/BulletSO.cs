using UnityEngine;

[CreateAssetMenu(menuName = "Gameplay/Bullet Data", fileName = "BulletSO")]
public sealed class BulletSO : ScriptableObject
{
    [SerializeField, Min(0)] private int damage = 1;

    public int Damage => damage;

    private void OnValidate()
    {
        if (damage < 0)
            damage = 0;
    }
}
