using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TeammateController : CoreEventBase
{
    private const string FlowLogPrefix = "[HomeDamageFlow][TeammateController]";

    [Header("Team Health")]
    [SerializeField, Min(1)] private int healthPerTeammate = 1;
    [SerializeField, Min(1)] private int teammateDespawnBatchSize = 8;
    [SerializeField] private bool onlyUseDamageResponseLayerFilter = false;
    [SerializeField] private LayerMask damageResponseLayers = ~0;
    [SerializeField] private bool debugDamageFlowLogs = true;

    [Header("Weapon")]
    [SerializeField] private WeaponSO defaultGun;
    [SerializeField] private BulletSpawner bulletSpawner;

    private readonly List<Teammate> _activeTeammates = new List<Teammate>(128);
    private readonly HashSet<EntityId> _activeTeammateIds = new HashSet<EntityId>();
    private readonly Queue<Teammate> _pendingDamageDespawns = new Queue<Teammate>(64);
    private readonly HashSet<EntityId> _pendingDamageDespawnIds = new HashSet<EntityId>();

    private int _currentHealth;
    private int _pendingIncomingDamage;
    private WeaponSO _equippedWeapon;
    private bool _hasWarnedMissingBulletSpawner;

    public int CurrentHealth => Mathf.Max(0, _currentHealth);
    public int MaxHealth => MultiplyClamped(_activeTeammates.Count, ResolveHealthPerTeammate());
    public int ActiveTeammateCount => _activeTeammates.Count;
    public int PendingIncomingDamage => Mathf.Max(0, _pendingIncomingDamage);
    public WeaponSO EquippedWeapon => ResolveActiveWeapon();

    protected override void OnEnable()
    {
        base.OnEnable();
        ApplyEquippedWeapon();
    }

    private void Update()
    {
        ProcessPendingIncomingDamage();
        FlushQueuedTeammateDespawns();
    }

    private void OnDisable()
    {
        _activeTeammates.Clear();
        _activeTeammateIds.Clear();
        _pendingDamageDespawns.Clear();
        _pendingDamageDespawnIds.Clear();
        _currentHealth = 0;
        _pendingIncomingDamage = 0;
    }

    public override void SubscribeEvents()
    {
        LogFlow("Subscribing to enemyHomeDamageBatch and teammateWeaponPickup.");
        CoreEvents.enemyHomeDamageBatch.Subscribe(HandleEnemyHomeDamageBatchEvent, Binder);
        CoreEvents.teammateWeaponPickup.Subscribe(HandleTeammateWeaponPickupEvent, Binder);
    }

    public void RegisterSpawnedTeammate(Teammate teammate)
    {
        if (teammate == null)
            return;

        EntityId teammateId = teammate.CachedEntityId;
        if (!_activeTeammateIds.Add(teammateId))
            return;

        _pendingDamageDespawnIds.Remove(teammateId);
        _activeTeammates.Add(teammate);
        _currentHealth = Mathf.Max(0, _currentHealth) + ResolveHealthPerTeammate();
        ClampCurrentHealthToCapacity();
        LogFlow(
            $"Registered teammate='{teammate.name}#{teammateId}'. activeCount={_activeTeammates.Count} currentHealth={_currentHealth} maxHealth={MaxHealth}.",
            teammate);
    }

    public void NotifyTeammateDespawned(Teammate teammate)
    {
        if (teammate == null)
            return;

        EntityId teammateId = teammate.CachedEntityId;
        _pendingDamageDespawnIds.Remove(teammateId);

        if (!_activeTeammateIds.Remove(teammateId))
        {
            ClampCurrentHealthToCapacity();
            return;
        }

        for (int i = _activeTeammates.Count - 1; i >= 0; i--)
        {
            Teammate activeTeammate = _activeTeammates[i];
            if (activeTeammate == null || activeTeammate.CachedEntityId.Equals(teammateId))
                _activeTeammates.RemoveAt(i);
        }

        ClampCurrentHealthToCapacity();
        LogFlow(
            $"Notified teammate despawn teammate='{teammate.name}#{teammateId}'. activeCount={_activeTeammates.Count} currentHealth={_currentHealth} maxHealth={MaxHealth}.",
            teammate);
    }

    public void EquipWeapon(WeaponSO weapon)
    {
        if (weapon != null && !weapon.IsValid)
            return;

        _equippedWeapon = weapon;
        ApplyEquippedWeapon();
    }

    public bool QueueIncomingDamage(int damage)
    {
        if (damage <= 0)
            return false;

        _pendingIncomingDamage = AddClamped(_pendingIncomingDamage, damage);
        LogFlow(
            $"Queued incoming damage damage={damage} pendingIncomingDamage={_pendingIncomingDamage} activeCount={_activeTeammates.Count} currentHealth={_currentHealth}.",
            warning: true);
        return true;
    }

    private void HandleEnemyHomeDamageBatchEvent(EnemyHomeDamageBatchEvent damageBatchEvent)
    {
        if (damageBatchEvent == null || damageBatchEvent.totalDamage <= 0)
            return;

        bool canApplyDamage = CanDespawnFromCollisionLayer(damageBatchEvent.collisionLayer);
        LogFlow(
            $"Received EnemyHomeDamageBatchEvent totalDamage={damageBatchEvent.totalDamage} enemyHitCount={damageBatchEvent.enemyHitCount} " +
            $"collisionLayer={damageBatchEvent.collisionLayer} ('{LayerMask.LayerToName(damageBatchEvent.collisionLayer)}') " +
            $"onlyUseDamageResponseLayerFilter={onlyUseDamageResponseLayerFilter} damageResponseLayers={damageResponseLayers.value} " +
            $"canApplyDamage={canApplyDamage} activeCount={_activeTeammates.Count} currentHealth={_currentHealth}.",
            damageBatchEvent.sourceSpawner,
            warning: !canApplyDamage);

        if (!canApplyDamage)
            return;

        QueueIncomingDamage(damageBatchEvent.totalDamage);
    }

    private void HandleTeammateWeaponPickupEvent(TeammateWeaponPickupEvent pickupEvent)
    {
        if (pickupEvent == null || !pickupEvent.hasValidWeaponData)
            return;

        Teammate collector = pickupEvent.collector;
        if (collector == null || !_activeTeammateIds.Contains(collector.CachedEntityId))
            return;

        EquipWeapon(pickupEvent.weaponData);
    }

    private void ProcessPendingIncomingDamage()
    {
        if (_pendingIncomingDamage <= 0)
            return;

        if (_activeTeammates.Count <= 0)
        {
            LogFlow(
                $"Dropping pending damage because there are no active teammates. pendingIncomingDamage={_pendingIncomingDamage}.",
                warning: true);
            _pendingIncomingDamage = 0;
            _currentHealth = 0;
            return;
        }

        int damageToApply = _pendingIncomingDamage;
        _pendingIncomingDamage = 0;
        LogFlow($"Processing pending damage damageToApply={damageToApply} currentHealthBefore={_currentHealth} activeCount={_activeTeammates.Count}.", warning: true);
        ApplyIncomingDamage(damageToApply);
    }

    private void ApplyIncomingDamage(int damage)
    {
        if (damage <= 0 || _activeTeammates.Count <= 0)
            return;

        ClampCurrentHealthToCapacity();
        int currentHealthBefore = _currentHealth;
        _currentHealth = Mathf.Max(0, _currentHealth - damage);

        int healthUnit = ResolveHealthPerTeammate();
        int desiredAliveCount = _currentHealth <= 0
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(_currentHealth / (float)healthUnit), 0, _activeTeammates.Count);

        int despawnCount = Mathf.Max(0, _activeTeammates.Count - desiredAliveCount);
        LogFlow(
            $"Applied incoming damage damage={damage} currentHealthBefore={currentHealthBefore} currentHealthAfter={_currentHealth} " +
            $"healthPerTeammate={healthUnit} activeCountBefore={_activeTeammates.Count} desiredAliveCount={desiredAliveCount} despawnCount={despawnCount}.",
            warning: despawnCount > 0);
        QueueTeammatesForDamageDespawn(despawnCount);
        ClampCurrentHealthToCapacity();
    }

    private void QueueTeammatesForDamageDespawn(int count)
    {
        while (count > 0 && _activeTeammates.Count > 0)
        {
            int lastIndex = _activeTeammates.Count - 1;
            Teammate teammate = _activeTeammates[lastIndex];
            _activeTeammates.RemoveAt(lastIndex);

            if (teammate == null)
                continue;

            EntityId teammateId = teammate.CachedEntityId;
            _activeTeammateIds.Remove(teammateId);

            if (_pendingDamageDespawnIds.Add(teammateId))
            {
                _pendingDamageDespawns.Enqueue(teammate);
                LogFlow(
                    $"Queued teammate for despawn teammate='{teammate.name}#{teammateId}' pendingQueueCount={_pendingDamageDespawns.Count} remainingActiveCount={_activeTeammates.Count}.",
                    teammate,
                    warning: true);
            }

            count--;
        }
    }

    private void FlushQueuedTeammateDespawns()
    {
        int batchBudget = Mathf.Max(1, teammateDespawnBatchSize);
        while (batchBudget-- > 0 && _pendingDamageDespawns.Count > 0)
        {
            Teammate teammate = _pendingDamageDespawns.Dequeue();
            if (teammate == null)
                continue;

            if (!teammate.BeginQueuedDamageDespawn())
            {
                _pendingDamageDespawnIds.Remove(teammate.CachedEntityId);
                LogFlow(
                    $"BeginQueuedDamageDespawn returned false for teammate='{teammate.name}#{teammate.CachedEntityId}'.",
                    teammate,
                    warning: true);
                continue;
            }

            LogFlow(
                $"Sending teammate='{teammate.name}#{teammate.CachedEntityId}' to BufferedPoolDespawnQueue.",
                teammate,
                warning: true);
            BufferedPoolDespawnQueue.Queue(teammate);
        }
    }

    private void ClampCurrentHealthToCapacity()
    {
        _currentHealth = Mathf.Clamp(_currentHealth, 0, MaxHealth);
    }

    private int ResolveHealthPerTeammate()
    {
        return Mathf.Max(1, healthPerTeammate);
    }

    private bool CanDespawnFromCollisionLayer(int collisionLayer)
    {
        if (!onlyUseDamageResponseLayerFilter)
            return true;

        if (collisionLayer < 0 || collisionLayer > 31)
            return false;

        return (damageResponseLayers.value & (1 << collisionLayer)) != 0;
    }

    private void LogFlow(string message, Object context = null, bool warning = false)
    {
        if (!debugDamageFlowLogs)
            return;

        string formattedMessage = $"{FlowLogPrefix}[{name}] {message}";
        Object resolvedContext = context != null ? context : this;
        if (warning)
            Debug.LogWarning(formattedMessage, resolvedContext);
        else
            Debug.Log(formattedMessage, resolvedContext);
    }

    private void ApplyEquippedWeapon()
    {
        WeaponSO activeWeapon = ResolveActiveWeapon();
        BulletSpawner resolvedBulletSpawner = ResolveBulletSpawner();
        if (resolvedBulletSpawner == null)
        {
            if (_hasWarnedMissingBulletSpawner || activeWeapon == null)
                return;

            _hasWarnedMissingBulletSpawner = true;
            Debug.LogWarning($"'{name}' could not find a BulletSpawner to apply weapon '{activeWeapon.name}'.", this);
            return;
        }

        _hasWarnedMissingBulletSpawner = false;
        resolvedBulletSpawner.ApplyWeapon(activeWeapon);
    }

    private WeaponSO ResolveActiveWeapon()
    {
        if (_equippedWeapon != null && _equippedWeapon.IsValid)
            return _equippedWeapon;

        return defaultGun != null && defaultGun.IsValid ? defaultGun : null;
    }

    private BulletSpawner ResolveBulletSpawner()
    {
        if (bulletSpawner != null)
            return bulletSpawner;

        bulletSpawner = GetComponentInChildren<BulletSpawner>(true);
        if (bulletSpawner != null)
            return bulletSpawner;

        if (transform.parent != null)
            bulletSpawner = transform.parent.GetComponentInChildren<BulletSpawner>(true);

        return bulletSpawner;
    }

    private static int AddClamped(int currentValue, int delta)
    {
        if (delta <= 0)
            return Mathf.Max(0, currentValue);

        long total = (long)Mathf.Max(0, currentValue) + delta;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private static int MultiplyClamped(int value, int multiplier)
    {
        if (value <= 0 || multiplier <= 0)
            return 0;

        long total = (long)value * multiplier;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }
}
