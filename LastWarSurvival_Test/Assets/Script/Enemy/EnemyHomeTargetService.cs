using UnityEngine;

[DefaultExecutionOrder(-850)]
public sealed class EnemyHomeTargetService : MonoBehaviour
{
    private const string RuntimeObjectName = "__EnemyHomeTargetService";

    private static EnemyHomeTargetService _instance;
    private EventBinder _binder;

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

    private void Awake()
    {
        if (_binder == null && !TryGetComponent(out _binder))
            _binder = gameObject.AddComponent<EventBinder>();
    }

    private void OnEnable()
    {
        if (_binder == null && !TryGetComponent(out _binder))
            _binder = gameObject.AddComponent<EventBinder>();

        CoreEvents.homeInteraction.Subscribe(HandleHomeInteractionEvent, _binder);
    }

    private void OnDisable()
    {
        if (_instance == this)
            _instance = null;
    }

    private void HandleHomeInteractionEvent(HomeInteractionEvent interactionEvent)
    {
        if (interactionEvent == null || !interactionEvent.isEnter)
            return;

        Collider sourceCollider = interactionEvent.sourceCollider;
        Collider other = interactionEvent.otherCollider;
        if (sourceCollider == null || other == null)
            return;

        Enemy enemy = other.GetComponentInParent<Enemy>();
        if (enemy == null || !enemy.IsAlive || !enemy.CanInteractWithHomeCollider)
            return;

        enemy.TryResolveHomeImpact(sourceCollider.gameObject.layer);
    }

    public void HandleHomeTriggerEnterFromRelay(Collider sourceCollider, Collider other)
    {
        HandleHomeInteractionEvent(new HomeInteractionEvent(
            null,
            sourceCollider,
            sourceCollider,
            other,
            HomeInteractionType.Enter));
    }

    private void EnsureSceneBindings()
    {
        if (_binder == null && !TryGetComponent(out _binder))
            _binder = gameObject.AddComponent<EventBinder>();
    }
}
