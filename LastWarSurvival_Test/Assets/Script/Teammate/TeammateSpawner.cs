using System.Collections.Generic;
using UnityEngine;

public class TeammateSpawner : SpawnGridQueue
{
    [Header("Team Health")]
    [SerializeField, Min(1)] private int healthPerTeammate = 1;
    [SerializeField, Min(1)] private int teammateDespawnBatchSize = 8;
    [SerializeField] private LayerMask damageResponseLayers = ~0;

    private readonly List<Teammate> _activeTeammates = new List<Teammate>(128);
    private readonly HashSet<EntityId> _activeTeammateIds = new HashSet<EntityId>();
    private readonly Queue<Teammate> _pendingDamageDespawns = new Queue<Teammate>(64);
    private readonly HashSet<EntityId> _pendingDamageDespawnIds = new HashSet<EntityId>();

    private int _currentHealth;
    private int _pendingIncomingDamage;

    protected override string SpawnedObjectLabel => "teammate";

    public int CurrentHealth => Mathf.Max(0, _currentHealth);
    public int MaxHealth => MultiplyClamped(_activeTeammates.Count, ResolveHealthPerTeammate());
    public int ActiveTeammateCount => _activeTeammates.Count;
    public int PendingIncomingDamage => Mathf.Max(0, _pendingIncomingDamage);

    protected override void OnEnable()
    {
        base.OnEnable();
    }

    private void Update()
    {
        ProcessPendingIncomingDamage();
        FlushQueuedTeammateDespawns();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        _activeTeammates.Clear();
        _activeTeammateIds.Clear();
        _pendingDamageDespawns.Clear();
        _pendingDamageDespawnIds.Clear();
        _currentHealth = 0;
        _pendingIncomingDamage = 0;
    }

    public override void SubscribeEvents()
    {
        base.SubscribeEvents();
        CoreEvents.collition.Subscribe(HandleCollitionEvent, Binder);
        CoreEvents.enemyHomeDamageBatch.Subscribe(HandleEnemyHomeDamageBatchEvent, Binder);
    }

    private void HandleCollitionEvent(CollitionEvent collitionEvent)
    {
        if (collitionEvent == null) return;
        if (collitionEvent.collitionType != CollitionEvent.CollitionType.Trigger) return;
        if (collitionEvent.collitionTag != CollitionEvent.CollitionTag.Player) return;

        var cardData = collitionEvent.cardAddQuantityData;
        if (cardData == null || !cardData.HasValidData) return;

        QueueSpawn(cardData.TeammateSpawnCount);
    }

    private void HandleEnemyHomeDamageBatchEvent(EnemyHomeDamageBatchEvent damageBatchEvent)
    {
        if (damageBatchEvent == null || damageBatchEvent.totalDamage <= 0)
            return;

        if (!CanDespawnFromCollisionLayer(damageBatchEvent.collisionLayer))
            return;

        QueueIncomingDamage(damageBatchEvent.totalDamage);
    }

    public override int GetOccupiedSlotCount()
    {
        return _activeTeammates.Count;
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
    }

    public bool QueueIncomingDamage(int damage)
    {
        if (damage <= 0)
            return false;

        _pendingIncomingDamage = AddClamped(_pendingIncomingDamage, damage);
        return true;
    }

    private void ProcessPendingIncomingDamage()
    {
        if (_pendingIncomingDamage <= 0)
            return;

        if (_activeTeammates.Count <= 0)
        {
            _pendingIncomingDamage = 0;
            _currentHealth = 0;
            return;
        }

        int damageToApply = _pendingIncomingDamage;
        _pendingIncomingDamage = 0;
        ApplyIncomingDamage(damageToApply);
    }

    private void ApplyIncomingDamage(int damage)
    {
        if (damage <= 0 || _activeTeammates.Count <= 0)
            return;

        ClampCurrentHealthToCapacity();
        _currentHealth = Mathf.Max(0, _currentHealth - damage);

        int healthUnit = ResolveHealthPerTeammate();
        int desiredAliveCount = _currentHealth <= 0
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(_currentHealth / (float)healthUnit), 0, _activeTeammates.Count);

        int despawnCount = Mathf.Max(0, _activeTeammates.Count - desiredAliveCount);
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
                _pendingDamageDespawns.Enqueue(teammate);

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
                continue;
            }

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
        if (collisionLayer < 0 || collisionLayer > 31)
            return false;

        return (damageResponseLayers.value & (1 << collisionLayer)) != 0;
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
