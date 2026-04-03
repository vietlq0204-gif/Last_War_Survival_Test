using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.ScriptableObjects;
using Vit.SpawnKit.Services;

namespace Vit.SpawnKit.Api
{
    /// <summary>
    /// Static facade for concise access to SpawnKit services.
    /// </summary>
    public static class SpawnKit
    {
        private static SpawnManager Service
        {
            get
            {
                if (SpawnManager.Instance != null) return SpawnManager.Instance;
                return Object.FindAnyObjectByType<SpawnManager>();
            }
        }

        #region  Spawn
        
        #region overload spawn one
        /// <summary>
        /// Spawns a single object at an exact position and rotation.
        /// </summary>
        public static GameObject SpawnOne(SpawnableSO spawnable, Vector3 position, Quaternion rotation, Transform parent = null, uint seed = 0, SpawnLifecycle? lifecycle = null)
        {
            var service = Service;
            return service != null
                ? service.SpawnOne(spawnable, position, rotation, parent, seed, lifecycle)
                : null;
        }
        #endregion
        
        #region overload spawn batch
        
        /// <summary>
        /// Spawn a batch from a <see cref="SpawnableSO"/>.
        /// </summary>
        /// <param name="spawnable">The asset define objects that need spawn</param>
        /// <param name="count">Decide max count that spawnable can spawn</param>
        /// <param name="parent">Where the objects in hierarchy after spawn</param>
        /// <param name="algorithm">The position where an object can be spawned in space. That coordinate is calculated from the origin, radius and seed.</param>
        /// <param name="seed">Controlled randomness</param>
        /// <param name="lifecycle">decide how to object return to the pool</param>
        /// <returns></returns>
        public static SpawnHandle Spawn(SpawnableSO spawnable, int count = 1, Transform parent = null, ISpawnAlgorithm algorithm = null, uint seed = 0, SpawnLifecycle? lifecycle = null)
        {
            var service = Service;
            return service != null
                ? service.Spawn(spawnable, count, parent, algorithm, seed, lifecycle)
                : null;
        }

        /// <summary>
                /// Spawns using a preset without collider input, typically at the parent transform or origin fallback.
                /// </summary>
        public static SpawnHandle Spawn(SpawnPresetSO preset, Transform parent = null)
                {
                    var service = Service;
                    return service != null
                        ? service.Spawn(preset, parent)
                        : null;
                }
        
        /// <summary>
        /// Spawns a batch from a <see cref="SpawnPresetSO"/>
        /// </summary>
        public static SpawnHandle Spawn(SpawnPresetSO preset, Collider volume, Transform parent = null)
        {
            var service = Service;
            return service != null
                ? service.Spawn(preset, volume, parent)
                : null;
        }

        /// <summary>
        /// Spawns using a preset across multiple collider volumes.
        /// </summary>
        public static SpawnHandle Spawn(SpawnPresetSO preset, Collider[] volumes, Transform parent = null)
                {
                    var service = Service;
                    return service != null
                        ? service.Spawn(preset, volumes, parent)
                        : null;
                }

        /// <summary>
        /// Spawns from any object that can build a <see cref="SpawnRequest"/>.
        /// </summary>
        public static SpawnHandle Spawn(ISpawnRequestSource source)
        {
            if (source == null) return null;

            var service = Service;
            return service != null
                ? service.Spawn(source)
                : null;
        }
        
        #endregion
        
        #region overload spawn batch async

        /// <summary>
        /// Spawns asynchronously using a preset without collider input.
        /// </summary>
        public static Task<SpawnHandle> SpawnAsync(SpawnPresetSO preset, Transform parent = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            var service = Service;
            return service != null
                ? service.SpawnAsync(preset, parent, maxPerFrame, cancellationToken)
                : Task.FromResult<SpawnHandle>(null);
        }
        
        /// <summary>
        /// Spawns a batch asynchronously across multiple frames from a <see cref="SpawnableSO"/>.
        /// </summary>
        public static Task<SpawnHandle> SpawnAsync(SpawnableSO spawnable, int count = 1, Transform parent = null, ISpawnAlgorithm algorithm = null, uint seed = 0, SpawnLifecycle? lifecycle = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            var service = Service;
            return service != null
                ? service.SpawnAsync(spawnable, count, parent, algorithm, seed, lifecycle, maxPerFrame,
                    cancellationToken)
                : Task.FromResult<SpawnHandle>(null);
        }

        /// <summary>
        /// Spawns asynchronously from a <see cref="SpawnPresetSO"/>.
        /// </summary>
        public static Task<SpawnHandle> SpawnAsync(SpawnPresetSO preset, Collider volume, Transform parent = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            var service = Service;
            return service != null
                ? service.SpawnAsync(preset, volume, parent, maxPerFrame, cancellationToken)
                : Task.FromResult<SpawnHandle>(null);
        }
        
