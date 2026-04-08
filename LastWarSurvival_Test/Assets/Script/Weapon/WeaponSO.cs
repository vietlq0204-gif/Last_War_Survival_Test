using UnityEngine;
using UnityEngine.Serialization;
using Vit.SpawnKit.ScriptableObjects;

[CreateAssetMenu(menuName = "Gameplay/Weapon Data", fileName = "WeaponSO")]
public sealed class WeaponSO : ScriptableObject
{
    [FormerlySerializedAs("bulletSpawnable")]
    [SerializeField] private SpawnPresetSO bulletSpawnPreset;
    [SerializeField, Min(0.01f)] private float fireInterval = 0.15f;
    [SerializeField, Min(1)] private int bulletsPerTeammate = 1;
    [SerializeField, Min(0)] private int maxBulletsPerVolley;
    [SerializeField, Min(1)] private int maxActiveBullets = 128;

    public SpawnPresetSO BulletSpawnPreset => bulletSpawnPreset;
    public float FireInterval => Mathf.Max(0.01f, fireInterval);
    public int BulletsPerTeammate => Mathf.Max(1, bulletsPerTeammate);
    public int MaxBulletsPerVolley => Mathf.Max(0, maxBulletsPerVolley);
    public int MaxActiveBullets => Mathf.Max(1, maxActiveBullets);
    public bool IsValid => bulletSpawnPreset != null && bulletSpawnPreset.HasSpawnables;

    private void OnValidate()
    {
        if (fireInterval < 0.01f)
            fireInterval = 0.01f;

        if (bulletsPerTeammate < 1)
            bulletsPerTeammate = 1;

        if (maxBulletsPerVolley < 0)
            maxBulletsPerVolley = 0;

        if (maxActiveBullets < 1)
            maxActiveBullets = 1;
    }
}
