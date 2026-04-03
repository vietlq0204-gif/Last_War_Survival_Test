using System;
using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.ScriptableObjects;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Data;

public class CardAddQuantitySpawnZone : MonoBehaviour
{
    [SerializeField] private SpawnPresetSO preset;

    private readonly List<SpawnHandle> _handle = new();

    private void OnDisable()
    {
        ReturnAllToPool();
    }

    private void Start()
    {
        SpawnCard();
    }

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
