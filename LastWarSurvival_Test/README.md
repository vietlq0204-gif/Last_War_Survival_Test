# GDD Nội Bộ - Last War Survival Test

## 1. Thông tin tài liệu

- Tên dự án: `Last War Survival Test`
- Tác giả tài liệu: `Le Quoc Viet`
- Ngày cập nhật: `2026-04-09`
- Video/game tham khảo: `Last War: Survival`
- Scene mục tiêu để đọc và nghiệm thu prototype: `Assets/Scenes/Demo (Play here).unity`
- Scene phụ trợ: `Assets/Scenes/Test.unity` chỉ nên xem như scene test/debug
- Mục đích tài liệu: mô tả ngắn gọn nhưng đủ chính xác về concept, mechanics, progression flow và cấu trúc hệ thống để team có thể dùng ngay cho việc trao đổi, chia task và phát triển prototype

## 2. Mục tiêu bài tập

Prototype này được chọn theo hướng tái hiện lại cảm giác cốt lõi của `Last War: Survival`: dễ hiểu ngay từ giây đầu, thao tác tối giản, phản hồi chiến đấu trực quan và có nhịp tăng sức mạnh rõ rệt trong một lượt chơi ngắn.

Mục tiêu của prototype hiện tại là kiểm chứng:

- Người chơi có thể hiểu cách chơi gần như ngay lập tức chỉ với thao tác kéo ngang.
- Vòng lặp `tăng quân số -> tăng mật độ bắn -> phá obstacle -> nhận reward -> chống enemy wave` có đủ rõ và đủ thỏa mãn hay không.
- Hạ tầng kỹ thuật cho spawn, formation, path và combat có đủ sạch để mở rộng thành level hoàn chỉnh hay không.

## 3. Những điểm ấn tượng của trò chơi được chọn và lý do lựa chọn

`Last War: Survival` là một mẫu game rất đáng chọn cho bài tập prototype vì nó hội tụ nhiều phẩm chất tốt của mobile action game hiện đại nhưng vẫn giữ được cấu trúc đủ gọn để phân tích và tái tạo nhanh.

Những điểm ấn tượng chính:

- Khả năng đọc hiểu cực nhanh:
  - Người chơi gần như hiểu luật ngay khi nhìn gameplay: kéo để đổi vị trí, đội hình tự bắn, nhặt thêm quân để mạnh lên.
- Cảm giác tăng sức mạnh rất rõ:
  - Sức mạnh không chỉ là con số ẩn trong UI mà được biểu hiện trực tiếp qua số quân, mật độ đạn và tốc độ dọn đường.
- Nhịp phần thưởng dày:
  - Cứ sau một quãng ngắn người chơi lại có một phần thưởng hoặc một quyết định, ví dụ chọn lane, ăn card, phá obstacle hay nhận weapon pickup.
- Tính giải trí thị giác tốt:
  - Dù thao tác đơn giản, màn hình luôn có chuyển động, projectile, kẻ địch và phản hồi va chạm, rất phù hợp với dạng game ngắn và gameplay quảng cáo.
- Cấu trúc hệ thống phù hợp để làm prototype:
  - Core loop của game có thể chia thành các module rõ ràng như movement, spawn, formation, combat, reward và game flow.

Lý do lựa chọn cho bài tập:

- Dễ giải thích cho team và dễ chứng minh mình hiểu core loop.
- Phù hợp để dựng prototype trong phạm vi ngắn mà vẫn thể hiện được tư duy system design.
- Có nhiều hướng mở rộng về content sau khi hoàn thành bản cơ bản.
- Là một thể loại phổ biến trên store nên có giá trị tham khảo tốt về market readability và retention loop.

## 4. Yếu tố thành công và điểm mạnh

Các yếu tố khiến mẫu game này có khả năng thành công tốt trên thị trường casual/mobile:

- Skill floor thấp:
  - Input đơn giản giúp người chơi mới vào game không bị ngợp.