        /// <summary>
        /// Spawns asynchronously using a preset across multiple collider volumes.
        /// </summary>
        public static Task<SpawnHandle> SpawnAsync(SpawnPresetSO preset, Collider[] volumes, Transform parent = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            var service = Service;
            return service != null
                ? service.SpawnAsync(preset, volumes, parent, maxPerFrame, cancellationToken)
                : Task.FromResult<SpawnHandle>(null);
        }
        
        /// <summary>
        /// Spawns asynchronously from any object that can build a <see cref="SpawnRequest"/>.
        /// </summary>
        public static Task<SpawnHandle> SpawnAsync(ISpawnRequestSource source, int maxPerFrame = 32, CancellationToken cancellationToken = default)
        {
            if (source == null) return Task.FromResult<SpawnHandle>(null);

            var service = Service;
            return service != null
                ? service.SpawnAsync(source, maxPerFrame, cancellationToken)
                : Task.FromResult<SpawnHandle>(null);
        }
        
        #endregion
        
        #endregion
        
        /// <summary>
        /// Return a spawn instance to it pool
        /// </summary>
        /// <param name="instance"></param>
        /// <returns></returns>
        public static bool Despawn(GameObject instance)
        {
            var service = Service;
            return service != null && service.Despawn(instance);
        }
        
        /// <summary>
        /// Prepare pool (the inactive instances) of <see cref="SpawnableSO"/> specifically
        /// </summary>
        /// <param name="spawnable"></param>
        public static void Prewarm(SpawnableSO spawnable)
        {
            var service = Service;
            service?.Register(spawnable);
        }

        /// <summary>
        /// Prepare pool (the inactive instances) of all <see cref="SpawnableSO"/> has been registered in <see cref="SpawnCatalogSO"/>
        /// </summary>
        public static void Prewarm(SpawnCatalogSO catalog)
        {
            var service = Service;
            service?.Prewarm(catalog);
        }
        
        /// <summary>
        /// Gets aggregated pool statistics for the spawnable referenced by a preset.
        /// </summary>
        public static PoolStats GetPoolStats(SpawnPresetSO preset)
        {
            return preset != null ? GetPoolStats(preset.spawnable) : default;
        }
        
        /// <summary>
        /// Attempts to get aggregated pool statistics for a spawnable.
        /// </summary>
        public static bool TryGetPoolStats(SpawnableSO spawnable, out PoolStats stats)
        {
            var service = Service;
            if (service == null)
            {
                stats = default;
                return false;
            }

            return service.TryGetPoolStats(spawnable, out stats);
        }        

        /// <summary>
        /// Trims inactive instances in pool from <see cref="SpawnPresetSO"/> , only retains specific count
        /// </summary>
        /// <remarks>It only works when pool has at least 2 instances</remarks>
        /// <param name="preset">batch for inactive instance need trims</param>
        /// <param name="keepInactive">count inactive instances need keep</param>
        /// <returns></returns>
        public static int Trim(SpawnPresetSO preset, int keepInactive = 0)
        {
            return preset != null ? Trim(preset.spawnable, keepInactive) : 0;
        }

        /// <summary>
        /// Clear all inactive instance in pool from <see cref="SpawnPresetSO"/>
        /// </summary>
        /// <param name="preset"></param>
        /// <returns></returns>
        public static int Clear(SpawnPresetSO preset)
        {
            return preset != null ? Clear(preset.spawnable) : 0;
        }

        /// <summary>
        /// Similar to <c>Trims</c> , but it retains the default inactive instances define by <see cref="SpawnPresetSO"/>
        /// </summary>
        /// <param name="preset"></param>
        /// <remarks>It only works when bool has more instances than count define by <see cref="SpawnPresetSO"/></remarks>
        public static int ReleaseUnused(SpawnPresetSO preset)
        {
            return preset != null ? ReleaseUnused(preset.spawnable) : 0;
        }
        
        
        private static PoolStats GetPoolStats(SpawnableSO spawnable)
        {
            var service = Service;
            return service != null ? service.GetPoolStats(spawnable) : default;
        }
        
        private static int Trim(SpawnableSO spawnable, int keepInactive = 0)
        {
            var service = Service;
            return service != null ? service.Trim(spawnable, keepInactive) : 0;
        }
        
        private static int Clear(SpawnableSO spawnable)
        {
            var service = Service;
            return service != null ? service.Clear(spawnable) : 0;
        }
        
        private static int ReleaseUnused(SpawnableSO spawnable)
        {
            var service = Service;
            return service != null ? service.ReleaseUnused(spawnable) : 0;
        }
        
    }
}