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

public sealed class CollitionEvent
{
    public enum CollitionType
    {
        Trigger = 0,
        Collider = 1
    }

    public enum CollitionTag
    {
        Player = 0,
        Enemey = 1,
        Obstacle
    }

    /// <summary>
    /// Kieu va cham da xay ra.
    /// </summary>
    public CollitionType collitionType { get; set; }

    /// <summary>
    /// Tag logic cua doi tuong nhan va cham.
    /// </summary>
    public CollitionTag collitionTag { get; set; }

    /// <summary>
    /// Du lieu ScriptableObject cua CardAddQuantity duoc gui kem theo event.
    /// </summary>
    public CardAddQuantitySO cardAddQuantityData { get; set; }

    /// <summary>
    /// Trang thai hop le de Player xu ly spawn.
    /// </summary>
    public bool hasValidCardAddQuantityData =>
        cardAddQuantityData != null
        && cardAddQuantityData.HasValidData;

    /// <summary>
    /// Tao event rong de support EventHub.Raise() khi can.
    /// </summary>
    public CollitionEvent()
    {
        collitionType = CollitionType.Trigger;
        collitionTag = CollitionTag.Player;
        cardAddQuantityData = null;
    }

    /// <summary>
    /// Tao event va cham day du data de Player co the xu ly spawn teammate.
    /// </summary>
    public CollitionEvent(
        CollitionType collitionType,
        CollitionTag collitionTag,
        CardAddQuantitySO cardAddQuantityData)
    {
        this.collitionType = collitionType;
        this.collitionTag = collitionTag;
        this.cardAddQuantityData = cardAddQuantityData;
    }
}

public static class CoreEvents
{
    public static string LastEventName;

    /// <summary>
    /// Event bat dau game.
    /// </summary>
    public static readonly EventHub<GameStartEvent> gameStart = new EventHub<GameStartEvent>();

    /// <summary>
    /// Event va cham tong hop, hien tai duoc dung cho luong CardAddQuantity -> Player.
    /// </summary>
    public static readonly EventHub<CollitionEvent> collition = new EventHub<CollitionEvent>();
}
