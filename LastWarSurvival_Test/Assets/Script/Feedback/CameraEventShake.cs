using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(DamageShakeFeedback))]
public sealed class CameraEventShake : CoreEventBase
{
    [SerializeField] private DamageShakeFeedback shakeFeedback;
    [SerializeField, Range(0f, 1f)] private float obstacleDespawnTrauma = 0.18f;
    [SerializeField, Range(0f, 1f)] private float enemyDamageTrauma = 0.28f;
    [SerializeField] private bool scaleEnemyDamageByTotalDamage = true;
    [SerializeField, Min(1)] private int damagePerExtraTraumaStep = 2;
    [SerializeField, Range(0f, 2f)] private float maxEnemyDamageTraumaMultiplier = 1.75f;

    protected override void Awake()
    {
        base.Awake();
        if (shakeFeedback == null)
            shakeFeedback = GetComponent<DamageShakeFeedback>();
    }

    public override void SubscribeEvents()
    {
        CoreEvents.obstacleDespawned.Subscribe(HandleObstacleDespawnedEvent, Binder);
        CoreEvents.enemyHomeDamageBatch.Subscribe(HandleEnemyHomeDamageBatchEvent, Binder);
    }

    private void HandleObstacleDespawnedEvent(ObstacleDespawnedEvent obstacleEvent)
    {
        if (obstacleEvent == null)
            return;

        ResolveShakeFeedback()?.PlayShake(obstacleDespawnTrauma);
    }

    private void HandleEnemyHomeDamageBatchEvent(EnemyHomeDamageBatchEvent damageBatchEvent)
    {
        if (damageBatchEvent == null || damageBatchEvent.totalDamage <= 0)
            return;

        float trauma = enemyDamageTrauma;
        if (scaleEnemyDamageByTotalDamage)
        {
            int safeStep = Mathf.Max(1, damagePerExtraTraumaStep);
            float multiplier = Mathf.Clamp(
                damageBatchEvent.totalDamage / (float)safeStep,
                1f,
                Mathf.Max(1f, maxEnemyDamageTraumaMultiplier));
            trauma *= multiplier;
        }

        ResolveShakeFeedback()?.PlayShake(trauma);
    }

    private DamageShakeFeedback ResolveShakeFeedback()
    {
        if (shakeFeedback == null)
            shakeFeedback = GetComponent<DamageShakeFeedback>();

        return shakeFeedback;
    }
}
