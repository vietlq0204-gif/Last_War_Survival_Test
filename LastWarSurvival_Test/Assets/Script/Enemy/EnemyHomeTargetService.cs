using UnityEngine;

[DefaultExecutionOrder(-850)]
public sealed class EnemyHomeTargetService : MonoBehaviour
{
    private const string RuntimeObjectName = "__EnemyHomeTargetService";
    private const string FlowLogPrefix = "[HomeDamageFlow][Service]";

    private static EnemyHomeTargetService _instance;
    private EventBinder _binder;

    [SerializeField] private bool debugFlowLogs = false;

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

        Collider homeCollider = interactionEvent.homeCollider;
        Collider sourceCollider = interactionEvent.sourceCollider;
        Collider other = interactionEvent.otherCollider;
        if (other == null)
        {
            LogFlow("Ignored enter event because other collider is null.", warning: true);
            return;
        }

        LogFlow(
            $"Home enter detected. home='{homeCollider?.name ?? "<null>"}' homeLayer='{LayerMask.LayerToName(homeCollider != null ? homeCollider.gameObject.layer : -1)}' " +
            $"source='{sourceCollider?.name ?? "<null>"}' sourceLayer='{LayerMask.LayerToName(sourceCollider != null ? sourceCollider.gameObject.layer : -1)}' " +
            $"other='{other.name}'.");

        Enemy enemy = other.GetComponentInParent<Enemy>();
        if (enemy == null || !enemy.IsAlive || !enemy.CanInteractWithHomeCollider)
        {
            LogFlow(
                $"Ignored collider '{other.name}' because enemy resolve failed or enemy is inactive. " +
                $"enemy='{enemy?.name ?? "<null>"}' isAlive={enemy != null && enemy.IsAlive}.",
                warning: true);
            return;
        }

        int homeLayer = ResolveHomeImpactLayer(homeCollider, sourceCollider);
        LogFlow(
            $"Forwarding home impact to enemy='{enemy.name}#{enemy.CachedEntityId}' with collisionLayer={homeLayer} ('{LayerMask.LayerToName(homeLayer)}').",
            enemy);
        enemy.TryResolveHomeImpact(homeLayer);
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

    private static int ResolveHomeImpactLayer(Collider homeCollider, Collider sourceCollider)
    {
        if (homeCollider != null)
            return homeCollider.gameObject.layer;

        return sourceCollider != null ? sourceCollider.gameObject.layer : -1;
    }

    private void EnsureSceneBindings()
    {
        if (_binder == null && !TryGetComponent(out _binder))
            _binder = gameObject.AddComponent<EventBinder>();
    }

    private void LogFlow(string message, Object context = null, bool warning = false)
    {
        if (!debugFlowLogs)
            return;

        string formattedMessage = $"{FlowLogPrefix} {message}";
        Object resolvedContext = context != null ? context : this;
        if (warning)
            Debug.LogWarning(formattedMessage, resolvedContext);
        else
            Debug.Log(formattedMessage, resolvedContext);
    }
}
