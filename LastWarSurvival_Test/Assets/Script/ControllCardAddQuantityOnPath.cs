public class ControllCardAddQuantityOnPath : ControllObjectOnPath
{
    protected override bool AcceptSpawnedObject(ObjectSpawned spawnedObject)
    {
        return spawnedObject is CardAddQuantity;
    }
}