- Core loop rõ và lặp lại tốt:
  - Tăng quân, tăng hỏa lực, sống sót lâu hơn là một vòng lặp rất dễ cảm nhận.
- Reward feedback mạnh:
  - Mỗi lần nhặt card hoặc đổi vũ khí đều tạo ra thay đổi trực quan ngay lập tức.
- Session ngắn nhưng vẫn có nhịp leo thang:
  - Phù hợp với thói quen chơi mobile, đặc biệt trong các phiên chơi ngắn.
- Dễ làm UA/creative:
  - Gameplay có tính "nhìn là hiểu", rất hợp cho trailer ngắn, playable ad và video hook.
- Dễ mở rộng content:
  - Có thể thêm nhiều loại enemy, weapon, card, obstacle và mode mà không cần phá vỡ loop cốt lõi.

Điểm mạnh nổi bật về design:

- Trực quan:
  - Sức mạnh và rủi ro đều thể hiện trực tiếp trên màn hình.
- Thỏa mãn:
  - Từ trạng thái yếu ban đầu đến trạng thái áp đảo tạo ra progression rất dễ gây nghiện.
- Gọn:
  - Chỉ với vài hệ thống chính đã đủ tạo ra một trải nghiệm hoàn chỉnh ở mức prototype.
- Linh hoạt:
  - Core loop cho phép scale lên nhiều hướng như level-based, endless, meta-upgrade hoặc hybrid strategy.

## 5. Tóm tắt concept

Đây là game hành động bắn súng theo lane/road, trong đó người chơi điều khiển một đội lính di chuyển ngang trên mặt đường. Đội hình tự động bắn về phía trước, liên tục xử lý enemy, phá obstacle trên đường, nhặt card cộng quân và nhận weapon pickup để gia tăng hỏa lực.

Fantasy cốt lõi của game là:

- Bắt đầu rất yếu với chỉ 1 đơn vị.
- Tăng sức mạnh theo cách nhìn thấy ngay trên màn hình.
- Đội hình càng đông thì cảm giác áp đảo càng rõ.
- Mỗi vài giây đều có một quyết định hoặc phần thưởng đủ để giữ nhịp hứng thú.

## 6. Trụ cột thiết kế

### 4.1. Readability ngay lập tức

- Người chơi chỉ cần hiểu 2 việc: kéo ngang để căn vị trí và để đội hình tự bắn.
- Sức mạnh của người chơi phải thể hiện trực tiếp qua số quân, mật độ đạn và vũ khí đang cầm.

### 4.2. Tăng trưởng ngắn hạn rõ ràng

- Card cộng quân tạo tăng trưởng tức thời.
- Reward vũ khí tạo power spike rõ rệt hơn card thường.

### 4.3. Áp lực leo thang liên tục

- Enemy không đứng yên mà tiến dần về Home.
- Áp lực tăng bằng mật độ wave và sự pha trộn giữa enemy nhỏ với enemy trâu.

### 4.4. Prototype-first

- Ưu tiên giải pháp kỹ thuật nhanh, dễ mở rộng, dễ kiểm tra trong editor.
- Tách hệ thống bằng event và hạ tầng spawn chung để tránh logic dính cứng.

## 7. Cơ chế gameplay cốt lõi

### 5.1. Điều khiển

- Người chơi kéo đội hình sang trái/phải trên trục ngang.
- Người chơi không điều khiển tiến/lùi; dòng gameplay tiến triển nhờ path và spawn setup của level.

### 5.2. Tấn công

- Đội người chơi tự động bắn đạn liên tục.
- Vũ khí hiện tại quyết định tốc độ bắn, loại đạn, damage và visual.

### 5.3. Tăng quân số

- Card xanh khi chạm vào sẽ cộng thêm teammate.
- Trong bản hiện tại, card đang dùng giá trị cộng `+1`.

### 5.4. Obstacle và reward

- Obstacle có HP và bị phá bởi đạn của người chơi.
- Một số obstacle có thể rơi reward item.
- Reward quan trọng hiện tại là pickup đổi sang `Gatling Gun`.

