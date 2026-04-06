public class Enemy : ObjectSpawned
{
    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        SetControlledCollisionEnabled(true);
    }
}
