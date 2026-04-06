using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;

namespace Vit.SpawnKit.Components
{
    /// <summary>
    /// authoring cho grid dựa trên Collider.
    /// Class này vừa giữ config, vừa vẽ preview, vừa cache algorithm runtime nên vẫn còn gom nhiều vai trò.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("SpawnKit/Collider Surface Grid Zone")]
    public sealed class ColliderSurfaceGridZone : MonoBehaviour
    {
        /// <summary>
        /// Collider được dùng làm phạm vi tính toán grid.
        /// Nếu để trống, component sẽ tự tìm collider cục bộ hoặc collider child đầu tiên; cách này tiện nhưng chưa rõ ràng khi có nhiều collider.
        /// </summary>
        [Header("Zone")]
        [Tooltip("Collider được dùng làm phạm vi tính toán grid. Nếu để trống, component sẽ thử tìm Collider trên chính object hoặc child.")]
        [SerializeField]
        private Collider zoneCollider;

        /// <summary>
        /// Khoảng cách cơ sở giữa các slot grid.
        /// Giá trị này đang canh chỉnh thủ công, chưa tự động suy ra từ kích thước prefab sẽ spawn.
        /// </summary>
        [Tooltip(
            "Khoảng cách giữa các slot grid. Nên đặt theo bề ngang thực tế của object cần spawn để tránh chồng lên nhau.")]
        [SerializeField, Min(0.1f)]
        private float cellSize = 0.5f;

        /// <summary>
        /// Khoảng trống mép collider không được dùng để đặt slot.
        /// Hiện tại chỉ là offset cố định, chưa tính theo hình dạng chi tiết của mesh hoặc collider.
        /// </summary>
        [Tooltip("Khoảng cách bỏ qua sát mép collider. Tăng giá trị này nếu object spawn dễ bị lộ ra khỏi vùng zone.")]
        [SerializeField, Min(0f)]
        private float edgePadding = 0.05f;

        /// <summary>
        /// Mặt phẳng tham chiếu để đặt grid trên collider.
        /// Lựa chọn này phù hợp cho collider đơn giản; với hình dạng phức tạp, anchor vẫn dựa trên bounds hoặc closest point.
        /// </summary>
        [Tooltip("Mặt phẳng tham chiếu để đặt grid: Top, Bottom hoặc Center của collider zone.")] [SerializeField]
        private ColliderGridPlaneAnchor anchor = ColliderGridPlaneAnchor.Center;

        /// <summary>
        /// Độ dịch theo trục up của grid để đặt object cao hơn hoặc thấp hơn mặt phẳng grid.
        /// Hiện tại offset này áp dụng đồng loạt cho mọi object, chưa có offset riêng theo variant.
        /// </summary>
        [Tooltip("Độ dịch theo trục up của grid. Dùng để căn chỉnh pivot object spawn so với mặt grid.")]
        [SerializeField]
        private float verticalOffset;

        /// <summary>
        /// Xác định có giữ slot trung tâm hay bỏ qua.
        /// Nếu tắt, ô giữa sẽ luôn trống; hiện tại chỉ loại bỏ đúng slot (0,0), không tạo khoảng trống rộng hơn.
        /// </summary>
        [Tooltip("Nếu bật, grid có thể dùng slot trung tâm. Nếu tắt, slot (0,0) ở giữa zone sẽ bị bỏ qua.")]
        [SerializeField]
        private bool includeCenterSlot;

        /// <summary>
        /// Xác định grid có xoay theo trục của collider hay dùng trục world.
        /// Với collider xoay nghiêng phức tạp, cách này vẫn dựa trên projection từ bounds nên chưa chính xác tuyệt đối.
        /// </summary>
        [Tooltip("Nếu bật, grid sử dụng trục right, up, forward của collider. Nếu tắt, grid sẽ căn theo trục world.")]
        [SerializeField]
        private bool useColliderAxes = true;

        /// <summary>
        /// Xác định object spawn có xoay theo grid zone hay giữ Quaternion.identity.
        /// Hiện tại chưa có chế độ custom rotation theo từng slot hay từng prefab.
        /// </summary>
        [Tooltip(
            "Nếu bật, slot grid trả về rotation theo zone. Nếu tắt, preview hoặc spawn sẽ dùng rotation mặc định.")]
        [SerializeField]
        private bool alignRotationToZone = true;

