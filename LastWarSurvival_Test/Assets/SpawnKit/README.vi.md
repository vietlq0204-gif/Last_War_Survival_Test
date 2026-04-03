[English](./README.md) | [Tieng Viet](./README.vi.md)

# Vit.SpawnKit

`Vit.SpawnKit` là Toolkit Spawn và pooling gọn nhẹ cho Unity, với 3 ý tưởng chính :

- Định nghĩa data spawn bằng `ScriptableObject`
- Tái sử dụng instance thông qua pool thay vì `Instantiate`/`Destroy` liên tục
- Giữ code gameplay ngắn gọn với facade tỉnh `SpawnKit`

Package tương thích với Unity `2022.3`. Tập trung vào runtime spawning với pool đươc back bởi prefab.

## Bao gồm

- `SpawnManager` dịch vụ runtime để đăng kí spawnable, pooling, spawn và despawn
- `SpawnKit` static facade cho gameplay dùng
- `SpawnableSO` định nghĩa nguồn spawn, pool config, lifecycle mặc định và cách chọn variant
- `SpawnCatalogSO` auto-register và prewarm
- `SpawnPresetSO` cho các cấu hình spawn tái sử dụng được
- API spawn batch đồng bộ và bất đồng bộ
- timed despawn va lifecycle trả object về pool khi `OnDisable`
- callback cho pooled instance qua `ISpawnPoolCallbacks`
- API quản lí pool: stats, trim, clear, release-unused

## Phạm vi

Package hien da dung duoc cho runtime spawning dua tren prefab.

Gioi han hien tai cua package:

- `SpawnSourceType.Addressables` co xuat hien trong API, nhung package nay chi co cac nhanh code dieu kien duoc guard boi `USE_ADDRESSABLES`. Chua co `AddressablesSpawnFactory` trong package nay.
- Package chua co custom editor tool. Viec setup hien duoc lam bang inspector mac dinh cua Unity.
- Sample trong repo nay nam ngoai package, tai `Assets/SpawnModule/Sample`.

## API

Facade public chinh la `Vit.SpawnKit.Api.SpawnKit`.

### Spawn

```csharp
// data lấy thông qua Interface (ISpawnRequestSource)
Spawn(ISpawnRequestSource source)
SpawnAsync(ISpawnRequestSource source, int maxPerFrame = 32, CancellationToken cancellationToken = default)

SpawnOne(SpawnableSO spawnable, Vector3 position, Quaternion rotation, Transform parent = null, uint seed = 0, SpawnLifecycle? lifecycle = null)

// data lấy trực tiếp từ SpawnableSO
Spawn(SpawnableSO spawnable, int count = 1, Transform parent = null, ISpawnAlgorithm algorithm = null, uint seed = 0, SpawnLifecycle? lifecycle = null)
SpawnAsync(SpawnableSO spawnable, int count = 1, Transform parent = null, ISpawnAlgorithm algorithm = null, uint seed = 0, SpawnLifecycle? lifecycle = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)

// data lấy từ SpawnPresetSO
Spawn(SpawnPresetSO preset, Transform parent = null)
SpawnAsync(SpawnPresetSO preset, Transform parent = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)

// data lấy từ SpawnPresetSO để rải trong volume (Collider)
Spawn(SpawnPresetSO preset, Collider volume, Transform parent = null)
SpawnAsync(SpawnPresetSO preset, Collider volume, Transform parent = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)

// data lấy từ SpawnPresetSO để rải trong volume[] (Collider)
Spawn(SpawnPresetSO preset, Collider[] volumes, Transform parent = null)
SpawnAsync(SpawnPresetSO preset, Collider[] volumes, Transform parent = null, int maxPerFrame = 32, CancellationToken cancellationToken = default)
```

### Despawn

```csharp
// trả một instance cụ thể về pool (thủ công)
SpawnHandle.Despawn(GameObject instance)
    
// trả tất cả instance thuộc quản lí của một SpawnHandle về pool
SpawnHandle.Despawn()
```

Dung nhom API nay khi lifecycle la `Manual`, hoac khi ban muon tra ve pool ca mot batch da spawn.

### Prewarm va dang ky pool

```csharp
Prewarm(SpawnableSO spawnable)
Prewarm(SpawnCatalogSO catalog)
```

Nhung API nay se tao pool som va prewarm cac instance inactive theo `PoolConfig` cua tung `SpawnableSO`.

### Thong ke pool

```csharp
TryGetPoolStats(SpawnableSO spawnable, out PoolStats stats)
GetPoolStats(SpawnPresetSO preset)
```

`PoolStats` gom 4 gia tri:

- `active`
- `inactive`
- `total`
- `max`

