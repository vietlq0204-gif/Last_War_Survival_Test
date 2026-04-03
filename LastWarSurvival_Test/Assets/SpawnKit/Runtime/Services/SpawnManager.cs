using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.Factories;
using Vit.SpawnKit.Pooling;
using Vit.SpawnKit.ScriptableObjects;

namespace Vit.SpawnKit.Services
{
    /// <summary>
    /// Runtime service responsible for spawning and pooling.
    /// </summary>
    public sealed class SpawnManager : MonoBehaviour
    {
        private sealed class AsyncSpawnOperation
        {
            public readonly TaskCompletionSource<SpawnHandle> completion = new TaskCompletionSource<SpawnHandle>();
            public readonly List<GameObject> instances = new List<GameObject>(64);
            public Coroutine coroutine;
        }

        private sealed class SpawnRuntime
        {
            public readonly SpawnableSO spawnable;
            public readonly GameObjectPool[] pools;
            public readonly float[] weights;

            public SpawnRuntime(SpawnableSO spawnable, GameObjectPool[] pools, float[] weights)
            {
                this.spawnable = spawnable;
                this.pools = pools;
                this.weights = weights;
            }

            public void Dispose()
            {
                for (int i = 0; i < pools.Length; i++)
                {
                    pools[i]?.Dispose();
                }
            }
        }

        public static SpawnManager Instance { get; private set; }

        [Header("Catalog")]
        [SerializeField] private SpawnCatalogSO catalog;

        [Header("Pools Root")]
        [SerializeField] private Transform poolsRoot;

        private readonly Dictionary<SpawnableSO, SpawnRuntime> _runtimes = new Dictionary<SpawnableSO, SpawnRuntime>(128);
        private readonly List<GameObject> _resultBuffer = new List<GameObject>(64);
        private readonly TimedDespawnScheduler _timedDespawnScheduler = new TimedDespawnScheduler();
        private readonly List<AsyncSpawnOperation> _pendingAsyncSpawns = new List<AsyncSpawnOperation>(8);

        private int _nextPoolId = 1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Multiple SpawnManager instances detected. The latest one will become Instance.");
            }
            Instance = this;

            if (poolsRoot == null)
            {
                var go = new GameObject("PoolsRoot");
                poolsRoot = go.transform;
                poolsRoot.SetParent(transform, false);
            }