        [Header("Cell Randomization")]
        [Tooltip("If enabled, each cell gets a deterministic random offset so the layout looks less uniform.")]
        [SerializeField]
        private bool randomizeCellPositions;

        [Tooltip("Max planar offset per cell, expressed as a fraction of cellSize.")]
        [SerializeField, Range(0f, 0.45f)]
        private float randomCellOffsetStrength = 0.2f;

        [Tooltip("Seed used to generate deterministic cell offsets.")]
        [SerializeField]
        private int randomCellOffsetSeed = 12345;

        /// <summary>
        /// Bật hoặc tắt vẽ grid preview trong Scene view.
        /// Preview này dùng chung algorithm runtime nên phản ánh đúng occupied slot, nhưng chưa tối ưu cho Scene view repaint quá nhiều.
        /// </summary>
        [Header("Preview")] [Tooltip("Bật hoặc tắt vẽ grid preview trong Scene view.")] [SerializeField]
        private bool drawGridPreview = true;

        /// <summary>
        /// Chỉ vẽ preview khi object đang được chọn trong Scene hoặc Hierarchy.
        /// Cách này giúp giảm rối mắt nhưng không có preview tổng quan khi chưa chọn zone.
        /// </summary>
        [Tooltip("Nếu bật, grid chỉ hiện khi zone đang được chọn.")] [SerializeField]
        private bool drawPreviewOnlyWhenSelected;

        /// <summary>
        /// Xác định có vẽ khối fill cho mỗi cell hay chỉ vẽ wireframe.
        /// Vẽ fill nhìn rõ occupied slot hơn nhưng tăng mật độ thông tin khi zone có quá nhiều ô.
        /// </summary>
        [Tooltip("Nếu bật, mỗi ô grid sẽ được vẽ dạng khối đặc. Nếu tắt, chỉ vẽ wireframe.")] [SerializeField]
        private bool drawFilledCells = true;

        /// <summary>
        /// Tỉ lệ kích thước khối preview so với kích thước cell.
        /// Đây là tỉ lệ giao diện, không ảnh hưởng đến logic spawn thực tế.
        /// </summary>
        [Tooltip("Tỉ lệ kích thước khối preview so với kích thước cell.")] [SerializeField, Range(0.1f, 1f)]
        private float previewCellFill = 0.2f;

        /// <summary>
        /// Độ dày tối thiểu của khối preview.
        /// Trường này chỉ ảnh hưởng cách vẽ gizmo, chưa đồng bộ với kích thước thật của object spawn.
        /// </summary>
        [Tooltip("Độ dày tối thiểu của khối preview trong Scene view.")] [SerializeField, Min(0.001f)]
        private float previewThickness = 0.02f;

        /// <summary>
        /// Bật hoặc tắt vẽ bounds bao ngoài của collider zone.
        /// Hiện tại đang vẽ bằng collider.bounds nên chỉ là AABB world, không phản ánh được local shape xoay phức tạp.
        /// </summary>
        [Tooltip("Nếu bật, Scene view sẽ vẽ thêm collider bounds để dễ căn chỉnh zone.")] [SerializeField]
        private bool drawColliderBounds = true;

        /// <summary>
        /// Màu dùng cho slot còn trống.
        /// Chưa có phân tách màu theo loại slot hay theo khoảng cách đến tâm.
        /// </summary>
        [Tooltip("Màu preview cho slot đang còn trống.")] [SerializeField]
        private Color availableCellColor = Color.green;

        /// <summary>
        /// Màu dùng cho slot đã bị chiếm bởi object đang sống.
        /// Màu này phụ thuộc vào reservation runtime, không ghi nhớ lịch sử spawn đã despawn.
        /// </summary>
        [Tooltip("Màu preview cho slot đã bị object đang hoạt động chiếm.")] [SerializeField]
        private Color occupiedCellColor = new Color(1f, 0.5f, 0f, 1f);