### Bao tri pool

```csharp
Trim(SpawnableSO spawnable, int keepInactive = 0)
Clear(SpawnableSO spawnable)
ReleaseUnused(SpawnableSO spawnable)

Trim(SpawnPresetSO preset, int keepInactive = 0)
Clear(SpawnPresetSO preset)
ReleaseUnused(SpawnPresetSO preset)
```

Dung cac API nay de:

- thu nho pool nhung van giu lai so inactive can thiet
- xoa toan bo inactive instance
- giam bo nho ve muc prewarm duoc cau hinh



## Cach Setup Trong Unity

### Buoc 1. Tao asset

Tao cac asset sau tu menu `Assets/Create/SpawnKit` trong Unity:

- `Spawnable`
- `Spawn Catalog`
- `Spawn Preset`

### Buoc 2. Cau hinh `SpawnableSO`

Can set:

- `Key`: id logic de lookup va debug
- `Source Type`: `Prefab`
- `Prefab Variants`: them mot hoac nhieu prefab
- `Pool Config`: cau hinh prewarm va kich thuoc pool
- `Default Lifecycle`: chon `Manual`, `OnDisable`, hoac `AfterSeconds`

### Buoc 3. Tao catalog

Them cac `SpawnableSO` cua ban vao `SpawnCatalogSO`.

### Buoc 4. Them `SpawnManager` vao scene

Tao mot empty GameObject, them `SpawnManager`, sau do gan:

- `Catalog`: tuy chon nhung nen dung
- `Pools Root`: tuy chon; neu de trong manager se tu tao

### Buoc 5. Tuy chon setup preset

Tao `SpawnPresetSO` khi ban muon co cau hinh spawn tai su dung duoc, dac biet la collider-volume spawning.

## Bat Dau Nhanh

### Spawn 1 object tai vi tri/rotation co dinh

```csharp
using UnityEngine;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.ScriptableObjects;

public sealed class SpawnOneExample : MonoBehaviour
{
    [SerializeField] private SpawnableSO spawnable;

    private void Start()
    {
        SpawnKit.SpawnOne(
            spawnable,
            position: transform.position,
            rotation: Quaternion.identity,
            parent: null,
            seed: 0,
            lifecycle: SpawnLifecycle.Manual);
    }
}
```

### Spawn theo batch

```csharp
using UnityEngine;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.ScriptableObjects;

public sealed class BatchSpawnExample : MonoBehaviour
{
    [SerializeField] private SpawnableSO spawnable;

    private void Start()
    {
        var handle = SpawnKit.Spawn(
            spawnable,
            count: 20,
            parent: transform,
            algorithm: new SimplePointAlgorithm(transform.position, 5f));

        // Giu handle neu ban muon despawn ca batch sau nay.
        Debug.Log(handle.IsEmpty ? "Khong spawn duoc object nao" : $"Da spawn {handle.Instances.Count} object");
    }
}
```

### Spawn tu preset vao mot hoac nhieu collider

```csharp
using UnityEngine;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.ScriptableObjects;

public sealed class PresetVolumeSpawnExample : MonoBehaviour
{
    [SerializeField] private SpawnPresetSO preset;
    [SerializeField] private Collider[] volumes;

    private void Start()
    {
        SpawnKit.Spawn(preset, volumes, transform);
    }
}
```

### Async spawn cho batch lon

```csharp
using System.Threading.Tasks;
using UnityEngine;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.ScriptableObjects;

public sealed class AsyncSpawnExample : MonoBehaviour
{
    [SerializeField] private SpawnableSO spawnable;

    private async void Start()
    {
        var handle = await SpawnKit.SpawnAsync(
            spawnable,
            count: 500,
            parent: transform,
            algorithm: new SimplePointAlgorithm(transform.position, 20f),
            maxPerFrame: 32);

        Debug.Log($"Da spawn bat dong bo {handle.Instances.Count} pooled object");
    }
}
```

## Spawn Trong Volume

`SpawnPresetSO` su dung cac placement algorithm co nhan dien collider.

Cac hanh vi san co:

- `ColliderVolumeAlgorithm`: dat object ben trong mot collider
- `MultiColliderVolumeAlgorithm`: chia se tong so luong spawn qua nhieu collider
- `PerVolumeColliderVolumeAlgorithm`: ap dung so luong spawn cho tung collider

Mot so field quan trong trong preset:

- `MaxCount`: so object toi da trong mot request
- `multiVolumeCountMode`
- `maxTryPerPoint`
- `candidatesPerPoint`
- `minDistance`
- `placementBufferCapacity`

Khi khong truyen collider, preset se fallback ve fixed pose tai parent duoc chon hoac world origin.

