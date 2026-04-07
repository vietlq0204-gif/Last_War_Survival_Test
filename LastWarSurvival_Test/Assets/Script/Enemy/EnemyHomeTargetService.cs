using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-850)]
public sealed class EnemyHomeTargetService : MonoBehaviour
{
    private const string RuntimeObjectName = "__EnemyHomeTargetService";
    private const string HomeTag = "Home";
    private const string PlayerTag = "Player";

    private static EnemyHomeTargetService _instance;

    private readonly List<Enemy> _activeChasers = new List<Enemy>(64);
    private readonly HashSet<EntityId> _activeChaserIds = new HashSet<EntityId>();

    private Collider _cachedHomeCollider;
    private Transform _cachedPlayerTarget;
    private EnemyHomeTargetRelay _cachedHomeRelay;

    public static void EnsureInitialized()
    {
        ResolveInstance().EnsureSceneBindings();
    }

    public static bool RegisterChasingEnemy(Enemy enemy)
    {
        if (enemy == null)
            return false;

        EnemyHomeTargetService instance = ResolveInstance();
        instance.EnsureSceneBindings();
        return instance.RegisterChasingEnemyInternal(enemy);
    }

    public static bool HasPlayerTarget()
    {
        EnemyHomeTargetService instance = ResolveInstance();
        instance.EnsureSceneBindings();
        return instance.ResolvePlayerTarget() != null;
    }

    public static void UnregisterChasingEnemy(Enemy enemy)
    {
        if (_instance == null || enemy == null)
            return;

        _instance.UnregisterChasingEnemyInternal(enemy);
    }

    private static EnemyHomeTargetService ResolveInstance()
    {
        if (_instance != null)
            return _instance;

        _instance = FindAnyObjectByType<EnemyHomeTargetService>();
        if (_instance != null)
            return _instance;

        var runtimeObject = new GameObject(RuntimeObjectName);
        DontDestroyOnLoad(runtimeObject);
        _instance = runtimeObject.AddComponent<EnemyHomeTargetService>();
        return _instance;
    }

    private void Update()
    {
        if (_activeChasers.Count == 0)
            return;

        Transform playerTarget = ResolvePlayerTarget();
        if (playerTarget == null)
            return;

        Vector3 targetPosition = playerTarget.position;
        float deltaTime = Time.deltaTime;

        for (int i = _activeChasers.Count - 1; i >= 0; i--)
        {
            Enemy enemy = _activeChasers[i];
            if (enemy == null
                || !enemy.gameObject.activeInHierarchy
                || !enemy.IsAlive
                || !enemy.IsChasingPlayer
                || !enemy.TickChasePlayer(targetPosition, deltaTime))
            {
                RemoveChasingEnemyAt(i);
            }
        }
    }

    private void OnDisable()
    {
        if (_instance == this)
            _instance = null;
    }

    public void HandleHomeTriggerEnterFromRelay(Collider sourceCollider, Collider other)
    {
        if (sourceCollider == null || other == null)
            return;

        Collider homeCollider = ResolveHomeCollider();
        if (homeCollider == null || sourceCollider != homeCollider)
            return;

        Enemy enemy = other.GetComponentInParent<Enemy>();
        if (enemy == null || !enemy.IsAlive || enemy.IsChasingPlayer)
            return;

        enemy.TryBeginHomeTargeting();
    }

    private bool RegisterChasingEnemyInternal(Enemy enemy)
    {
        Transform playerTarget = ResolvePlayerTarget();
        if (playerTarget == null || enemy == null || !enemy.IsAlive)
            return false;

        if (!_activeChaserIds.Add(enemy.CachedEntityId))
            return false;

        _activeChasers.Add(enemy);
        return true;
    }

    private void UnregisterChasingEnemyInternal(Enemy enemy)
    {
        if (enemy == null)
            return;

        EntityId enemyId = enemy.CachedEntityId;
        if (!_activeChaserIds.Remove(enemyId))
            return;

        for (int i = _activeChasers.Count - 1; i >= 0; i--)
        {
            Enemy activeEnemy = _activeChasers[i];
            if (activeEnemy == null || activeEnemy.CachedEntityId.Equals(enemyId))
                RemoveChasingEnemyAt(i);
        }
    }

    private void RemoveChasingEnemyAt(int index)
    {
        int lastIndex = _activeChasers.Count - 1;
        Enemy removedEnemy = _activeChasers[index];
        if (removedEnemy != null)
            _activeChaserIds.Remove(removedEnemy.CachedEntityId);

        _activeChasers[index] = _activeChasers[lastIndex];
        _activeChasers.RemoveAt(lastIndex);
    }

    private void EnsureSceneBindings()
    {
        EnsureHomeRelay();
        ResolvePlayerTarget();
    }

    private void EnsureHomeRelay()
    {
        Collider homeCollider = ResolveHomeCollider();
        if (homeCollider == null)
            return;

        if (!homeCollider.TryGetComponent(out _cachedHomeRelay))
            _cachedHomeRelay = homeCollider.gameObject.AddComponent<EnemyHomeTargetRelay>();

        _cachedHomeRelay.Initialize(this, homeCollider);

        if (!homeCollider.TryGetComponent(out Rigidbody homeRigidbody))
        {
            homeRigidbody = homeCollider.gameObject.AddComponent<Rigidbody>();
            homeRigidbody.isKinematic = true;
            homeRigidbody.useGravity = false;
        }
    }

    private Collider ResolveHomeCollider()
    {
        if (_cachedHomeCollider != null
            && _cachedHomeCollider.gameObject.activeInHierarchy)
        {
            return _cachedHomeCollider;
        }

        GameObject homeObject = GameObject.FindGameObjectWithTag(HomeTag);
        if (homeObject == null)
        {
            _cachedHomeCollider = null;
            return null;
        }

        _cachedHomeCollider = homeObject.GetComponent<Collider>();
        if (_cachedHomeCollider == null)
            _cachedHomeCollider = homeObject.GetComponentInChildren<Collider>(true);

        return _cachedHomeCollider;
    }

    private Transform ResolvePlayerTarget()
    {
        if (_cachedPlayerTarget != null && _cachedPlayerTarget.gameObject.activeInHierarchy)
            return _cachedPlayerTarget;

        GameObject playerObject = GameObject.FindGameObjectWithTag(PlayerTag);
        _cachedPlayerTarget = playerObject != null ? playerObject.transform : null;
        return _cachedPlayerTarget;
    }
}