### 5.5. Enemy pressure

- Enemy được spawn theo cụm trên grid, di chuyển theo path về phía Home.
- Khi enemy chạm vùng Home, đội người chơi bị trừ HP theo cơ chế batch damage.

### 5.6. Điều kiện kết thúc

- Prototype hiện tại có điều kiện thua rõ ràng: đội người chơi về `0` quân.
- Chưa có điều kiện thắng hoàn chỉnh.
- Chưa có boss đúng nghĩa trong source hiện tại.

## 8. Progression flow của một lượt chơi

### 6.1. Mở đầu

- Scene vào trạng thái pause và hiện panel Start.
- Khi nhấn Start, game bắt đầu với `1` teammate.
- Mục đích của đoạn đầu là dạy thao tác kéo ngang và cho người chơi thấy đội hình tự bắn.

### 6.2. Giai đoạn học tăng trưởng cơ bản

- Người chơi tiếp cận card cộng quân đầu tiên.
- Sau vài lần nhặt card, đội hình đông hơn, vùng bắn rộng hơn và cảm giác sức mạnh tăng lên rõ.

### 6.3. Giai đoạn thêm quyết định chiến đấu

- Obstacle bắt đầu trở thành mục tiêu cần ưu tiên bắn.
- Người chơi hiểu rằng ngoài việc sống sót, họ còn cần chủ động phá obstacle để tìm reward.

### 6.4. Giai đoạn power spike

- Khi phá đúng obstacle có reward, người chơi nhận pickup đổi vũ khí.
- Từ đây loop chiến đấu trở nên mạnh và nhanh hơn rõ rệt.

### 6.5. Giai đoạn áp lực leo thang

- Enemy xuất hiện liên tục theo nhiều cụm trên path.
- Có sự pha trộn enemy nhỏ và enemy trâu để ép đội người chơi phải giữ quân số và tối ưu vị trí bắn.

### 6.6. Kết thúc lượt chơi

- Khi tổng HP đội hình bị trừ hết, toàn bộ teammate bị loại bỏ và game chuyển sang Game Over.
- Người chơi có thể restart để vào lượt mới.

## 9. Các hệ thống cốt lõi của prototype và mapping sang mã nguồn

Phần này liệt kê các hệ thống cần thiết của prototype, vai trò của từng hệ thống, và class/source chính đang chịu trách nhiệm triển khai.

