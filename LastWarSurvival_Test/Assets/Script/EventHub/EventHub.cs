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

public sealed class GameOverEvent
{
    public bool IsGameOver { get; set; }

    public GameOverEvent()
    {
        IsGameOver = true;
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

public sealed class EnemyHomeDamageBatchEvent
{
    public EnemySpawner sourceSpawner { get; set; }
    public int totalDamage { get; set; }
    public int enemyHitCount { get; set; }
    public int collisionLayer { get; set; }

    public EnemyHomeDamageBatchEvent()
    {
        sourceSpawner = null;
        totalDamage = 0;
        enemyHitCount = 0;
        collisionLayer = -1;
    }

    public EnemyHomeDamageBatchEvent(EnemySpawner sourceSpawner, int totalDamage, int enemyHitCount, int collisionLayer)
    {
        this.sourceSpawner = sourceSpawner;
        this.totalDamage = totalDamage;
        this.enemyHitCount = enemyHitCount;
        this.collisionLayer = collisionLayer;
    }
}

public sealed class TeammateWeaponPickupEvent
{
    public Teammate collector { get; set; }
    public WeaponSO weaponData { get; set; }
    public WeaponPickupItem sourcePickup { get; set; }

    public bool hasValidWeaponData => collector != null && weaponData != null && weaponData.IsValid;

    public TeammateWeaponPickupEvent()
    {
        collector = null;
        weaponData = null;
        sourcePickup = null;
    }

    public TeammateWeaponPickupEvent(Teammate collector, WeaponSO weaponData, WeaponPickupItem sourcePickup)
    {
        this.collector = collector;
        this.weaponData = weaponData;
        this.sourcePickup = sourcePickup;
    }
}

public enum HomeInteractionType
{
    Enter = 0,
    Exit = 1,
}

public sealed class HomeInteractionEvent
{
    public HomeController controller { get; set; }
    public Collider homeCollider { get; set; }
    public Collider sourceCollider { get; set; }
    public Collider otherCollider { get; set; }
    public HomeInteractionType interactionType { get; set; }

    public bool isEnter => interactionType == HomeInteractionType.Enter;
    public bool isExit => interactionType == HomeInteractionType.Exit;

    public HomeInteractionEvent()
    {
        controller = null;
        homeCollider = null;
        sourceCollider = null;
        otherCollider = null;
        interactionType = HomeInteractionType.Enter;
    }

    public HomeInteractionEvent(
        HomeController controller,
        Collider homeCollider,
        Collider sourceCollider,
        Collider otherCollider,
        HomeInteractionType interactionType)
    {
        this.controller = controller;
        this.homeCollider = homeCollider;
        this.sourceCollider = sourceCollider;
        this.otherCollider = otherCollider;
        this.interactionType = interactionType;
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
    /// Event ket thuc game khi doi teammate khong con ai song.
    /// </summary>
    public static readonly EventHub<GameOverEvent> gameOver = new EventHub<GameOverEvent>();

    /// <summary>
    /// Event va cham tong hop, hien tai duoc dung cho luong CardAddQuantity -> Player.
    /// </summary>
    public static readonly EventHub<CollitionEvent> collition = new EventHub<CollitionEvent>();

    /// <summary>
    /// Event batch khi enemy cham Home va da duoc tong hop damage theo EnemySpawner.
    /// </summary>
    public static readonly EventHub<EnemyHomeDamageBatchEvent> enemyHomeDamageBatch = new EventHub<EnemyHomeDamageBatchEvent>();

    /// <summary>
    /// Event khi pickup weapon duoc teammate nhat.
    /// </summary>
    public static readonly EventHub<TeammateWeaponPickupEvent> teammateWeaponPickup =
        new EventHub<TeammateWeaponPickupEvent>();

    /// <summary>
    /// Event trigger tong hop cua Home de cac he thong khac cung consume.
    /// </summary>
    public static readonly EventHub<HomeInteractionEvent> homeInteraction =
        new EventHub<HomeInteractionEvent>();
}
