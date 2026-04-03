using System;
using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.ScriptableObjects;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Data;

public partial class CardAddQuantitySpawnZone : CoreEventBase
{
    [SerializeField] private SpawnPresetSO preset;
    private readonly List<SpawnHandle> _handle = new();

    private void OnDisable()
    {
        ReturnAllToPool();
    }
    
}

// logic
public partial class CardAddQuantitySpawnZone
{
    private void SpawnCard()
    {
        var handle = SpawnKit.Spawn(preset, transform);
        _handle.Add(handle);
    }

    private void ReturnAllToPool()
    {
        foreach (var handle in _handle)
        {
            handle.Despawn();
        }
        _handle.Clear();
    }
}

// event
public partial class CardAddQuantitySpawnZone
{
    public override void SubscribeEvents()
    {
        CoreEvents.gameStart.Subscribe(e => SpawnCardWhenGameStart(e), Binder);
    }

    private void SpawnCardWhenGameStart(GameStartEvent e)
    {
        var isStart = e.IsStarted;
        if (!isStart) return;
        SpawnCard();
    }
}
