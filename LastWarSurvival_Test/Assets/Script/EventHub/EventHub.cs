using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class EventHub<T> where T : class, new()
{
    private event Action<T> _onEvent;
    private readonly object _lock = new();

    public void Subscribe(Action<T> listener) { lock (_lock) _onEvent += listener; }

    public void Unsubscribe(Action<T> listener) { lock (_lock) _onEvent -= listener; }

    public void Subscribe(Action<T> l, EventBinder b)
    {
        lock (_lock) _onEvent += l;
        b.AddUnsubscriber(() => { lock (_lock) _onEvent -= l; });
    }

    public void Raise(T args)
    {
        CoreEvents.LastEventName = typeof(T).Name;
        Action<T> snapshot;
        lock (_lock) snapshot = _onEvent;
        snapshot?.Invoke(args);
    }

    // cần fix, có thể bỏ đi sau này vì nó bắt buộc new T() -> GC
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


/// <summary>
/// Centralized Event 
/// </summary>
public static class CoreEvents
{
    public static string LastEventName;

    public static readonly EventHub<GameStartEvent> gameStart = new EventHub<GameStartEvent>();
    
}