| Hệ thống | Vai trò trong prototype | Mã nguồn chính |
| --- | --- | --- |
| Game Flow / UI | Điều khiển trạng thái `Start -> Play -> Game Over -> Restart`, pause đầu màn, panel Start/Game Over | [`Core`](Assets/Script/Manager/Core.cs), [`EventHub`](Assets/Script/EventHub/EventHub.cs) |
| Event Bus | Tách các hệ thống bằng event thay vì gọi trực tiếp lẫn nhau | [`EventHub`](Assets/Script/EventHub/EventHub.cs), [`EventBinder`](Assets/Script/EventHub/EventBinder.cs), [`CoreEventBase`](Assets/Script/EventHub/CoreEventBase.cs) |
| Spawn Framework | Cung cấp API spawn, pooling, lifecycle thống nhất cho enemy, bullet, teammate, obstacle, card | [`SpawnKit`](Assets/SpawnKit/Runtime/API/SpawnKit.cs), [`SpawnManager`](Assets/SpawnKit/Runtime/Services/SpawnManager.cs), [`SpawnRequest`](Assets/SpawnKit/Runtime/Data/SpawnRequest.cs), [`SpawnLifecycle`](Assets/SpawnKit/Runtime/Data/SpawnLifecycle.cs), [`SpawnPresetSO`](Assets/SpawnKit/Runtime/ScriptableObjects/SpawnPresetSO.cs), [`SpawnableSO`](Assets/SpawnKit/Runtime/ScriptableObjects/SpawnableSO.cs) |
| Path Authoring | Bake các điểm để object có thể di chuyển theo đường định sẵn trong level | [`PointBaker`](Assets/Script/Map/PointBaker.cs) |
| Path Movement | Kéo card stream hoặc enemy group chạy theo path | [`ObjectOnPathController`](Assets/Script/Controller/ObjectOnPathController.cs), [`CardAddQuantityOnPathController`](Assets/Script/CartAddQuantity/CardAddQuantityOnPathController.cs), [`EnemyGridGroupOnPathController`](Assets/Script/Enemy/EnemyGridGroupOnPathController.cs) |
| Formation / Grid Placement | Giữ đội hình teammate và enemy ở các slot rõ ràng, tránh chồng chéo | [`ColliderSurfaceGridZone`](Assets/SpawnKit/Runtime/Components/ColliderSurfaceGridZone.cs), [`ColliderSurfaceGridAlgorithm`](Assets/SpawnKit/Runtime/Algorithms/ColliderSurfaceGridAlgorithm.cs), [`FormationCenterSpawnToGridAlgorithm`](Assets/SpawnKit/Runtime/Algorithms/FormationCenterSpawnToGridAlgorithm.cs), [`SpawnGridQueue`](Assets/Script/Spawn/SpawnGridQueue.cs) |
| Player Squad Control | Điều khiển kéo ngang, tổng HP, equip weapon, game-over check của đội người chơi | [`TeammateController`](Assets/Script/Teammate/TeammateController.cs), [`Teammate`](Assets/Script/Teammate/Teammate.cs), [`TeammateSpawner`](Assets/Script/Teammate/TeammateSpawner.cs) |
| Card Add Quantity | Spawn card trên lane, trigger khi chạm player, cộng thêm teammate | [`CardAddQuantity`](Assets/Script/CartAddQuantity/CardAddQuantity.cs), [`CardAddQuantitySO`](Assets/Script/CartAddQuantity/CardAddQuantitySO.cs), [`CardAddQuantityCollisionRelay`](Assets/Script/CartAddQuantity/CardAddQuantityCollisionRelay.cs), [`CardAddQuantitySpawnZone`](Assets/Script/CartAddQuantity/CardAddQuantitySpawnZone.cs) |
| Weapon / Reward Pickup | Rơi vật phẩm sau khi phá obstacle và chuyển weapon cho đội người chơi | [`WeaponPickupItem`](Assets/Script/Weapon/WeaponPickupItem.cs), [`WeaponSO`](Assets/Script/Weapon/WeaponSO.cs), [`RewardSO`](Assets/Script/Reward/RewardSO.cs), [`ItemSO`](Assets/Script/Reward/ItemSO.cs) |
| Bullet / Ranged Combat | Tạo volley đạn, quản lý damage, hit detection và tự hủy | [`BulletSpawner`](Assets/Script/Bullet/BulletSpawner.cs), [`Bullet`](Assets/Script/Bullet/Bullet.cs), [`BulletSO`](Assets/Script/Bullet/BulletSO.cs), [`BulletSpawnerHomeRelay`](Assets/Script/Bullet/BulletSpawnerHomeRelay.cs) |
| Obstacle | Spawn obstacle trên đường, nhận damage, chết và phát reward | [`ObstacleSpawner`](Assets/Script/Obstacle/ObstacleSpawner.cs), [`Obstacle`](Assets/Script/Obstacle/Obstacle.cs), [`ObstacleSO`](Assets/Script/Obstacle/ObstacleSO.cs) |
| Enemy Wave / Lane Pressure | Spawn enemy theo grid, gom thành group, di chuyển trên path, recycle wave | [`EnemySpawner`](Assets/Script/Enemy/EnemySpawner.cs), [`Enemy`](Assets/Script/Enemy/Enemy.cs), [`EnemyGridGroup`](Assets/Script/Enemy/EnemyGridGroup.cs), [`EnemyGridGroupPathLane`](Assets/Script/Enemy/EnemyGridGroupPathLane.cs), [`EnemyGridPathItem`](Assets/Script/Enemy/EnemyGridPathItem.cs) |
| Home Interaction / Damage Routing | Xác định enemy đã chạm Home và gom damage trả về đội người chơi | [`HomeController`](Assets/Script/Home/HomeController.cs), [`HomeControllerRelay`](Assets/Script/Home/HomeControllerRelay.cs), [`EnemyHomeTargetService`](Assets/Script/Enemy/EnemyHomeTargetService.cs), [`EnemyHomeTargetRelay`](Assets/Script/Enemy/EnemyHomeTargetRelay.cs), [`EventHub`](Assets/Script/EventHub/EventHub.cs) |
| Feedback / Camera | Camera follow, camera shake, hit shake trên object | [`CameraFollowTaggedTarget`](Assets/Script/Controller/CameraFollowTaggedTarget.cs), [`CameraEventShake`](Assets/Script/Feedback/CameraEventShake.cs), [`DamageShakeFeedback`](Assets/Script/Feedback/DamageShakeFeedback.cs) |
| Pool-safe Despawn | Gom despawn theo batch để giảm chi phí runtime | [`BufferedPoolDespawnQueue`](Assets/Script/Spawn/BufferedPoolDespawnQueue.cs), [`ObjectSpawned`](Assets/Script/Spawn/ObjectSpawned.cs) |
| Editor Tooling cho level | Tool hỗ trợ vẽ/làm việc với grid zone trong editor | [`ColliderSurfaceGridZoneEditor`](Assets/Editor/ColliderSurfaceGridZoneEditor.cs) |