        /// <summary>
        /// Màu wireframe của từng cell.
        /// Đây là một màu chung cho toàn bộ grid, chưa có nhận biết trung tâm hay vòng ưu tiên.
        /// </summary>
        [Tooltip("Màu wireframe của từng cell grid.")] [SerializeField]
        private Color gridOutlineColor = Color.white;

        /// <summary>
        /// Màu bounds collider preview.
        /// Chỉ dùng cho gizmo editor, không ảnh hưởng logic runtime.
        /// </summary>
        [Tooltip("Màu bounds của collider zone trong Scene view.")] [SerializeField]
        private Color colliderBoundsColor = Color.blue;

        /// <summary>
        /// Buffer tạm để tránh tạo list mới mỗi lần vẽ preview.
        /// Danh sách này chỉ giảm cấp phát bộ nhớ ở mức cơ bản, chưa loại bỏ hoàn toàn chi phí rebuild preview.
        /// </summary>
        private readonly List<ColliderSurfaceGridCellPreview> _previewCells =
            new List<ColliderSurfaceGridCellPreview>(256);

        /// <summary>
        /// Algorithm runtime được cache để giữ occupied slot giữa nhiều lần spawn.
        /// Nếu bất kỳ config nào đổi, algorithm sẽ được tạo lại và mất state cũ.
        /// </summary>
        private ColliderSurfaceGridAlgorithm _algorithm;

        /// <summary>
        /// Collider đã được resolve cho zone hiện tại.
        /// Property này có lazy lookup nên gọi nhiều lần liên tiếp vẫn có thể kích hoạt cache logic.
        /// </summary>
        public Collider ZoneCollider => ResolveCollider();

        /// <summary>
        /// Số slot đang bị chiếm trong algorithm hiện tại.
        /// Giá trị này phụ thuộc vào algorithm cache; nếu config đổi và algorithm tạo lại, số liệu sẽ reset.
        /// </summary>
        public int OccupiedSlotCount => ResolveAlgorithm() != null ? _algorithm.OccupiedSlotCount : 0;

        public bool RandomizeCellPositions
        {
            get => randomizeCellPositions;
            set
            {
                if (randomizeCellPositions == value)
                    return;

                randomizeCellPositions = value;
                _algorithm = null;
            }
        }

        /// <summary>
        /// Trả về algorithm grid đang dùng cho zone, tái sử dụng nếu config không đổi.
        /// Hàm này ưu tiên giữ state occupied slot, nhưng khi bất kỳ tham số nào thay đổi thì phải rebuild toàn bộ algorithm.
        /// </summary>
        public ColliderSurfaceGridAlgorithm ResolveAlgorithm()
        {
            var collider = ResolveCollider();
            if (collider == null)
            {
                _algorithm = null;
                return null;
            }

            // Tái sử dụng cùng một instance algorithm để giữ trạng thái occupied slot giữa nhiều lần spawn.
            if (_algorithm != null && _algorithm.Matches(
                    collider,
                    cellSize,
                    edgePadding,
                    verticalOffset,
                    anchor,
                    includeCenterSlot,
                    useColliderAxes,
                    alignRotationToZone,
                    randomizeCellPositions,
                    randomCellOffsetStrength,
                    randomCellOffsetSeed))
                return _algorithm;

            _algorithm = new ColliderSurfaceGridAlgorithm(
                collider,
                cellSize,
                edgePadding,
                verticalOffset,
                anchor,
                includeCenterSlot,
                useColliderAxes,
                alignRotationToZone,
                randomizeCellPositions,
                randomCellOffsetStrength,
                randomCellOffsetSeed);
            return _algorithm;
        }

        /// <summary>
        /// Lấy số slot còn trống có thể spawn trong zone.
        /// Hàm này sẽ đi qua algorithm và refresh candidate cells, chưa được cache riêng cho editor repaint.
        /// </summary>
        public int GetAvailableSlotCount()
        {
            var algorithm = ResolveAlgorithm();
            return algorithm != null ? algorithm.GetAvailableSlotCount() : 0;
        }

        /// <summary>
        /// Tự động gán collider tham chiếu khi mới thêm component.
        /// Chỉ là convenience setup, không đảm bảo chọn đúng collider nếu object có nhiều collider phụ.
        /// </summary>
        private void Reset()
        {
            CacheColliderReference();
        }

