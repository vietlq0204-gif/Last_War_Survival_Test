using UnityEngine;

/// <summary>
/// Relay trigger tu collider con ve CardAddQuantity o root.
/// </summary>
public class CardAddQuantityCollisionRelay : MonoBehaviour
{
    /// <summary>
    /// Tham chieu ve CardAddQuantity goc de forward trigger dung doi tuong.
    /// </summary>
    private CardAddQuantity _owner;

    /// <summary>
    /// Gan card root de relay trigger dung doi tuong.
    /// </summary>
    public void Initialize(CardAddQuantity owner)
    {
        _owner = owner;
    }

    private void OnTriggerEnter(Collider other)
    {
        _owner?.HandleTriggerEnter(other);
    }

    private void OnTriggerExit(Collider other)
    {
        _owner?.HandleTriggerExit(other);
    }
}
