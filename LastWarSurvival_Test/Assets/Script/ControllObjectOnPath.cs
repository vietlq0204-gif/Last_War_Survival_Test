using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PointBaker))]
public class ControllObjectOnPath : CoreEventBase
{
    private struct RuntimeCard
    {
        public CardAddQuantity card;
        public Transform cachedTransform;
        public float distance;
        public float speed;
        public EntityId spawnZoneId;
    }

    [SerializeField, Min(0.01f)] private float defaultMoveSpeed = 2f;

    private PointBaker _pointBaker;
    private readonly ListPoint.PathCache _pathCache = new ListPoint.PathCache();
    private readonly List<RuntimeCard> _runtimeCards = new List<RuntimeCard>(128);
    private readonly Dictionary<EntityId, int> _runtimeCardIndexMap = new Dictionary<EntityId, int>(128);
    private bool _hasValidPath;
    private float _closestDistanceToStart = float.PositiveInfinity;

    public bool HasValidPath => _hasValidPath;
    public float PathLength => _hasValidPath ? _pathCache.totalLength : 0f;
    public int ActiveCardCount => _runtimeCards.Count;
    public float ClosestDistanceToStart => _runtimeCards.Count == 0 ? float.PositiveInfinity : _closestDistanceToStart;

    protected override void Awake()
    {
        base.Awake();
        TryGetPointBaker();
        RebuildPathCache();
    }

    public override void SubscribeEvents()
    {
        CoreEvents.cardAddQuantitySpawned.Subscribe(HandleCardSpawned, Binder);
    }

    private void FixedUpdate()
    {
        if (!_hasValidPath || _runtimeCards.Count == 0) return;

        float deltaTime = Time.fixedDeltaTime;
        float closestDistance = float.PositiveInfinity;

        for (int i = _runtimeCards.Count - 1; i >= 0; i--)
        {
            var runtimeCard = _runtimeCards[i];

            if (!IsRuntimeCardValid(runtimeCard))
            {
                RemoveRuntimeCardAt(i);
                continue;
            }

            float nextDistance = runtimeCard.distance + runtimeCard.speed * deltaTime;
            if (nextDistance >= _pathCache.totalLength)
            {
                CardAddQuantity endedCard = runtimeCard.card;
                EntityId endedSpawnZoneId = runtimeCard.spawnZoneId;
                RemoveRuntimeCardAt(i);
                RaiseReachedEnd(endedCard, endedSpawnZoneId);
                continue;
            }

            runtimeCard.distance = nextDistance;

            if (!_pointBaker.listPoint.EvaluateByDistance(_pathCache, nextDistance, out var worldPosition))
            {
                RemoveRuntimeCardAt(i);
                continue;
            }

            runtimeCard.cachedTransform.position = worldPosition;
            _runtimeCards[i] = runtimeCard;

            if (nextDistance < closestDistance)
                closestDistance = nextDistance;
        }

        _closestDistanceToStart = _runtimeCards.Count == 0
            ? float.PositiveInfinity
            : closestDistance;
    }

    public bool RebuildPathCache()
    {
        TryGetPointBaker();
        _hasValidPath = _pointBaker != null && _pointBaker.listPoint.BuildPathCache(_pathCache);
        _closestDistanceToStart = float.PositiveInfinity;
        return _hasValidPath;
    }

    public bool TryEvaluateDistance(float distance, out Vector3 position)
    {
        position = default;
        if (!_hasValidPath && !RebuildPathCache()) return false;

        return _pointBaker.listPoint.EvaluateByDistance(_pathCache, distance, out position);
    }

    private void HandleCardSpawned(CardAddQuantitySpawnedEvent e)
    {
        if (e == null || e.Card == null || e.PointBaker != _pointBaker) return;
        if (!_hasValidPath && !RebuildPathCache()) return;

        float initialDistance = Mathf.Clamp(
            e.InitialDistance,
            0f,
            Mathf.Max(0f, _pathCache.totalLength - 0.0001f));

        if (!_pointBaker.listPoint.EvaluateByDistance(_pathCache, initialDistance, out var worldPosition))
            return;

        EntityId instanceId = e.Card.gameObject.GetEntityId();
        float speed = e.MoveSpeed > 0f ? e.MoveSpeed : defaultMoveSpeed;

        e.Card.SetRoadCollisionEnabled(false);
        e.Card.CachedTransform.position = worldPosition;

        if (_runtimeCardIndexMap.TryGetValue(instanceId, out int index))
        {
            var cached = _runtimeCards[index];
            cached.cachedTransform = e.Card.CachedTransform;
            cached.distance = initialDistance;
            cached.speed = speed;
            cached.spawnZoneId = e.SpawnZoneId;
            _runtimeCards[index] = cached;
        }
        else
        {
            _runtimeCardIndexMap.Add(instanceId, _runtimeCards.Count);
            _runtimeCards.Add(new RuntimeCard
            {
                card = e.Card,
                cachedTransform = e.Card.CachedTransform,
                distance = initialDistance,
                speed = speed,
                spawnZoneId = e.SpawnZoneId
            });
        }

        if (initialDistance < _closestDistanceToStart)
            _closestDistanceToStart = initialDistance;
    }

    private void TryGetPointBaker()
    {
        if (_pointBaker != null) return;
        if (TryGetComponent(out _pointBaker)) return;

        if (transform.parent != null)
            _pointBaker = transform.parent.GetComponentInChildren<PointBaker>(true);
    }

    private bool IsRuntimeCardValid(RuntimeCard runtimeCard)
    {
        return runtimeCard.card != null
            && runtimeCard.cachedTransform != null
            && runtimeCard.card.gameObject.activeInHierarchy;
    }

    private void RaiseReachedEnd(CardAddQuantity card, EntityId spawnZoneId)
    {
        if (card == null) return;

        CoreEvents.cardAddQuantityReachedEnd.Raise(new CardAddQuantityReachedEndEvent
        {
            Card = card,
            PointBaker = _pointBaker,
            SpawnZoneId = spawnZoneId
        });
    }

    private void RemoveRuntimeCardAt(int index)
    {
        int lastIndex = _runtimeCards.Count - 1;
        EntityId removedId = _runtimeCards[index].card != null
            ? _runtimeCards[index].card.gameObject.GetEntityId()
            : default;

        if (index != lastIndex)
        {
            var lastCard = _runtimeCards[lastIndex];
            _runtimeCards[index] = lastCard;

            if (lastCard.card != null)
                _runtimeCardIndexMap[lastCard.card.gameObject.GetEntityId()] = index;
        }

        _runtimeCards.RemoveAt(lastIndex);

        if (removedId != default)
            _runtimeCardIndexMap.Remove(removedId);
    }
}
