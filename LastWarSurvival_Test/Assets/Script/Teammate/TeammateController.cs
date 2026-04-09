using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class TeammateController : CoreEventBase
{
    private const string FlowLogPrefix = "[HomeDamageFlow][TeammateController]";

    [Header("Team Health")]
    [SerializeField, Min(1)] private int healthPerTeammate = 1;
    [SerializeField, Min(1)] private int teammateDespawnBatchSize = 8;
    [SerializeField] private bool onlyUseDamageResponseLayerFilter = false;
    [SerializeField] private LayerMask damageResponseLayers = ~0;
    [SerializeField] private bool debugDamageFlowLogs = true;

    [Header("Weapon")]
    [SerializeField] private WeaponSO defaultGun;
    [SerializeField] private BulletSpawner bulletSpawner;

    [Header("Player Movement")]
    [SerializeField] private Collider playerRoadCollider;
    [SerializeField] private Camera inputCamera;
    [SerializeField, Min(0.01f)] private float dragSensitivity = 1f;
    [SerializeField, Min(0f)] private float horizontalFollowSpeed = 18f;

    private readonly List<Teammate> _activeTeammates = new List<Teammate>(128);
    private readonly HashSet<EntityId> _activeTeammateIds = new HashSet<EntityId>();
    private readonly Queue<Teammate> _pendingDamageDespawns = new Queue<Teammate>(64);
    private readonly HashSet<EntityId> _pendingDamageDespawnIds = new HashSet<EntityId>();

    private int _currentHealth;
    private int _pendingIncomingDamage;
    private WeaponSO _equippedWeapon;
    private bool _hasWarnedMissingBulletSpawner;
    private bool _isDraggingPlayer;
    private bool _dragUsesTouch;
    private int _activeTouchFingerId = -1;
    private float _dragStartPlayerX;
    private float _dragStartWorldX;
    private float _dragTargetX;
    private Collider _cachedPlayerCollider;
    private bool _isGameStarted;
    private bool _hasSeenLivingTeammateThisSession;
    private bool _hasRaisedGameOverEvent;

    public int CurrentHealth => Mathf.Max(0, _currentHealth);
    public int MaxHealth => MultiplyClamped(_activeTeammates.Count, ResolveHealthPerTeammate());
    public int ActiveTeammateCount => _activeTeammates.Count;
    public int PendingIncomingDamage => Mathf.Max(0, _pendingIncomingDamage);
    public WeaponSO EquippedWeapon => ResolveActiveWeapon();

    protected override void Awake()
    {
        base.Awake();
        _dragTargetX = transform.position.x;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _dragTargetX = ClampPlayerXToRoad(transform.position.x);
        ApplyEquippedWeapon();
    }

    private void Update()
    {
        HandlePlayerDragInput();
        ApplyHorizontalMovement();
        ProcessPendingIncomingDamage();
        FlushQueuedTeammateDespawns();
        NotifyGameOverIfNeeded();
    }

    private void OnDisable()
    {
        _activeTeammates.Clear();
        _activeTeammateIds.Clear();
        _pendingDamageDespawns.Clear();
        _pendingDamageDespawnIds.Clear();
        _currentHealth = 0;
        _pendingIncomingDamage = 0;
        _isDraggingPlayer = false;
        _dragUsesTouch = false;
        _activeTouchFingerId = -1;
        _dragTargetX = transform.position.x;
        _isGameStarted = false;
        _hasSeenLivingTeammateThisSession = false;
        _hasRaisedGameOverEvent = false;
    }

    public override void SubscribeEvents()
    {
        LogFlow("Subscribing to enemyHomeDamageBatch and teammateWeaponPickup.");
        CoreEvents.enemyHomeDamageBatch.Subscribe(HandleEnemyHomeDamageBatchEvent, Binder);
        CoreEvents.teammateWeaponPickup.Subscribe(HandleTeammateWeaponPickupEvent, Binder);
        CoreEvents.gameStart.Subscribe(HandleGameStartEvent, Binder);
    }

    public void RegisterSpawnedTeammate(Teammate teammate)
    {
        if (teammate == null)
            return;

        EntityId teammateId = teammate.CachedEntityId;
        if (!_activeTeammateIds.Add(teammateId))
            return;

        _pendingDamageDespawnIds.Remove(teammateId);
        _activeTeammates.Add(teammate);
        _currentHealth = Mathf.Max(0, _currentHealth) + ResolveHealthPerTeammate();
        ClampCurrentHealthToCapacity();
        _hasSeenLivingTeammateThisSession = true;
        _hasRaisedGameOverEvent = false;
        teammate.ApplyWeaponVisual(ResolveActiveWeapon());
        LogFlow(
            $"Registered teammate='{teammate.name}#{teammateId}'. activeCount={_activeTeammates.Count} currentHealth={_currentHealth} maxHealth={MaxHealth}.",
            teammate);
    }

    public void NotifyTeammateDespawned(Teammate teammate)
    {
        if (teammate == null)
            return;

        EntityId teammateId = teammate.CachedEntityId;
        _pendingDamageDespawnIds.Remove(teammateId);

        if (!_activeTeammateIds.Remove(teammateId))
        {
            ClampCurrentHealthToCapacity();
            return;
        }

        for (int i = _activeTeammates.Count - 1; i >= 0; i--)
        {
            Teammate activeTeammate = _activeTeammates[i];
            if (activeTeammate == null || activeTeammate.CachedEntityId.Equals(teammateId))
                _activeTeammates.RemoveAt(i);
        }

        ClampCurrentHealthToCapacity();
        LogFlow(
            $"Notified teammate despawn teammate='{teammate.name}#{teammateId}'. activeCount={_activeTeammates.Count} currentHealth={_currentHealth} maxHealth={MaxHealth}.",
            teammate);
    }

    public void EquipWeapon(WeaponSO weapon)
    {
        if (weapon != null && !weapon.IsValid)
            return;

        _equippedWeapon = weapon;
        ApplyEquippedWeapon();
    }

    public bool QueueIncomingDamage(int damage)
    {
        if (damage <= 0)
            return false;

        _pendingIncomingDamage = AddClamped(_pendingIncomingDamage, damage);
        LogFlow(
            $"Queued incoming damage damage={damage} pendingIncomingDamage={_pendingIncomingDamage} activeCount={_activeTeammates.Count} currentHealth={_currentHealth}.",
            warning: true);
        return true;
    }

    private void HandleEnemyHomeDamageBatchEvent(EnemyHomeDamageBatchEvent damageBatchEvent)
    {
        if (damageBatchEvent == null || damageBatchEvent.totalDamage <= 0)
            return;

        bool canApplyDamage = CanDespawnFromCollisionLayer(damageBatchEvent.collisionLayer);
        LogFlow(
            $"Received EnemyHomeDamageBatchEvent totalDamage={damageBatchEvent.totalDamage} enemyHitCount={damageBatchEvent.enemyHitCount} " +
            $"collisionLayer={damageBatchEvent.collisionLayer} ('{LayerMask.LayerToName(damageBatchEvent.collisionLayer)}') " +
            $"onlyUseDamageResponseLayerFilter={onlyUseDamageResponseLayerFilter} damageResponseLayers={damageResponseLayers.value} " +
            $"canApplyDamage={canApplyDamage} activeCount={_activeTeammates.Count} currentHealth={_currentHealth}.",
            damageBatchEvent.sourceSpawner,
            warning: !canApplyDamage);

        if (!canApplyDamage)
            return;

        QueueIncomingDamage(damageBatchEvent.totalDamage);
    }

    private void HandleGameStartEvent(GameStartEvent gameStartEvent)
    {
        if (gameStartEvent == null || !gameStartEvent.IsStarted)
            return;

        _isGameStarted = true;
        _hasSeenLivingTeammateThisSession = _activeTeammates.Count > 0;
        _hasRaisedGameOverEvent = false;
    }

    private void HandleTeammateWeaponPickupEvent(TeammateWeaponPickupEvent pickupEvent)
    {
        if (pickupEvent == null || !pickupEvent.hasValidWeaponData)
            return;

        Teammate collector = pickupEvent.collector;
        if (collector == null || !_activeTeammateIds.Contains(collector.CachedEntityId))
            return;

        EquipWeapon(pickupEvent.weaponData);
    }

    private void ProcessPendingIncomingDamage()
    {
        if (_pendingIncomingDamage <= 0)
            return;

        if (_activeTeammates.Count <= 0)
        {
            LogFlow(
                $"Dropping pending damage because there are no active teammates. pendingIncomingDamage={_pendingIncomingDamage}.",
                warning: true);
            _pendingIncomingDamage = 0;
            _currentHealth = 0;
            return;
        }

        int damageToApply = _pendingIncomingDamage;
        _pendingIncomingDamage = 0;
        LogFlow($"Processing pending damage damageToApply={damageToApply} currentHealthBefore={_currentHealth} activeCount={_activeTeammates.Count}.", warning: true);
        ApplyIncomingDamage(damageToApply);
    }

    private void ApplyIncomingDamage(int damage)
    {
        if (damage <= 0 || _activeTeammates.Count <= 0)
            return;

        ClampCurrentHealthToCapacity();
        int currentHealthBefore = _currentHealth;
        _currentHealth = Mathf.Max(0, _currentHealth - damage);

        int healthUnit = ResolveHealthPerTeammate();
        int desiredAliveCount = _currentHealth <= 0
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(_currentHealth / (float)healthUnit), 0, _activeTeammates.Count);

        int despawnCount = Mathf.Max(0, _activeTeammates.Count - desiredAliveCount);
        LogFlow(
            $"Applied incoming damage damage={damage} currentHealthBefore={currentHealthBefore} currentHealthAfter={_currentHealth} " +
            $"healthPerTeammate={healthUnit} activeCountBefore={_activeTeammates.Count} desiredAliveCount={desiredAliveCount} despawnCount={despawnCount}.",
            warning: despawnCount > 0);
        QueueTeammatesForDamageDespawn(despawnCount);
        ClampCurrentHealthToCapacity();
    }

    private void NotifyGameOverIfNeeded()
    {
        if (!_isGameStarted || _hasRaisedGameOverEvent || !_hasSeenLivingTeammateThisSession)
            return;

        if (_activeTeammates.Count > 0 || _currentHealth > 0 || _pendingIncomingDamage > 0 || _pendingDamageDespawns.Count > 0)
            return;

        _hasRaisedGameOverEvent = true;
        CoreEvents.gameOver.Raise(new GameOverEvent
        {
            IsGameOver = true
        });
    }

    private void QueueTeammatesForDamageDespawn(int count)
    {
        while (count > 0 && _activeTeammates.Count > 0)
        {
            int lastIndex = _activeTeammates.Count - 1;
            Teammate teammate = _activeTeammates[lastIndex];
            _activeTeammates.RemoveAt(lastIndex);

            if (teammate == null)
                continue;

            EntityId teammateId = teammate.CachedEntityId;
            _activeTeammateIds.Remove(teammateId);

            if (_pendingDamageDespawnIds.Add(teammateId))
            {
                _pendingDamageDespawns.Enqueue(teammate);
                LogFlow(
                    $"Queued teammate for despawn teammate='{teammate.name}#{teammateId}' pendingQueueCount={_pendingDamageDespawns.Count} remainingActiveCount={_activeTeammates.Count}.",
                    teammate,
                    warning: true);
            }

            count--;
        }
    }

    private void FlushQueuedTeammateDespawns()
    {
        int batchBudget = Mathf.Max(1, teammateDespawnBatchSize);
        while (batchBudget-- > 0 && _pendingDamageDespawns.Count > 0)
        {
            Teammate teammate = _pendingDamageDespawns.Dequeue();
            if (teammate == null)
                continue;

            if (!teammate.BeginQueuedDamageDespawn())
            {
                _pendingDamageDespawnIds.Remove(teammate.CachedEntityId);
                LogFlow(
                    $"BeginQueuedDamageDespawn returned false for teammate='{teammate.name}#{teammate.CachedEntityId}'.",
                    teammate,
                    warning: true);
                continue;
            }

            LogFlow(
                $"Sending teammate='{teammate.name}#{teammate.CachedEntityId}' to BufferedPoolDespawnQueue.",
                teammate,
                warning: true);
            BufferedPoolDespawnQueue.Queue(teammate);
        }
    }

    private void ClampCurrentHealthToCapacity()
    {
        _currentHealth = Mathf.Clamp(_currentHealth, 0, MaxHealth);
    }

    private int ResolveHealthPerTeammate()
    {
        return Mathf.Max(1, healthPerTeammate);
    }

    private bool CanDespawnFromCollisionLayer(int collisionLayer)
    {
        if (!onlyUseDamageResponseLayerFilter)
            return true;

        if (collisionLayer < 0 || collisionLayer > 31)
            return false;

        return (damageResponseLayers.value & (1 << collisionLayer)) != 0;
    }

    private void LogFlow(string message, Object context = null, bool warning = false)
    {
        if (!debugDamageFlowLogs)
            return;

        string formattedMessage = $"{FlowLogPrefix}[{name}] {message}";
        Object resolvedContext = context != null ? context : this;
        if (warning)
            Debug.LogWarning(formattedMessage, resolvedContext);
        else
            Debug.Log(formattedMessage, resolvedContext);
    }

    private void ApplyEquippedWeapon()
    {
        WeaponSO activeWeapon = ResolveActiveWeapon();
        ApplyWeaponVisualToActiveTeammates(activeWeapon);

        BulletSpawner resolvedBulletSpawner = ResolveBulletSpawner();
        if (resolvedBulletSpawner == null)
        {
            if (_hasWarnedMissingBulletSpawner || activeWeapon == null)
                return;

            _hasWarnedMissingBulletSpawner = true;
            Debug.LogWarning($"'{name}' could not find a BulletSpawner to apply weapon '{activeWeapon.name}'.", this);
            return;
        }

        _hasWarnedMissingBulletSpawner = false;
        resolvedBulletSpawner.ApplyWeapon(activeWeapon);
    }

    private WeaponSO ResolveActiveWeapon()
    {
        if (_equippedWeapon != null && _equippedWeapon.IsValid)
            return _equippedWeapon;

        return defaultGun != null && defaultGun.IsValid ? defaultGun : null;
    }

    private BulletSpawner ResolveBulletSpawner()
    {
        if (bulletSpawner != null)
            return bulletSpawner;

        bulletSpawner = GetComponentInChildren<BulletSpawner>(true);
        if (bulletSpawner != null)
            return bulletSpawner;

        if (transform.parent != null)
            bulletSpawner = transform.parent.GetComponentInChildren<BulletSpawner>(true);

        return bulletSpawner;
    }

    private void ApplyWeaponVisualToActiveTeammates(WeaponSO weapon)
    {
        for (int i = _activeTeammates.Count - 1; i >= 0; i--)
        {
            Teammate teammate = _activeTeammates[i];
            if (teammate == null)
                continue;

            teammate.ApplyWeaponVisual(weapon);
        }
    }

    private void HandlePlayerDragInput()
    {
        if (TryGetTouchBegan(out Vector2 beganTouchPosition, out int beganTouchFingerId))
        {
            TryBeginDrag(beganTouchPosition, usesTouch: true, beganTouchFingerId);
        }
        else if (TryGetMouseButtonDown(out Vector2 mouseDownPosition))
        {
            TryBeginDrag(mouseDownPosition, usesTouch: false, fingerId: -1);
        }

        if (!_isDraggingPlayer)
            return;

        if (_dragUsesTouch)
        {
            if (!TryGetTouchByFingerId(_activeTouchFingerId, out Vector2 activeTouchPosition, out bool touchReleased))
            {
                EndDrag();
                return;
            }

            if (touchReleased)
            {
                EndDrag();
                return;
            }

            UpdateDragTarget(activeTouchPosition);
            return;
        }

        if (!TryGetMouseButton(out Vector2 mouseHeldPosition))
        {
            EndDrag();
            return;
        }

        UpdateDragTarget(mouseHeldPosition);
    }

    private void ApplyHorizontalMovement()
    {
        float clampedTargetX = ClampPlayerXToRoad(_dragTargetX);
        Vector3 currentPosition = transform.position;
        float nextX = horizontalFollowSpeed > 0f
            ? Mathf.MoveTowards(currentPosition.x, clampedTargetX, horizontalFollowSpeed * Time.deltaTime)
            : clampedTargetX;

        if (Mathf.Approximately(currentPosition.x, nextX))
            return;

        transform.position = new Vector3(nextX, currentPosition.y, currentPosition.z);
    }

    private bool TryBeginDrag(Vector2 screenPosition, bool usesTouch, int fingerId)
    {
        if (IsPointerOverUi(usesTouch, fingerId))
            return false;

        if (!TryScreenToDragPlaneX(screenPosition, out float worldX))
            return false;

        _isDraggingPlayer = true;
        _dragUsesTouch = usesTouch;
        _activeTouchFingerId = usesTouch ? fingerId : -1;
        _dragStartPlayerX = transform.position.x;
        _dragStartWorldX = worldX;
        _dragTargetX = transform.position.x;
        return true;
    }

    private void UpdateDragTarget(Vector2 screenPosition)
    {
        if (!TryScreenToDragPlaneX(screenPosition, out float worldX))
            return;

        float dragDeltaX = (worldX - _dragStartWorldX) * Mathf.Max(0f, dragSensitivity);
        _dragTargetX = _dragStartPlayerX + dragDeltaX;
    }

    private void EndDrag()
    {
        _isDraggingPlayer = false;
        _dragUsesTouch = false;
        _activeTouchFingerId = -1;
        _dragTargetX = ClampPlayerXToRoad(_dragTargetX);
    }

    private bool TryScreenToDragPlaneX(Vector2 screenPosition, out float worldX)
    {
        worldX = transform.position.x;

        Camera camera = ResolveInputCamera();
        if (camera == null)
            return false;

        Ray ray = camera.ScreenPointToRay(screenPosition);
        Plane dragPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
        if (!dragPlane.Raycast(ray, out float enter))
            return false;

        worldX = ray.GetPoint(enter).x;
        return true;
    }

    private float ClampPlayerXToRoad(float targetX)
    {
        Collider roadCollider = ResolvePlayerRoadCollider();
        if (roadCollider == null || !roadCollider.enabled || !roadCollider.gameObject.activeInHierarchy)
            return targetX;

        Bounds roadBounds = roadCollider.bounds;
        float playerHalfWidth = ResolvePlayerHalfWidth();
        float minX = roadBounds.min.x + playerHalfWidth;
        float maxX = roadBounds.max.x - playerHalfWidth;

        if (minX > maxX)
            return roadBounds.center.x;

        return Mathf.Clamp(targetX, minX, maxX);
    }

    private float ResolvePlayerHalfWidth()
    {
        Collider playerCollider = ResolvePlayerCollider();
        if (playerCollider == null)
            return 0f;

        return Mathf.Max(0f, playerCollider.bounds.extents.x);
    }

    private Collider ResolvePlayerCollider()
    {
        if (_cachedPlayerCollider != null)
            return _cachedPlayerCollider;

        if (TryGetComponent(out Collider rootCollider))
        {
            _cachedPlayerCollider = rootCollider;
            return _cachedPlayerCollider;
        }

        _cachedPlayerCollider = GetComponentInChildren<Collider>(true);
        return _cachedPlayerCollider;
    }

    private Collider ResolvePlayerRoadCollider()
    {
        if (playerRoadCollider != null)
            return playerRoadCollider;

        Transform parent = transform.parent;
        if (parent == null)
            return null;

        Collider[] siblingColliders = parent.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < siblingColliders.Length; i++)
        {
            Collider candidate = siblingColliders[i];
            if (candidate == null)
                continue;

            string candidateName = candidate.name;
            if (!string.IsNullOrEmpty(candidateName) && candidateName.ToLowerInvariant().Contains("road"))
            {
                playerRoadCollider = candidate;
                return playerRoadCollider;
            }
        }

        return null;
    }

    private Camera ResolveInputCamera()
    {
        if (inputCamera != null)
            return inputCamera;

        inputCamera = Camera.main;
        return inputCamera;
    }

    private static bool TryGetTouchBegan(out Vector2 screenPosition, out int fingerId)
    {
        #if ENABLE_INPUT_SYSTEM
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var candidate = touches[i];
                if (!candidate.press.wasPressedThisFrame)
                    continue;

                screenPosition = candidate.position.ReadValue();
                fingerId = candidate.touchId.ReadValue();
                return true;
            }
        }
        #endif

        #if ENABLE_LEGACY_INPUT_MANAGER
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch candidate = Input.GetTouch(i);
            if (candidate.phase != TouchPhase.Began)
                continue;

            screenPosition = candidate.position;
            fingerId = candidate.fingerId;
            return true;
        }
        #endif

        screenPosition = default;
        fingerId = -1;
        return false;
    }

    private static bool TryGetTouchByFingerId(int fingerId, out Vector2 screenPosition, out bool isReleased)
    {
        #if ENABLE_INPUT_SYSTEM
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var candidate = touches[i];
                if (candidate.touchId.ReadValue() != fingerId)
                    continue;

                screenPosition = candidate.position.ReadValue();
                isReleased = candidate.press.wasReleasedThisFrame || !candidate.press.isPressed;
                return true;
            }
        }
        #endif

        #if ENABLE_LEGACY_INPUT_MANAGER
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch candidate = Input.GetTouch(i);
            if (candidate.fingerId != fingerId)
                continue;

            screenPosition = candidate.position;
            isReleased = candidate.phase == TouchPhase.Ended || candidate.phase == TouchPhase.Canceled;
            return true;
        }
        #endif

        screenPosition = default;
        isReleased = true;
        return false;
    }

    private static bool TryGetMouseButtonDown(out Vector2 screenPosition)
    {
        #if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            screenPosition = mouse.position.ReadValue();
            return true;
        }
        #endif

        #if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            screenPosition = Input.mousePosition;
            return true;
        }
        #endif

        screenPosition = default;
        return false;
    }

    private static bool TryGetMouseButton(out Vector2 screenPosition)
    {
        #if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
        {
            screenPosition = mouse.position.ReadValue();
            return true;
        }
        #endif

        #if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(0))
        {
            screenPosition = Input.mousePosition;
            return true;
        }
        #endif

        screenPosition = default;
        return false;
    }

    private static bool IsPointerOverUi(bool usesTouch, int fingerId)
    {
        if (EventSystem.current == null)
            return false;

        if (!usesTouch)
            return EventSystem.current.IsPointerOverGameObject();

        return EventSystem.current.IsPointerOverGameObject(fingerId);
    }

    private static int AddClamped(int currentValue, int delta)
    {
        if (delta <= 0)
            return Mathf.Max(0, currentValue);

        long total = (long)Mathf.Max(0, currentValue) + delta;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private static int MultiplyClamped(int value, int multiplier)
    {
        if (value <= 0 || multiplier <= 0)
            return 0;

        long total = (long)value * multiplier;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }
}
