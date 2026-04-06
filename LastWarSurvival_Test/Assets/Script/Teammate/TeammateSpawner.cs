public class TeammateSpawner : SpawnGridQueue
{
    protected override string SpawnedObjectLabel => "teammate";

    public override void SubscribeEvents()
    {
        base.SubscribeEvents();
        CoreEvents.collition.Subscribe(HandleCollitionEvent, Binder);
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
}