## 10. Giải thích logic code cốt lõi theo từng flow

Phần này không mô tả lại game design ở mức khái niệm, mà giải thích ngắn gọn cách logic đang chạy trong source hiện tại.

| Flow / Logic | Cách chạy trong runtime hiện tại | Mã nguồn chính |
| --- | --- | --- |
| Start game | Khi mở scene, `Core` hiện panel Start và dừng thời gian. Người chơi nhấn Start thì `Core` raise event `gameStart`, ẩn panel và tiếp tục gameplay. | [`Core`](Assets/Script/Manager/Core.cs), [`EventHub`](Assets/Script/EventHub/EventHub.cs) |
| Kéo ngang đội hình | `TeammateController` đọc input kéo, đổi sang vị trí đích theo trục X, clamp trong phạm vi của road collider và nội suy đội hình tới vị trí mới. | [`TeammateController`](Assets/Script/Teammate/TeammateController.cs) |
| Khởi tạo đội hình ban đầu | `TeammateSpawner` spawn số lượng teammate đầu tiên khi game bắt đầu. Ở scene `Demo`, số lượng khởi tạo là `1`. | [`TeammateSpawner`](Assets/Script/Teammate/TeammateSpawner.cs), [`Core`](Assets/Script/Manager/Core.cs) |
| Cộng quân khi ăn card | `CardAddQuantityCollisionRelay` phát sự kiện va chạm khi player chạm card. `TeammateSpawner` nhận event này, đọc data từ `CardAddQuantitySO` và queue spawn thêm teammate vào formation. | [`CardAddQuantityCollisionRelay`](Assets/Script/CartAddQuantity/CardAddQuantityCollisionRelay.cs), [`CardAddQuantity`](Assets/Script/CartAddQuantity/CardAddQuantity.cs), [`CardAddQuantitySO`](Assets/Script/CartAddQuantity/CardAddQuantitySO.cs), [`TeammateSpawner`](Assets/Script/Teammate/TeammateSpawner.cs), [`EventHub`](Assets/Script/EventHub/EventHub.cs) |
| Sắp slot đội hình | Mỗi teammate mới không được đặt tự do mà đi qua `SpawnGridQueue` và grid algorithm để lấy slot hợp lệ. Cách này giữ đội hình ổn định khi tăng hoặc mất quân. | [`SpawnGridQueue`](Assets/Script/Spawn/SpawnGridQueue.cs), [`ColliderSurfaceGridZone`](Assets/SpawnKit/Runtime/Components/ColliderSurfaceGridZone.cs), [`ColliderSurfaceGridAlgorithm`](Assets/SpawnKit/Runtime/Algorithms/ColliderSurfaceGridAlgorithm.cs) |
| Auto-fire của đội người chơi | `TeammateController` quyết định khi nào đội được bắn, còn `BulletSpawner` tạo volley đạn theo `WeaponSO` hiện tại. Vũ khí mặc định bắn chu kỳ `0.2s`; Gatling Gun bắn nhanh hơn với chu kỳ `0.1s`. | [`TeammateController`](Assets/Script/Teammate/TeammateController.cs), [`BulletSpawner`](Assets/Script/Bullet/BulletSpawner.cs), [`WeaponSO`](Assets/Script/Weapon/WeaponSO.cs) |
| Spawn và bay của đạn | `BulletSpawner` tạo bullet theo preset/pool, đặt vị trí bắn dựa trên bề rộng đội hình. `Bullet` tự di chuyển, kiểm tra overlap theo layer mask và despawn khi trúng mục tiêu hoặc hết thời gian sống. | [`BulletSpawner`](Assets/Script/Bullet/BulletSpawner.cs), [`Bullet`](Assets/Script/Bullet/Bullet.cs), [`BulletSO`](Assets/Script/Bullet/BulletSO.cs) |
| Damage lên enemy | Khi trúng enemy, `Bullet` gọi vào target để trừ HP. Enemy nhỏ hiện có HP thấp và chết nhanh, dùng để tạo cảm giác dọn đám đông. | [`Bullet`](Assets/Script/Bullet/Bullet.cs), [`Enemy`](Assets/Script/Enemy/Enemy.cs), [`EnemyHealthBarWorld`](Assets/Script/Enemy/EnemyHealthBarWorld.cs) |
| Damage lên obstacle | Đạn của player cũng dùng cùng luồng hit để gây damage lên obstacle. Điều này giữ combat loop đơn giản: người chơi không cần thêm nút bắn riêng cho object trên đường. | [`Bullet`](Assets/Script/Bullet/Bullet.cs), [`Obstacle`](Assets/Script/Obstacle/Obstacle.cs), [`ObstacleSO`](Assets/Script/Obstacle/ObstacleSO.cs) |
| Phá obstacle và thả reward | `Obstacle` theo dõi HP. Khi HP về `0`, object phát event despawn và nếu config có reward thì tạo pickup item tương ứng. Prototype hiện tại có obstacle rơi `Gatling Gun`. | [`Obstacle`](Assets/Script/Obstacle/Obstacle.cs), [`ObstacleSO`](Assets/Script/Obstacle/ObstacleSO.cs), [`RewardSO`](Assets/Script/Reward/RewardSO.cs), [`WeaponPickupItem`](Assets/Script/Weapon/WeaponPickupItem.cs), [`EventHub`](Assets/Script/EventHub/EventHub.cs) |
| Thu thập pickup và đổi vũ khí | `WeaponPickupItem` bay về teammate gần nhất trong tầm, sau đó phát logic equip vũ khí. `TeammateController` cập nhật weapon dùng chung cho đội, đồng thời thay visual trên từng teammate. | [`WeaponPickupItem`](Assets/Script/Weapon/WeaponPickupItem.cs), [`TeammateController`](Assets/Script/Teammate/TeammateController.cs), [`Teammate`](Assets/Script/Teammate/Teammate.cs), [`WeaponSO`](Assets/Script/Weapon/WeaponSO.cs) |
| Spawn card theo lane | Card không spawn ngẫu nhiên tự do mà đi qua `CardAddQuantitySpawnZone` và path controller để tạo cảm giác level có nhịp đều, dễ tuning khoảng cách xuất hiện. | [`CardAddQuantitySpawnZone`](Assets/Script/CartAddQuantity/CardAddQuantitySpawnZone.cs), [`SpawnZone`](Assets/Script/Spawn/SpawnZone.cs), [`CardAddQuantityOnPathController`](Assets/Script/CartAddQuantity/CardAddQuantityOnPathController.cs), [`PointBaker`](Assets/Script/Map/PointBaker.cs) |
| Spawn obstacle theo slot đường | Obstacle dùng các point đã bake sẵn để biết chỗ đứng trên road. Cách này phù hợp cho prototype vì level designer chỉ cần đặt point thay vì viết logic lane phức tạp. | [`ObstacleSpawner`](Assets/Script/Obstacle/ObstacleSpawner.cs), [`PointBaker`](Assets/Script/Map/PointBaker.cs) |
| Spawn enemy wave theo cụm | Enemy không chạy riêng lẻ ngay từ đầu mà được tạo bởi `EnemySpawner`, xếp lên grid, rồi được `EnemyGridGroup` gom thành các cụm. Điều này giúp wave trông đông mà vẫn dễ kiểm soát. | [`EnemySpawner`](Assets/Script/Enemy/EnemySpawner.cs), [`EnemyGridGroup`](Assets/Script/Enemy/EnemyGridGroup.cs), [`ColliderSurfaceGridZone`](Assets/SpawnKit/Runtime/Components/ColliderSurfaceGridZone.cs) |
| Di chuyển enemy trên path | `EnemyGridGroupPathLane` và `EnemyGridGroupOnPathController` kéo cả cụm enemy tiến dần từ đầu đường về phía Home. Khi tới cuối path, group có thể được tái sử dụng để giữ áp lực liên tục. | [`EnemyGridGroupPathLane`](Assets/Script/Enemy/EnemyGridGroupPathLane.cs), [`EnemyGridGroupOnPathController`](Assets/Script/Enemy/EnemyGridGroupOnPathController.cs), [`ObjectOnPathController`](Assets/Script/Controller/ObjectOnPathController.cs), [`EnemyGridPathItem`](Assets/Script/Enemy/EnemyGridPathItem.cs) |
| Né obstacle trong khi giữ formation | Enemy có logic dò obstacle và đổi slot khả dụng trong grid nếu bị chặn. Đây là một lớp hành vi quan trọng giúp lane sống động hơn thay vì chỉ chạy xuyên qua vật cản. | [`EnemySpawner`](Assets/Script/Enemy/EnemySpawner.cs), [`ColliderSurfaceGridAlgorithm`](Assets/SpawnKit/Runtime/Algorithms/ColliderSurfaceGridAlgorithm.cs) |
| Gây damage về Home | Khi enemy chạm vùng Home, `HomeController` và relay liên quan xác nhận va chạm. `EnemySpawner` gom damage theo batch rồi gửi `EnemyHomeDamageBatchEvent` về đội người chơi thay vì trừ từng hit rời rạc. | [`HomeController`](Assets/Script/Home/HomeController.cs), [`HomeControllerRelay`](Assets/Script/Home/HomeControllerRelay.cs), [`EnemyHomeTargetService`](Assets/Script/Enemy/EnemyHomeTargetService.cs), [`EnemyHomeTargetRelay`](Assets/Script/Enemy/EnemyHomeTargetRelay.cs), [`EnemySpawner`](Assets/Script/Enemy/EnemySpawner.cs), [`EventHub`](Assets/Script/EventHub/EventHub.cs) |
| Mất quân và kiểm tra game over | `TeammateController` xem mỗi teammate như một phần HP của cả đội. Khi nhận batch damage, controller trừ tổng HP, despawn số teammate tương ứng và nếu đội đã về `0` thì raise `gameOver`. | [`TeammateController`](Assets/Script/Teammate/TeammateController.cs), [`TeammateSpawner`](Assets/Script/Teammate/TeammateSpawner.cs), [`Core`](Assets/Script/Manager/Core.cs) |
| Feedback hình ảnh khi giao tranh | Hit vào enemy hoặc obstacle có shake tại chỗ. Một số event như damage và obstacle destruction cũng đẩy trauma sang camera để tăng cảm giác lực. | [`DamageShakeFeedback`](Assets/Script/Feedback/DamageShakeFeedback.cs), [`CameraEventShake`](Assets/Script/Feedback/CameraEventShake.cs) |
| Camera theo player | Camera giữ player ở vị trí quan sát ổn định bằng cách follow target có tag `Player`. Đây là giải pháp đủ gọn cho prototype lane shooter. | [`CameraFollowTaggedTarget`](Assets/Script/Controller/CameraFollowTaggedTarget.cs) |

