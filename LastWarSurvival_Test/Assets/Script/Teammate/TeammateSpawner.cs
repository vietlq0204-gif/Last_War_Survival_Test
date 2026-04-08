using UnityEngine;

public class TeammateSpawner : SpawnGridQueue
{
    [SerializeField] private TeammateController teammateController;

    protected override string SpawnedObjectLabel => "teammate";

    public int CurrentHealth => ResolveController() != null ? ResolveController().CurrentHealth : 0;
    public int MaxHealth => ResolveController() != null ? ResolveController().MaxHealth : 0;
    public int ActiveTeammateCount => ResolveController() != null ? ResolveController().ActiveTeammateCount : 0;
    public int PendingIncomingDamage => ResolveController() != null ? ResolveController().PendingIncomingDamage : 0;
    public WeaponSO EquippedWeapon => ResolveController() != null ? ResolveController().EquippedWeapon : null;

    public override void SubscribeEvents()
    {
        base.SubscribeEvents();
        CoreEvents.collition.Subscribe(HandleCollitionEvent, Binder);
    }

    private void HandleCollitionEvent(CollitionEvent collitionEvent)
    {
        if (collitionEvent == null) return;
        if (collitionEvent.collitionType != CollitionEvent.CollitionType.Trigger) return;
        if (collitionEvent.collitionTag != CollitionEvent.CollitionTag.Player) return;

        var cardData = collitionEvent.cardAddQuantityData;
        if (cardData == null || !cardData.HasValidData) return;

        QueueSpawn(cardData.TeammateSpawnCount);
    }

    public override int GetOccupiedSlotCount()
    {
        TeammateController controller = ResolveController();
        return controller != null ? controller.ActiveTeammateCount : 0;
    }

    public void RegisterSpawnedTeammate(Teammate teammate)
    {
        ResolveController()?.RegisterSpawnedTeammate(teammate);
    }

    public void EquipWeapon(WeaponSO weapon)
    {
        ResolveController()?.EquipWeapon(weapon);
    }

    public void NotifyTeammateDespawned(Teammate teammate)
    {
        ResolveController()?.NotifyTeammateDespawned(teammate);
    }

    public bool QueueIncomingDamage(int damage)
    {
        TeammateController controller = ResolveController();
        return controller != null && controller.QueueIncomingDamage(damage);
    }

    private TeammateController ResolveController()
    {
        if (teammateController != null)
            return teammateController;

        if (TryGetComponent(out teammateController))
            return teammateController;

        teammateController = GetComponentInChildren<TeammateController>(true);
        if (teammateController != null)
            return teammateController;

        if (transform.parent != null)
            teammateController = transform.parent.GetComponentInChildren<TeammateController>(true);

        return teammateController;
    }
}
