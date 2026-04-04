public class CardAddQuantityOnPathController : ObjectOnPathController
{
    protected override bool AcceptSpawnedObject(ObjectSpawned spawnedObject)
    {
        return spawnedObject is CardAddQuantity;
    }
}
