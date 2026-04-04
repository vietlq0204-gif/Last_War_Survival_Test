using System;
using UnityEngine;

public class CardAddQuantity : ObjectSpawned
{
    // CardAddQuantitySO cấu hình data cho CardAddQuantity
            // vd: int valueCard = 1;
    
    public void SetRoadCollisionEnabled(bool enabled)
    {
        SetControlledCollisionEnabled(enabled);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // raise event kèm CardAddQuantitySO
        }
    }
}