        /// <summary>
        /// Đảm bảo zone có collider ngay khi component được bật.
        /// Không có xử lý nâng cao cho trường hợp collider được tạo trễ ở frame sau.
        /// </summary>
        private void OnEnable()
        {
            CacheColliderReference();
        }

        /// <summary>
        /// Clamp các giá trị editor để tránh dữ liệu sai cơ bản.
        /// Hiện tại chỉ clamp min-max đơn giản, chưa có validate quan hệ giữa cell size và bounds zone.
        /// </summary>
        private void OnValidate()
        {
            cellSize = Mathf.Max(0.01f, cellSize);
            edgePadding = Mathf.Max(0f, edgePadding);
            randomCellOffsetStrength = Mathf.Clamp(randomCellOffsetStrength, 0f, 0.45f);
            previewCellFill = Mathf.Clamp(previewCellFill, 0.1f, 1f);
            previewThickness = Mathf.Max(0.001f, previewThickness);
            CacheColliderReference();
        }

        /// <summary>
        /// Vẽ preview trong Scene view khi tùy chọn vẽ luôn đang bật.
        /// Đường vẽ này có thể được gọi nhiều lần bởi editor repaint, nên chưa tối ưu cho zone rất lớn.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!drawGridPreview || drawPreviewOnlyWhenSelected) return;
            DrawPreviewGizmos();
        }

        /// <summary>
        /// Vẽ preview khi zone đang được chọn.
        /// Đây là đường vẽ ưu tiên cho lúc cần căn chỉnh zone trong editor.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!drawGridPreview) return;
            DrawPreviewGizmos();
        }

        /// <summary>
        /// Vẽ grid preview dựa trên đúng algorithm runtime.
        /// Cách này giúp preview trùng với occupied slot thật, nhưng mỗi lần repaint đều rebuild danh sách cell preview.
        /// </summary>
        private void DrawPreviewGizmos()
        {
            var algorithm = ResolveAlgorithm();
            var collider = ResolveCollider();
            if (algorithm == null || collider == null) return;

            // Preview dùng cùng algorithm runtime nên Scene view phản ánh đúng slot đang bị chiếm.
            _previewCells.Clear();
            algorithm.GetPreviewCells(_previewCells);
            if (_previewCells.Count == 0 && !drawColliderBounds) return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            if (drawColliderBounds)
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = colliderBoundsColor;
                Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
            }

            Vector3 previewSize = new Vector3(
                cellSize * previewCellFill,
                Mathf.Max(previewThickness, cellSize * 0.02f),
                cellSize * previewCellFill);

            for (int i = 0; i < _previewCells.Count; i++)
            {
                var cell = _previewCells[i];
                Gizmos.matrix = Matrix4x4.TRS(cell.Position, cell.Rotation, Vector3.one);

                if (drawFilledCells)
                {
                    // Màu fill cho phép nhìn nhanh slot nào đang còn trống và slot nào đã bị chiếm.
                    Gizmos.color = cell.IsOccupied ? occupiedCellColor : availableCellColor;
                    Gizmos.DrawCube(Vector3.zero, previewSize);
                }

                Gizmos.color = gridOutlineColor;
                Gizmos.DrawWireCube(Vector3.zero, previewSize);
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        /// <summary>
        /// Resolve collider zone theo lazy lookup.
        /// Nếu có nhiều child collider, hàm này sẽ dùng collider đầu tiên tìm thấy và chưa có rule ưu tiên rõ ràng.
        /// </summary>
        private Collider ResolveCollider()
        {
            if (zoneCollider != null) return zoneCollider;
            CacheColliderReference();
            return zoneCollider;
        }

        /// <summary>
        /// Tự động cache collider tham chiếu cho zone.
        /// Thứ tự ưu tiên hiện tại là collider trên chính object rồi đến child đầu tiên; chưa có UI chọn collider thông minh.
        /// </summary>
        private void CacheColliderReference()
        {
            if (zoneCollider != null) return;

            if (TryGetComponent(out Collider localCollider))
            {
                zoneCollider = localCollider;
                return;
            }

            zoneCollider = GetComponentInChildren<Collider>();
        }
    }
}
