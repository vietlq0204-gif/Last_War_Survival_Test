using System;
using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.ScriptableObjects;
using Vit.SpawnKit.Services;

namespace Vit.SpawnKit.Data
{
    /// <summary>
    /// Batch spawn request.
    /// </summary>
    public readonly struct SpawnRequest
    {
        public readonly SpawnableSO spawnable;
        public readonly SpawnKey key;
        public readonly int count;
        public readonly Transform parent;
        public readonly ISpawnAlgorithm algorithm;
        public readonly uint seed;
        public readonly SpawnLifecycle lifecycle;
        public readonly bool overrideLifecycle;
        public readonly int[] variantPlan;

        public SpawnRequest(
            SpawnableSO spawnable,
            int count,
            Transform parent,
            ISpawnAlgorithm algorithm,
            uint seed = 0,
            SpawnLifecycle? lifecycle = null,
            int[] variantPlan = null)
        {
            this.spawnable = spawnable;
            this.key = spawnable != null ? spawnable.key : default;
            this.count = count;
            this.parent = parent;
            this.algorithm = algorithm;
            this.seed = seed;
            this.overrideLifecycle = lifecycle.HasValue;
            this.lifecycle = lifecycle ?? SpawnLifecycle.Manual;
            this.variantPlan = variantPlan;
        }

        public SpawnRequest(
            SpawnKey key,
            int count,
            Transform parent,
            ISpawnAlgorithm algorithm,
            uint seed = 0,
            SpawnLifecycle? lifecycle = null,
            int[] variantPlan = null)
        {
            this.spawnable = null;
            this.key = key;
            this.count = count;
            this.parent = parent;
            this.algorithm = algorithm;
            this.seed = seed;
            this.overrideLifecycle = lifecycle.HasValue;
            this.lifecycle = lifecycle ?? SpawnLifecycle.Manual;
            this.variantPlan = variantPlan;
        }

        public static SpawnRequest Single(
            SpawnableSO spawnable,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null,
            uint seed = 0,
            SpawnLifecycle? lifecycle = null)
        {
            return new SpawnRequest(
                spawnable,
                1,
                parent,
                new FixedPoseAlgorithm(position, rotation),
                seed,
                lifecycle);
        }
    }

    /// <summary>
    /// Holds spawned instances so they can be despawned as a batch.
    /// </summary>
    public sealed class SpawnHandle /*: IDisposable*/
    {
        private readonly SpawnManager _manager;
        private readonly List<GameObject> _instances;

        public IReadOnlyList<GameObject> Instances => _instances;
        public bool IsEmpty => _instances.Count == 0;
        public GameObject FirstOrDefault => _instances.Count > 0 ? _instances[0] : null;

        internal SpawnHandle(SpawnManager manager, List<GameObject> instances)
        {
            _manager = manager;
            _instances = instances;
        }

        /// <summary>
        /// return all instance in this handle request to pool
        /// </summary>
        public void Despawn()
        {
            if (_manager == null) return;

            for (int i = 0; i < _instances.Count; i++)
            {
                _manager.Despawn(_instances[i]);
            }

            _instances.Clear();
        }

        /// <summary>
        /// return the instance specific in this handle request to pool
        /// </summary>
        /// <param name="instance"></param>
        /// <returns></returns>
        public bool Despawn(GameObject instance)
        {
            if (_manager == null || instance == null) return false;

            int index = _instances.IndexOf(instance);
            if (index < 0) return false;
            if (!_manager.Despawn(instance)) return false;

            _instances.RemoveAt(index);
            return true;
        }

        // public void Dispose()
        // {
        //     Despawn();
        // }
    }
}