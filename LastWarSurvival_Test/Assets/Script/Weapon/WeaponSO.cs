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
    [Header("Teammate Visual")]
    [SerializeField] private GameObject teammateVisualPrefab;
    [SerializeField] private bool hideDefaultTeammateModelWhenEquipped;
    [SerializeField] private Vector3 teammateVisualLocalPosition;
    [SerializeField] private Vector3 teammateVisualLocalEulerAngles;
    [SerializeField] private Vector3 teammateVisualLocalScale = Vector3.one;

    public SpawnPresetSO BulletSpawnPreset => bulletSpawnPreset;
    public float FireInterval => Mathf.Max(0.01f, fireInterval);
    public int BulletsPerTeammate => Mathf.Max(1, bulletsPerTeammate);
    public int MaxBulletsPerVolley => Mathf.Max(0, maxBulletsPerVolley);
    public int MaxActiveBullets => Mathf.Max(1, maxActiveBullets);
    public GameObject TeammateVisualPrefab => teammateVisualPrefab;
    public bool HideDefaultTeammateModelWhenEquipped => hideDefaultTeammateModelWhenEquipped;
    public Vector3 TeammateVisualLocalPosition => teammateVisualLocalPosition;
    public Quaternion TeammateVisualLocalRotation => Quaternion.Euler(teammateVisualLocalEulerAngles);
    public Vector3 TeammateVisualLocalScale => ResolveSafeVisualScale(teammateVisualLocalScale);
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

        teammateVisualLocalScale = ResolveSafeVisualScale(teammateVisualLocalScale);
    }

    private static Vector3 ResolveSafeVisualScale(Vector3 source)
    {
        return new Vector3(
            Mathf.Approximately(source.x, 0f) ? 1f : source.x,
            Mathf.Approximately(source.y, 0f) ? 1f : source.y,
            Mathf.Approximately(source.z, 0f) ? 1f : source.z);
    }
}