## 11. Snapshot triển khai hiện tại trong source

Các thông số dưới đây là tình trạng đang có trong prototype, hữu ích để team đọc tài liệu và hiểu phạm vi thật đang chạy trong scene `Demo`.

- Khởi tạo đầu game: `1` teammate
- HP đội người chơi: `1 HP / teammate`
- Card cộng quân mặc định: `+1`
- Weapon mặc định:
  - fire interval: `0.2s`
  - damage đạn: `1`
- Gatling Gun:
  - fire interval: `0.1s`
  - damage đạn: `2`
- Enemy nhỏ:
  - HP: `3`
  - contact damage: `1`
- Enemy trâu:
  - HP: `1000`
  - contact damage: `15`
- Bullet:
  - move speed: `24`
  - life time khoảng `0.8s`
- Trạng thái hoàn thành hiện tại:
  - có Start
  - có Game Over
  - có Restart
  - chưa có Win state hoàn chỉnh
  - chưa có boss flow đúng nghĩa

## 12. Phạm vi prototype

### 10.1. In-scope

- Một lượt chơi ngắn có thể bắt đầu, chơi, thua và restart
- Điều khiển ngang đội hình
- Tăng quân bằng card
- Auto-fire
- Enemy wave chạy theo path
- Obstacle có HP
- Reward pickup đổi vũ khí
- Camera follow và hit feedback cơ bản

