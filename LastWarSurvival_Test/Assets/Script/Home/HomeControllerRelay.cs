using UnityEngine;

[DisallowMultipleComponent]
public sealed class HomeControllerRelay : MonoBehaviour
{
    private HomeController _owner;
    private Collider _sourceCollider;

    public void Initialize(HomeController owner, Collider sourceCollider)
    {
        _owner = owner;
        _sourceCollider = sourceCollider;
    }

    private void OnTriggerEnter(Collider other)
    {
        _owner?.HandleTriggerFromRelay(_sourceCollider, other, HomeInteractionType.Enter);
    }

    private void OnTriggerExit(Collider other)
    {
        _owner?.HandleTriggerFromRelay(_sourceCollider, other, HomeInteractionType.Exit);
    }
}