            RegisterFromCatalog();
        }

        private void OnDestroy()
        {
            CancelPendingAsyncSpawns();
            _timedDespawnScheduler.Clear();

            foreach (var runtime in _runtimes.Values)
            {
                runtime?.Dispose();
            }
            _runtimes.Clear();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            FlushPendingPoolHierarchy();
            _timedDespawnScheduler.Tick();
        }

        public void RegisterFromCatalog()
        {
            if (catalog == null || catalog.items == null) return;

            for (int i = 0; i < catalog.items.Count; i++)
            {
                Register(catalog.items[i]);
            }
        }

        public void Prewarm(SpawnCatalogSO preloadCatalog = null)
        {
            var source = preloadCatalog != null ? preloadCatalog : catalog;
            if (source == null || source.items == null) return;

            for (int i = 0; i < source.items.Count; i++)
            {
                Register(source.items[i]);
            }
        }

        public bool Register(SpawnableSO spawnable)
        {
            if (spawnable == null) return false;
            if (_runtimes.ContainsKey(spawnable)) return true;

            int variantCount = GetVariantCount(spawnable);
            if (variantCount <= 0) return false;

            var pools = new GameObjectPool[variantCount];
            var weights = new float[variantCount];
            bool hasPool = false;

            for (int i = 0; i < variantCount; i++)
            {
                var factory = CreateFactory(spawnable, i);
                if (factory == null) continue;

                string debugName = $"{spawnable.name}_v{i}";
                pools[i] = new GameObjectPool(spawnable.key, _nextPoolId++, factory, spawnable.poolConfig, _timedDespawnScheduler, poolsRoot, debugName);
                pools[i].Prewarm(spawnable.poolConfig.prewarmCount);
                weights[i] = GetVariantWeight(spawnable, i);
                hasPool = true;
            }

            if (!hasPool) return false;

            _runtimes.Add(spawnable, new SpawnRuntime(spawnable, pools, weights));
            return true;
        }

        public SpawnHandle Spawn(
            SpawnableSO spawnable,
            int count = 1,
            Transform parent = null,
            ISpawnAlgorithm algorithm = null,
            uint seed = 0,
            SpawnLifecycle? lifecycle = null)
        {
            return Spawn(new SpawnRequest(spawnable, count, parent, algorithm, seed, lifecycle));
        }

        public Task<SpawnHandle> SpawnAsync(
            SpawnableSO spawnable,
            int count = 1,
            Transform parent = null,
            ISpawnAlgorithm algorithm = null,
            uint seed = 0,
            SpawnLifecycle? lifecycle = null,
            int maxPerFrame = 32,
            CancellationToken cancellationToken = default)
        {
            return SpawnAsync(new SpawnRequest(spawnable, count, parent, algorithm, seed, lifecycle), maxPerFrame, cancellationToken);
        }

        public SpawnHandle Spawn(ISpawnRequestSource source)
        {
            if (source == null) return EmptyHandle();
            return Spawn(source.CreateSpawnRequest());
        }

        public Task<SpawnHandle> SpawnAsync(ISpawnRequestSource source, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            if (source == null) return Task.FromResult(EmptyHandle());
            return SpawnAsync(source.CreateSpawnRequest(), maxPerFrame, cancellationToken);
        }

        public SpawnHandle Spawn(SpawnPresetSO preset, Collider volume, Transform parent = null)
        {
            if (preset == null || preset.spawnable == null) return EmptyHandle();
            return Spawn(preset.CreateRequest(volume, parent));
        }

        public Task<SpawnHandle> SpawnAsync(SpawnPresetSO preset, Collider volume, Transform parent = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            if (preset == null || preset.spawnable == null) return Task.FromResult(EmptyHandle());
            return SpawnAsync(preset.CreateRequest(volume, parent), maxPerFrame, cancellationToken);
        }

        public SpawnHandle Spawn(SpawnPresetSO preset, Collider[] volumes, Transform parent = null)
        {
            if (preset == null || preset.spawnable == null) return EmptyHandle();
            return Spawn(preset.CreateRequest(volumes, parent));
        }

        public Task<SpawnHandle> SpawnAsync(SpawnPresetSO preset, Collider[] volumes, Transform parent = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            if (preset == null || preset.spawnable == null) return Task.FromResult(EmptyHandle());
            return SpawnAsync(preset.CreateRequest(volumes, parent), maxPerFrame, cancellationToken);
        }

        public SpawnHandle Spawn(SpawnPresetSO preset, Transform parent = null)
        {
            if (preset == null || preset.spawnable == null) return EmptyHandle();
            return Spawn(preset.CreateRequest(parent));
        }

        public Task<SpawnHandle> SpawnAsync(SpawnPresetSO preset, Transform parent = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            if (preset == null || preset.spawnable == null) return Task.FromResult(EmptyHandle());
            return SpawnAsync(preset.CreateRequest(parent), maxPerFrame, cancellationToken);
        }

        public GameObject SpawnOne(SpawnableSO spawnable, Vector3 position, Quaternion rotation, Transform parent = null, uint seed = 0, SpawnLifecycle? lifecycle = null)
        {
            var handle = Spawn(SpawnRequest.Single(spawnable, position, rotation, parent, seed, lifecycle));
            return handle.FirstOrDefault;
        }

        public SpawnHandle Spawn(in SpawnRequest request)
        {
            if (!TryPrepareSpawn(request, out var prepared)) return EmptyHandle();

            _resultBuffer.Clear();
            ExecuteSpawnRange(prepared, 0, prepared.request.count, _resultBuffer);

            var instances = new List<GameObject>(_resultBuffer.Count);
            instances.AddRange(_resultBuffer);
            return new SpawnHandle(this, instances);
        }

        public Task<SpawnHandle> SpawnAsync(in SpawnRequest request, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            if (!TryPrepareSpawn(request, out var prepared))
            {
                return Task.FromResult(EmptyHandle());
            }

            int batchSize = Mathf.Max(1, maxPerFrame);
            if (prepared.request.count <= batchSize)
            {
                return Task.FromResult(Spawn(prepared.request));
            }

            var operation = new AsyncSpawnOperation();
            operation.instances.Capacity = Mathf.Max(operation.instances.Capacity, prepared.request.count);
            operation.coroutine = StartCoroutine(SpawnAsyncRoutine(prepared, batchSize, cancellationToken, operation));
            _pendingAsyncSpawns.Add(operation);
            return operation.completion.Task;
        }

        public bool Despawn(GameObject go)
        {
            if (go == null) return false;
            var pooled = go.GetComponent<PooledObject>();
            if (pooled == null) return false;
            return pooled.ReturnToPool();
        }

        public PoolStats GetPoolStats(SpawnableSO spawnable)
        {
            TryGetPoolStats(spawnable, out var stats);
            return stats;
        }

        public bool TryGetPoolStats(SpawnableSO spawnable, out PoolStats stats)
        {
            stats = default;
            if (spawnable == null) return false;

            if (!_runtimes.TryGetValue(spawnable, out var runtime))
            {
                int variantCount = GetVariantCount(spawnable);
                int max = variantCount > 0 ? variantCount * Mathf.Max(1, spawnable.poolConfig.maxSize) : 0;
                stats = new PoolStats(0, 0, 0, max);
                return false;
            }

            int active = 0;
            int inactive = 0;
            int total = 0;
            int maxTotal = 0;

            for (int i = 0; i < runtime.pools.Length; i++)
            {
                var pool = runtime.pools[i];
                if (pool == null) continue;

                active += pool.ActiveCount;
                inactive += pool.InactiveCount;
                total += pool.TotalCount;
                maxTotal += pool.MaxSize;
            }

            stats = new PoolStats(active, inactive, total, maxTotal);
            return true;
        }

        public int Trim(SpawnableSO spawnable, int keepInactive = 0)
        {
            if (!_runtimes.TryGetValue(spawnable, out var runtime)) return 0;

            int removed = 0;
            for (int i = 0; i < runtime.pools.Length; i++)
            {
                removed += runtime.pools[i]?.Trim(keepInactive) ?? 0;
            }

            return removed;
        }

        public int Clear(SpawnableSO spawnable)
        {
            if (!_runtimes.TryGetValue(spawnable, out var runtime)) return 0;

            int removed = 0;
            for (int i = 0; i < runtime.pools.Length; i++)
            {
                removed += runtime.pools[i]?.Clear() ?? 0;
            }

            return removed;
        }

        public int ReleaseUnused(SpawnableSO spawnable)
        {
            if (!_runtimes.TryGetValue(spawnable, out var runtime)) return 0;

            int removed = 0;
            for (int i = 0; i < runtime.pools.Length; i++)
            {
                removed += runtime.pools[i]?.ReleaseUnused() ?? 0;
            }

            return removed;
        }

        private readonly struct PreparedSpawn
        {
            public readonly SpawnRequest request;
            public readonly SpawnRuntime runtime;
            public readonly uint effectiveSeed;
            public readonly SpawnLifecycle lifecycle;
            public readonly ISpawnAlgorithm algorithm;

            public PreparedSpawn(
                in SpawnRequest request,
                SpawnRuntime runtime,
                uint effectiveSeed,
                SpawnLifecycle lifecycle,
                ISpawnAlgorithm algorithm)
            {
                this.request = request;
                this.runtime = runtime;
                this.effectiveSeed = effectiveSeed;
                this.lifecycle = lifecycle;
                this.algorithm = algorithm;
            }
        }

        private SpawnHandle EmptyHandle()
        {
            return new SpawnHandle(this, new List<GameObject>(0));
        }

        private bool TryPrepareSpawn(in SpawnRequest request, out PreparedSpawn prepared)
        {
            prepared = default;
            if (request.count <= 0) return false;

            var spawnable = ResolveSpawnable(request);
            if (spawnable == null) return false;
            if (!Register(spawnable)) return false;
            if (!_runtimes.TryGetValue(spawnable, out var runtime)) return false;

            uint effectiveSeed = ResolveSeed(request.seed, spawnable, request.parent);
            var lifecycle = request.overrideLifecycle ? request.lifecycle : spawnable.defaultLifecycle;
            lifecycle.Sanitize();

            var algorithm = request.algorithm ?? new SimplePointAlgorithm(Vector3.zero, 0f);
            if (algorithm is ISpawnBatchReset resettable)
            {
                resettable.ResetPlaced();
            }

            prepared = new PreparedSpawn(request, runtime, effectiveSeed, lifecycle, algorithm);
            return true;
        }

        private void ExecuteSpawnRange(in PreparedSpawn prepared, int startIndex, int endExclusive, List<GameObject> results)
        {
            for (int i = startIndex; i < endExclusive; i++)
            {
                int variantIndex = ResolveVariantIndex(prepared.runtime, prepared.request.variantPlan, prepared.effectiveSeed, i);
                if (variantIndex < 0 || variantIndex >= prepared.runtime.pools.Length) continue;

                var pool = prepared.runtime.pools[variantIndex];
                if (pool == null || !pool.IsReady) continue;

                prepared.algorithm.GetPose(i, prepared.effectiveSeed, out var position, out var rotation);
                var go = pool.Rent(prepared.request.parent, position, rotation, prepared.lifecycle);
                if (go != null)
                {
                    results.Add(go);
                }
            }
        }

        private IEnumerator SpawnAsyncRoutine(
            PreparedSpawn prepared,
            int maxPerFrame,
            CancellationToken cancellationToken,
            AsyncSpawnOperation operation)
        {
            int index = 0;

            while (index < prepared.request.count)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    CleanupCanceledAsyncSpawn(operation, cancellationToken);
                    yield break;
                }

                int endExclusive = Mathf.Min(prepared.request.count, index + maxPerFrame);
                ExecuteSpawnRange(prepared, index, endExclusive, operation.instances);
                index = endExclusive;

                if (index < prepared.request.count)
                {
                    yield return null;
                }
            }

            CompleteAsyncSpawn(operation, new SpawnHandle(this, operation.instances));
        }

        private void CompleteAsyncSpawn(AsyncSpawnOperation operation, SpawnHandle handle)
        {
            RemovePendingAsyncSpawn(operation);
            operation.completion.TrySetResult(handle);
        }

        private void CleanupCanceledAsyncSpawn(AsyncSpawnOperation operation, CancellationToken cancellationToken)
        {
            for (int i = 0; i < operation.instances.Count; i++)
            {
                Despawn(operation.instances[i]);
            }

            operation.instances.Clear();
            RemovePendingAsyncSpawn(operation);
            operation.completion.TrySetCanceled(cancellationToken);
        }

        private void CancelPendingAsyncSpawns()
        {
            for (int i = _pendingAsyncSpawns.Count - 1; i >= 0; i--)
            {
                var operation = _pendingAsyncSpawns[i];
                if (operation == null) continue;

                if (operation.coroutine != null)
                {
                    StopCoroutine(operation.coroutine);
                }

                operation.instances.Clear();
                operation.completion.TrySetCanceled();
            }

            _pendingAsyncSpawns.Clear();
        }

        private void RemovePendingAsyncSpawn(AsyncSpawnOperation operation)
        {
            _pendingAsyncSpawns.Remove(operation);
        }

        private void FlushPendingPoolHierarchy()
        {
            foreach (var runtime in _runtimes.Values)
            {
                if (runtime == null || runtime.pools == null) continue;

                for (int i = 0; i < runtime.pools.Length; i++)
                {
                    runtime.pools[i]?.FlushPendingReparents();
                }
            }
        }

        private SpawnableSO ResolveSpawnable(in SpawnRequest request)
        {
            if (request.spawnable != null) return request.spawnable;
            if (request.key.Hash == 0) return null;
            return FindSpawnableInCatalog(request.key);
        }

        private uint ResolveSeed(uint seed, SpawnableSO spawnable, Transform parent)
        {
            int contextHash = parent != null ? parent.GetHashCode() : 0;
            return SeedResolver.Resolve(seed, spawnable.defaultSeedMode, spawnable.key.Hash, contextHash);
        }

        private static int GetVariantCount(SpawnableSO spawnable)
        {
            if (spawnable == null) return 0;

            if (spawnable.sourceType == SpawnSourceType.Prefab)
                return spawnable.prefabVariants != null ? spawnable.prefabVariants.Count : 0;

#if USE_ADDRESSABLES
            if (spawnable.sourceType == SpawnSourceType.Addressables)
                return spawnable.addressVariants != null ? spawnable.addressVariants.Count : 0;
#endif

            return 0;
        }

        private static float GetVariantWeight(SpawnableSO spawnable, int variantIndex)
        {
            if (spawnable == null) return 0f;

            if (spawnable.sourceType == SpawnSourceType.Prefab)
            {
                if (spawnable.prefabVariants == null || variantIndex < 0 || variantIndex >= spawnable.prefabVariants.Count)
                    return 0f;

                return Mathf.Max(0f, spawnable.prefabVariants[variantIndex].weight);
            }

#if USE_ADDRESSABLES
            if (spawnable.sourceType == SpawnSourceType.Addressables)
            {
                if (spawnable.addressVariants == null || variantIndex < 0 || variantIndex >= spawnable.addressVariants.Count)
                    return 0f;

                return Mathf.Max(0f, spawnable.addressVariants[variantIndex].weight);
            }
#endif

            return 0f;
        }

        private ISpawnFactory CreateFactory(SpawnableSO spawnable, int variantIndex)
        {
            switch (spawnable.sourceType)
            {
                case SpawnSourceType.Prefab:
                {
                    var list = spawnable.prefabVariants;
                    if (list == null || list.Count == 0) return null;

                    variantIndex = Mathf.Clamp(variantIndex, 0, list.Count - 1);
                    var prefab = list[variantIndex].prefab;
                    if (prefab == null) return null;

                    return new PrefabSpawnFactory(prefab);
                }

                case SpawnSourceType.Addressables:
                {
#if USE_ADDRESSABLES
                    var list = spawnable.addressVariants;
                    if (list == null || list.Count == 0) return null;

                    variantIndex = Mathf.Clamp(variantIndex, 0, list.Count - 1);
                    var addr = list[variantIndex].addressKey;
                    if (string.IsNullOrEmpty(addr)) return null;

                    return new AddressablesSpawnFactory(addr);
#else
                    Debug.LogError("Addressables factory requested but USE_ADDRESSABLES is not defined.");
                    return null;
#endif
                }

                default:
                    return null;
            }
        }

        private static int PickVariantIndex(float[] weights, uint seed, uint salt)
        {
            if (weights == null || weights.Length == 0) return 0;
            return WeightedPicker.PickIndex(weights, seed, salt * 0x9E3779B9u);
        }

        private static int ResolveVariantIndex(SpawnRuntime runtime, int[] variantPlan, uint seed, int spawnIndex)
        {
            if (variantPlan != null)
            {
                return spawnIndex >= 0 && spawnIndex < variantPlan.Length
                    ? variantPlan[spawnIndex]
                    : -1;
            }

            return PickVariantIndex(runtime.weights, seed, (uint)(spawnIndex + 1));
        }

        private SpawnableSO FindSpawnableInCatalog(SpawnKey key)
        {
            if (catalog == null || catalog.items == null) return null;

            for (int i = 0; i < catalog.items.Count; i++)
            {
                var so = catalog.items[i];
                if (so == null) continue;
                if (so.key.Hash == key.Hash) return so;
            }

            return null;
        }
    }
}
