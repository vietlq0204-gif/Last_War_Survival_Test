public class EnemyGridGroupOnPathController : ObjectOnPathController
{
    protected override bool AcceptSpawnedObject(ObjectSpawned spawnedObject)
    {
        return spawnedObject is EnemyGridPathItem;
    }
}