### 10.2. Out-of-scope ở thời điểm hiện tại

- Meta progression ngoài trận
- Nâng cấp dài hạn giữa nhiều lượt chơi
- Nhiều loại card với multiplier phức tạp
- Boss battle riêng
- Win condition hoàn chỉnh theo level end
- Economy, shop, live-ops, quest

## 13. Đề xuất chia task nếu tiếp tục phát triển

- Gameplay:
  - Mở rộng thêm loại card ngoài `+1`
  - Bổ sung rule thắng theo đích cuối level hoặc wave clear
  - Thiết kế thêm ít nhất 1 enemy archetype trung gian
- Tech:
  - Chuẩn hóa event name `collision` hiện đang viết thành `collition` trong event hub
  - Tách reward spawning khỏi `Instantiate` trực tiếp nếu muốn đồng bộ hoàn toàn với pool
  - Viết thêm debug HUD cho số quân, weapon hiện tại, HP tổng
- Content:
  - Tạo thêm obstacle/reward type
  - Tạo thêm nhiều lane/path setup để kiểm chứng độ bền của hệ thống

## 14. Kết luận

Prototype hiện tại đã tái hiện được phần cốt lõi quan trọng nhất của fantasy `Last War: Survival`: bắt đầu yếu, tăng quân số nhanh, gia tăng mật độ bắn, phá vật cản để lấy nâng cấp và chống lại áp lực wave tiến về Home.

Về mặt kỹ thuật, project có nền tảng khá tốt cho một prototype vì đã có:

- hạ tầng spawn/pool tương đối tách bạch
- formation/grid rõ ràng
- path-driven spawning đủ linh hoạt cho lane combat
- event flow giúp giảm coupling giữa các hệ thống

Điểm còn thiếu lớn nhất để bước sang mức vertical slice là:

- điều kiện thắng rõ ràng
- nhiều content variation hơn
- progression layer phong phú hơn ngoài `+1` và Gatling pickup
