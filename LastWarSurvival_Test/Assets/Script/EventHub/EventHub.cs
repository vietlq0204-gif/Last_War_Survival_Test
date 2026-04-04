using System;
using UnityEngine;

public class EventHub<T> where T : class, new()
{
    private event Action<T> _onEvent;
    private readonly object _lock = new();

    public void Subscribe(Action<T> listener) { lock (_lock) _onEvent += listener; }

    public void Unsubscribe(Action<T> listener) { lock (_lock) _onEvent -= listener; }

    public void Subscribe(Action<T> listener, EventBinder binder)
    {
        lock (_lock) _onEvent += listener;
        binder.AddUnsubscriber(() => { lock (_lock) _onEvent -= listener; });
    }

    public void Raise(T args)
    {
        CoreEvents.LastEventName = typeof(T).Name;
        Action<T> snapshot;
        lock (_lock) snapshot = _onEvent;
        snapshot?.Invoke(args);
    }

    public void Raise()
    {
        CoreEvents.LastEventName = typeof(T).Name;
        Action<T> snapshot;
        lock (_lock) snapshot = _onEvent;
        snapshot?.Invoke(new T());
    }
}

public sealed class GameStartEvent
{
    public bool IsStarted { get; set; }

    public GameStartEvent()
    {
        IsStarted = !IsStarted;
    }
}

public struct ObjectSpawnInstruction
{
    public ObjectSpawned SpawnedObject { get; set; }
    public float MoveSpeed { get; set; }
    public float InitialDistance { get; set; }
    public EntityId SpawnZoneId { get; set; }
}

public struct ObjectReachedEndInfo
{
    public ObjectSpawned SpawnedObject { get; set; }
    public EntityId SpawnZoneId { get; set; }
}

public static class CoreEvents
{
    public static string LastEventName;

    public static readonly EventHub<GameStartEvent> gameStart = new EventHub<GameStartEvent>();
}