Voi `Spawn(...)` truc tiep neu khong truyen algorithm, manager se fallback ve `SimplePointAlgorithm` co ban kinh `0` tai world origin. Trong thuc te, nen truyen algorithm moi khi viec dat vi tri theo batch la quan trong.

## Pooling Lifecycle

Moi lan spawn co the dung mot lifecycle policy:

- `Manual`: tra ve pool thu cong bang `SpawnKit.Despawn(...)` hoac `SpawnHandle.DespawnAll()`
- `OnDisable`: object tu tra ve pool khi bi disable
- `AfterSeconds`: object tu tra ve sau mot khoang thoi gian, theo scaled hoac unscaled time

Ban co the dinh nghia lifecycle:

- tren `SpawnableSO` nhu gia tri mac dinh
- tren `SpawnPresetSO` nhu mot override
- tren tung request thong qua cac API overload nhan `SpawnLifecycle`

## Pool Callback

Neu prefab can cac hook de reset state, hay implement `ISpawnPoolCallbacks` tren component trong prefab hierarchy.

```csharp
using UnityEngine;
using Vit.SpawnKit.Pooling;

public sealed class BulletView : MonoBehaviour, ISpawnPoolCallbacks
{
    public void OnSpawnedFromPool()
    {
        // Reset runtime state tai day.
    }

    public void OnDespawnedToPool()
    {
        // Dung effect, clear reference, v.v.
    }
}
```

## Vi Du Quan Ly Pool

Ban co the kiem tra va quan ly pool thong qua facade:

```csharp
using UnityEngine;
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.ScriptableObjects;

public sealed class PoolMaintenanceExample : MonoBehaviour
{
    [SerializeField] private SpawnableSO spawnable;

    private void Start()
    {
        SpawnKit.Prewarm(spawnable);

        PoolStats stats = SpawnKit.GetPoolStats(spawnable);
        Debug.Log($"Active={stats.active}, Inactive={stats.inactive}, Total={stats.total}, Max={stats.max}");

        SpawnKit.ReleaseUnused(spawnable);
        SpawnKit.Trim(spawnable, keepInactive: 8);
        SpawnKit.Clear(spawnable);
    }
}
```

Helper san co:

- `SpawnKit.Prewarm(SpawnableSO spawnable)`
- `SpawnKit.Prewarm(SpawnCatalogSO catalog)`
- `SpawnKit.GetPoolStats(...)`
- `SpawnKit.TryGetPoolStats(...)`
- `SpawnKit.Trim(...)`
- `SpawnKit.Clear(...)`
- `SpawnKit.ReleaseUnused(...)`

## Dung `SpawnRequest` Truc Tiep

Cho cac truong hop nang cao, ban co the tu tao `SpawnRequest` hoac implement `ISpawnRequestSource`.

Phu hop khi:

- ban muon viet `ISpawnAlgorithm` rieng
- ban muon spawn theo `SpawnKey`
- ban can lifecycle override o muc request
- ban muon cap request data tu mot gameplay system khac

## Namespace

Nhung namespace thuong dung:

```csharp
using Vit.SpawnKit.Api;
using Vit.SpawnKit.Algorithms;
using Vit.SpawnKit.Data;
using Vit.SpawnKit.Pooling;
using Vit.SpawnKit.ScriptableObjects;
```

## Sample Trong Repo Nay

Repo nay co mot demo toi gian nam ngoai package:

- `Assets/SpawnModule/Sample/Scene/SpawnTest.unity`
- `Assets/SpawnModule/Sample/SomethingZone_Spawner_Demo.cs`

Demo do goi:

```csharp
SpawnKit.Spawn(data, volume);
```

trong do `data` la `SpawnPresetSO` va `volume` la mang collider.

## Cach Dung De Xuat

Voi phan lon project, luong setup gon nhat la:

1. Dinh nghia du lieu spawn tai su dung trong `SpawnableSO`.
2. Dua cac asset do vao `SpawnCatalogSO`.
3. Them mot `SpawnManager` vao active scene.
4. Dung `SpawnPresetSO` cho cac rule spawn do designer cau hinh.
5. Goi facade `SpawnKit` tu gameplay code.

## Trang Thai

Package van dang o giai doan som, nhung runtime flow hien tai da du on cho:

- prefab-based pooled spawning
- batch spawning
- volume spawning
- async batched spawning
- lifecycle-driven despawn

Neu ban dinh publish package rong hon, nhung cai tien hop ly tiep theo la:

- dua sample vao ben trong package
- them editor tooling de setup nhanh hon
- bo sung test cho pool behavior va async spawning
- tich hop Addressables that su khi `USE_ADDRESSABLES` duoc bat
