using UnityEngine;
using Vit.SpawnKit.Api;

public class CardAddQuantity : ObjectSpawned
{
    /// <summary>
    /// ScriptableObject chua data gameplay cua card.
    /// </summary>
    [Header("Cau hinh thuong tu card")]
    [SerializeField] private CardAddQuantitySO cardData;

    /// <summary>
    /// Chan card phat event nhieu lan khi player con nam trong trigger.
    /// </summary>
    private bool _hasRaisedValidCollision;

    /// <summary>
    /// Danh dau card dang doi Player roi khoi trigger de tra ve pool ngay.
    /// </summary>
    private bool _isWaitingForPlayerExit;

    /// <summary>
    /// Ten tag dung de nhan dien collider Player.
    /// </summary>
    private const string PlayerTag = "Player";

    /// <summary>
    /// Data ScriptableObject cua card hien tai.
    /// </summary>
    public CardAddQuantitySO CardData => cardData;

    /// <summary>
    /// So teammate card se cong them cho Player.
    /// </summary>
    public int TeammateSpawnCount => cardData != null ? cardData.TeammateSpawnCount : 0;

    /// <summary>
    /// Du lieu card hop le khi da gan SO va SO co count hop le.
    /// </summary>
    public bool HasValidData => cardData != null && cardData.HasValidData;

    protected override void Awake()
    {
        base.Awake();
        EnsureCollisionRelays();
    }
    
    private void OnTriggerEnter(Collider other)
    {
        HandleTriggerEnter(other);
    }

    private void OnTriggerExit(Collider other)
    {
        
        HandleTriggerExit(other);
    }

    /// <summary>
    /// Reset trang thai moi khi card duoc lay ra tu pool de tiep tuc chay tren road.
    /// </summary>
    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        _hasRaisedValidCollision = false;
        _isWaitingForPlayerExit = false;
        SetRoadCollisionEnabled(true);
    }

    /// <summary>
    /// Don trang thai runtime truoc khi card quay lai pool.
    /// </summary>
    public override void OnDespawnedToPool()
    {
        _hasRaisedValidCollision = false;
        _isWaitingForPlayerExit = false;
        base.OnDespawnedToPool();
    }

    /// <summary>
    /// Bat hoac tat va cham cua card trong luc card dang chay tren road.
    /// </summary>
    public void SetRoadCollisionEnabled(bool enabled)
    {
        SetControlledCollisionEnabled(enabled);
    }

    /// <summary>
    /// Ham xu ly trigger chung cho ca root collider va collider relay o child.
    /// </summary>
    public void HandleTriggerEnter(Collider other)
    {
        if (_hasRaisedValidCollision || other == null) return;
        if (!TryBuildCollitionEvent(other, out var collitionEvent)) return;

        _hasRaisedValidCollision = true;
        _isWaitingForPlayerExit = true;
        // Debug.Log(
        //     $"CardAddQuantity '{name}' va cham Player, raise CollitionEvent voi data '{cardData.name}' (spawn {cardData.TeammateSpawnCount}).",
        //     this);
        CoreEvents.collition.Raise(collitionEvent);
    }

    /// <summary>
    /// Khi Player roi trigger, card se duoc tra ve pool ngay thay vi doi den cuoi path.
    /// </summary>
    public void HandleTriggerExit(Collider other)
    {
        if (!_isWaitingForPlayerExit || other == null) return;
        if (!IsPlayerCollider(other)) return;

        _isWaitingForPlayerExit = false;
        // Debug.Log($"CardAddQuantity '{name}' roi trigger Player, yeu cau tra ve pool ngay.", this);

        if (TryReleaseFromPathController())
            return;

        if (SpawnKit.Despawn(gameObject))
            return;

        Debug.LogWarning($"CardAddQuantity '{name}' khong the tra ve pool sau khi roi Player.", this);
    }

    /// <summary>
    /// Dung CollitionEvent hop le tu collider Player neu card dang co data hop le.
    /// </summary>
    private bool TryBuildCollitionEvent(Collider other, out CollitionEvent collitionEvent)
    {
        collitionEvent = null;
        if (!HasValidData) return false;
        if (!IsPlayerCollider(other)) return false;

        collitionEvent = new CollitionEvent(
            CollitionEvent.CollitionType.Trigger,
            CollitionEvent.CollitionTag.Player,
            cardData);
        return collitionEvent.hasValidCardAddQuantityData;
    }

    /// <summary>
    /// Kiem tra collider vua va cham co phai Player theo tag hay khong.
    /// </summary>
    private bool IsPlayerCollider(Collider other)
    {
        return other != null && other.CompareTag("Player");
    }

    /// <summary>
    /// Dam bao collider o child co relay de forward trigger ve root card.
    /// </summary>
    private void EnsureCollisionRelays()
    {
        var colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || collider.gameObject == gameObject) continue;

            if (!collider.TryGetComponent(out CardAddQuantityCollisionRelay relay))
                relay = collider.gameObject.AddComponent<CardAddQuantityCollisionRelay>();

            relay.Initialize(this);
        }
    }
}
