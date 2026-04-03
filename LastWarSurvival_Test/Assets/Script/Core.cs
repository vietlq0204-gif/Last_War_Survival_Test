using UnityEngine;

public class Core : CoreEventBase
{
    void Start()
    {
        CoreEvents.gameStart.Raise();
    }


    public override void SubscribeEvents()
    {
        
    }
}
