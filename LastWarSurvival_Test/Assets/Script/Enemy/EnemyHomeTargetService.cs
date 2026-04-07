using UnityEngine;

[DefaultExecutionOrder(-850)]
public sealed class EnemyHomeTargetService : MonoBehaviour
{
    private const string RuntimeObjectName = "__EnemyHomeTargetService";
    private const string HomeTag = "Home";

    private static EnemyHomeTargetService _instance;

    private Collider _cachedHomeCollider;
    private EnemyHomeTargetRelay _cachedHomeRelay;

    public static void EnsureInitialized()
    {
        ResolveInstance().EnsureSceneBindings();
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
        if (enemy == null || !enemy.IsAlive || !enemy.CanInteractWithHomeCollider)
            return;

        enemy.TryResolveHomeImpact(sourceCollider.gameObject.layer);
    }

    private void EnsureSceneBindings()
    {
        EnsureHomeRelay();
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
}
