namespace Vit.SpawnKit.Data
{
/// <summary>
/// Contract for objects or systems that can provide a spawn request.
/// </summary>
public interface ISpawnRequestSource
{
    SpawnRequest CreateSpawnRequest();
}
}
