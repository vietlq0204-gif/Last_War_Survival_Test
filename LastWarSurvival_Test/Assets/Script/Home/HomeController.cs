using UnityEngine;

[DisallowMultipleComponent]
public sealed class HomeController : MonoBehaviour
{
    private const string HomeTag = "Home";

    [SerializeField] private Collider homeCollider;

    public delegate void HomeInteractionHandler(HomeInteractionEvent interactionEvent);

    public event HomeInteractionHandler Interaction;

    public Collider HomeCollider => homeCollider;

    private void Reset()
    {
        AutoAssignHomeCollider();
    }

    private void Awake()
    {
        AutoAssignHomeCollider();
        EnsureRelayBindings();
        EnsureRigidbody();
    }

    private void OnEnable()
    {
        EnsureRelayBindings();
        EnsureRigidbody();
    }

    private void OnValidate()
    {
        AutoAssignHomeCollider();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (homeCollider == null || homeCollider.gameObject != gameObject)
            return;

        Dispatch(homeCollider, other, HomeInteractionType.Enter);
    }

    private void OnTriggerExit(Collider other)
    {
        if (homeCollider == null || homeCollider.gameObject != gameObject)
            return;

        Dispatch(homeCollider, other, HomeInteractionType.Exit);
    }

    public void HandleTriggerFromRelay(Collider sourceCollider, Collider other, HomeInteractionType interactionType)
    {
        if (sourceCollider == null || other == null)
            return;

        if (!IsManagedHomeCollider(sourceCollider))
            return;

        Dispatch(sourceCollider, other, interactionType);
    }

    public bool IsInside(Transform targetRoot)
    {
        if (homeCollider == null || targetRoot == null || !targetRoot.gameObject.activeInHierarchy)
            return false;

        Collider targetCollider = ResolvePrimaryCollider(targetRoot);
        if (targetCollider == null)
            return homeCollider.bounds.Contains(targetRoot.position);

        return Physics.ComputePenetration(
            homeCollider,
            homeCollider.transform.position,
            homeCollider.transform.rotation,
            targetCollider,
            targetCollider.transform.position,
            targetCollider.transform.rotation,
            out _,
            out _);
    }

    private void Dispatch(Collider sourceCollider, Collider other, HomeInteractionType interactionType)
    {
        if (homeCollider == null || other == null)
            return;

        var interactionEvent = new HomeInteractionEvent(
            this,
            homeCollider,
            sourceCollider,
            other,
            interactionType);

        Interaction?.Invoke(interactionEvent);
        CoreEvents.homeInteraction.Raise(interactionEvent);
    }

    private void AutoAssignHomeCollider()
    {
        if (homeCollider != null)
            return;

        if (TryGetComponent(out Collider localCollider) && localCollider.CompareTag(HomeTag))
        {
            homeCollider = localCollider;
            return;
        }

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];
            if (candidate == null)
                continue;

            if (candidate.CompareTag(HomeTag))
            {
                homeCollider = candidate;
                return;
            }
        }

        if (TryGetComponent(out localCollider))
            homeCollider = localCollider;
    }

    private void EnsureRelayBindings()
    {
        if (homeCollider == null)
            return;

        RegisterRelay(homeCollider);

        Collider[] colliders = homeCollider.transform.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            RegisterRelay(colliders[i]);
        }
    }

    private void RegisterRelay(Collider candidate)
    {
        if (candidate == null || candidate.gameObject == gameObject)
            return;

        if (!IsManagedHomeCollider(candidate))
            return;

        if (!candidate.TryGetComponent(out HomeControllerRelay relay))
            relay = candidate.gameObject.AddComponent<HomeControllerRelay>();

        relay.Initialize(this, candidate);
    }

    private void EnsureRigidbody()
    {
        if (homeCollider == null)
            return;

        if (!homeCollider.TryGetComponent(out Rigidbody homeRigidbody))
        {
            homeRigidbody = homeCollider.gameObject.AddComponent<Rigidbody>();
            homeRigidbody.isKinematic = true;
            homeRigidbody.useGravity = false;
            return;
        }

        homeRigidbody.isKinematic = true;
        homeRigidbody.useGravity = false;
    }

    private bool IsManagedHomeCollider(Collider candidate)
    {
        if (candidate == null || homeCollider == null)
            return false;

        if (candidate == homeCollider)
            return true;

        if (candidate.CompareTag(HomeTag))
            return true;

        return candidate.transform.IsChildOf(homeCollider.transform);
    }

    private static Collider ResolvePrimaryCollider(Transform root)
    {
        if (root == null)
            return null;

        if (root.TryGetComponent(out Collider rootCollider))
            return rootCollider;

        return root.GetComponentInChildren<Collider>(true);
    }
}
