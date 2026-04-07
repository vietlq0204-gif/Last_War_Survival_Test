using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class ListPoint
{
    [Serializable]
    public struct PointData
    {
        public GameObject point; // tham chiếu đến gameObject này
        public int index; // index của point này

        public PointData (GameObject point, int index)
        {
            this.point = point;
            this.index = index;
        }
    }

    public PointData[] pointData; // mỗi point

    /// <summary>
    /// Dữ liệu cache phục vụ nội suy theo chiều dài đường đi (length-based interpolation).
    /// </summary>
    [Serializable]
    public sealed class PathCache
    {
        /// <summary>Danh sách vị trí (POCO) đã trích xuất từ pointData theo thứ tự index tăng dần.</summary>
        public readonly List<Vector3> positions = new List<Vector3>(128);

        /// <summary>Độ dài tích lũy tại mỗi vị trí (cumLen[0]=0, tăng dần).</summary>
        public readonly List<float> cumulativeLengths = new List<float>(128);

        /// <summary>
        /// Map: segment i bắt đầu từ positions[segStartPosIndex[i]]
        /// </summary>
        public readonly List<int> segStartPosIndex = new List<int>(128);

        /// <summary>Tổng chiều dài đường đi.</summary>
        public float totalLength;

        /// <summary>Xóa toàn bộ cache.</summary>
        public void Clear()
        {
            positions.Clear();
            cumulativeLengths.Clear();
            segStartPosIndex.Clear();
            totalLength = 0f;
        }
    }

    #region Helper

    /// <summary>
    /// helper Tìm segment index i bằng binary search.
    /// </summary>
    /// <remarks>
    /// cumLen phải tăng dần, cumLen[0] = 0, cumLen[last] = totalLength.
    /// </remarks>
    /// <returns> 
    /// Index i của segment.
    /// </returns>
    /// <param name="cumLen">Danh sách độ dài tích lũy.</param>
    /// <param name="d">Khoảng cách cần evaluate.</param>
    private int FindSegmentIndex(List<float> cumLen, float d)
    {
        if (cumLen == null || cumLen.Count < 2) return 0;

        int lo = 0;
        int hi = cumLen.Count - 1;

        // Invariant: tìm vị trí insert của d, sau đó lấy -1.
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            float v = cumLen[mid];

            if (v < d) lo = mid + 1;
            else hi = mid - 1;
        }

        // lo là vị trí insert đầu tiên mà cumLen[lo] >= d
        int idx = lo - 1;
        return Mathf.Clamp(idx, 0, cumLen.Count - 2);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Lấy GameObject theo index
    /// </summary>
    public GameObject GetPoint(int index)
    {
        if (pointData == null) return null;

        for (int i = 0; i < pointData.Length; i++)
            if (pointData[i].index == index)
                return pointData[i].point;

        return null;
    }

    /// <summary>
    /// Build cache (positions + cumulativeLengths + totalLength) từ pointData để phục vụ:
    /// - nội suy vị trí theo khoảng cách dọc đường (distance/length)
    /// - truy vấn nhanh segment theo cumLen
    /// </summary>
    /// <remarks>
    /// - Nếu muốn nội suy mượt hơn: có thể tăng mật độ điểm bằng cách resample sau khi cache.
    /// </remarks>
    /// <returns>
    /// true nếu build thành công (>= 2 positions hợp lệ và totalLength > 0),
    /// false nếu không đủ dữ liệu.
    /// </returns>
    /// <param name="cache"> Đối tượng cache nhận dữ liệu (sẽ được Clear và fill lại)</param>
    /// <param name="includeInactive"> Nếu true: vẫn lấy Transform position dù object inactive</param>
    /// <param name="epsilon">Ngưỡng bỏ qua segment quá ngắn (mặc định 1e-5)</param>
    public bool BuildPathCache(PathCache cache, bool includeInactive = true, float epsilon = 1e-5f)
    {
        if (cache == null) return false;
        cache.Clear();

        if (pointData == null || pointData.Length < 2)
            return false;

        // 1) Filter + sort
        List<PointData> sorted = new List<PointData>(pointData.Length);
        for (int i = 0; i < pointData.Length; i++)
            if (pointData[i].point != null)
                sorted.Add(pointData[i]);

        if (sorted.Count < 2) return false;

        sorted.Sort((a, b) => a.index.CompareTo(b.index));

        // 2) Extract positions
        for (int i = 0; i < sorted.Count; i++)
        {
            GameObject go = sorted[i].point;
            if (!includeInactive && !go.activeSelf) continue;

            cache.positions.Add(go.transform.position);
        }

        if (cache.positions.Count < 2) return false;

        // 3) Build segments
        cache.cumulativeLengths.Add(0f); // segment 0 start
        cache.totalLength = 0f;

        for (int i = 0; i < cache.positions.Count - 1; i++)
        {
            float segLen = Vector3.Distance(
                cache.positions[i],
                cache.positions[i + 1]);

            if (segLen <= epsilon)
                continue; // SKIP segment

            cache.totalLength += segLen;
            cache.cumulativeLengths.Add(cache.totalLength);
            cache.segStartPosIndex.Add(i);
        }

        return cache.totalLength > epsilon
            && cache.segStartPosIndex.Count > 0;
    }

    /// <summary>
    /// Nội suy vị trí theo khoảng cách dọc đường đi (length-based interpolation).
    /// </summary>
    /// <remarks>
    /// - distance sẽ được clamp vào [0, totalLength]
    /// - Dùng binary search trên cumulativeLengths để tìm segment nhanh
    /// - Trả về false nếu cache không hợp lệ hoặc không đủ điểm
    /// </remarks>
    /// <returns>
    /// true nếu evaluate thành công, ngược lại false.
    /// </returns>
    /// <param name="cache">Cache đã build từ BuildPathCache().</param>
    /// <param name="distance">Khoảng cách dọc đường (0..totalLength).</param>
    /// <param name="position">Vị trí nội suy trả ra.</param>
    public bool EvaluateByDistance(PathCache cache, float distance, out Vector3 position)
    {
        position = default;

        if (cache == null) return false;
        if (cache.segStartPosIndex.Count == 0) return false;
        if (cache.totalLength <= Mathf.Epsilon) return false;

        float d = Mathf.Clamp(distance, 0f, cache.totalLength);

        if (d <= 0f)
        {
            position = cache.positions[0];
            return true;
        }

        if (d >= cache.totalLength)
        {
            position = cache.positions[cache.positions.Count - 1];
            return true;
        }

        int segIndex = FindSegmentIndex(cache.cumulativeLengths, d);

        // map segment -> positions
        int a = cache.segStartPosIndex[
            Mathf.Clamp(segIndex, 0, cache.segStartPosIndex.Count - 1)
        ];
        int b = a + 1;

        float lenA = cache.cumulativeLengths[segIndex];
        float lenB = cache.cumulativeLengths[segIndex + 1];
        float segLen = lenB - lenA;

        if (segLen <= Mathf.Epsilon)
        {
            position = cache.positions[a];
            return true;
        }

        float t = (d - lenA) / segLen;
        position = Vector3.LerpUnclamped(
            cache.positions[a],
            cache.positions[b],
            t);

        return true;
    }

    /// <summary>
    /// Lấy index “middle point” theo danh sách pointData (sau khi sort theo index).
    /// </summary>
    /// <remarks>
    /// Đây là middle theo số lượng point (discrete), KHÔNG phải middle theo chiều dài đường.
    /// </remarks>
    /// <returns>
    /// Middle index (theo index bake).
    /// </returns>
    public int GetMiddlePointIndex()
    {
        if (pointData == null || pointData.Length == 0) return 0;

        // Lọc point hợp lệ và sort theo index tăng dần giống BuildPathCache.
        List<PointData> sorted = new List<PointData>(pointData.Length);
        for (int i = 0; i < pointData.Length; i++)
            if (pointData[i].point != null)
                sorted.Add(pointData[i]);

        if (sorted.Count == 0) return 0;

        sorted.Sort((a, b) => a.index.CompareTo(b.index));

        int mid = sorted.Count / 2; // lấy middle bên phải nếu chẵn
        return sorted[mid].index;
    }

    /// <summary>
    /// Lấy GameObject middle + index (theo danh sách pointData đã sort).
    /// </summary>
    /// <returns>
    /// true nếu có middle hợp lệ.
    /// </returns>
    /// <param name="middlePoint"></param>
    /// <param name="middleIndex"></param>
    /// <returns></returns>
    public bool TryGetMiddlePoint(out GameObject middlePoint, out int middleIndex)
    {
        middlePoint = null;
        middleIndex = 0;

        if (pointData == null || pointData.Length == 0) return false;

        List<PointData> sorted = new List<PointData>(pointData.Length);
        for (int i = 0; i < pointData.Length; i++)
            if (pointData[i].point != null)
                sorted.Add(pointData[i]);

        if (sorted.Count == 0) return false;

        sorted.Sort((a, b) => a.index.CompareTo(b.index));

        int mid = sorted.Count / 2;
        middlePoint = sorted[mid].point;
        middleIndex = sorted[mid].index;
        return true;
    }

    /// <summary>
    /// Lấy tọa độ tại “giữa đường” theo chiều dài (50% totalLength).
    /// </summary>
    /// <remarks>
    /// Cần cache đã build từ BuildPathCache(). Đây là middle chuẩn theo length.
    /// </remarks>
    /// <returns>
    /// true nếu evaluate thành công.
    /// </returns>    
    /// /// <param name="cache"></param>
    /// <param name="position"></param>
    /// <returns></returns>
    public bool TryGetMiddleCoordinate(PathCache cache, out Vector3 position)
    {
        position = default;
        if (cache == null) return false;
        if (cache.totalLength <= Mathf.Epsilon) return false;

        float d = cache.totalLength * 0.5f;
        return EvaluateByDistance(cache, d, out position);
    }

    /// <summary>
    /// Lấy GameObject đầu tiên (theo index nhỏ nhất) trong pointData.
    /// </summary>
    /// <param name="firstPoint"></param>
    /// <returns>
    /// true nếu có point hợp lệ, false nếu không có point nào hợp lệ.
    /// </returns>
    public bool TryGetFirstPoint(out GameObject firstPoint)
    {
        firstPoint = null;
        if (pointData == null || pointData.Length == 0) return false;

        int bestIndex = int.MaxValue;
        for (int i = 0; i < pointData.Length; i++)
        {
            var pd = pointData[i];
            if (pd.point == null) continue;
            if (pd.index <= bestIndex) // lấy index nhỏ nhất
            {
                bestIndex = pd.index;
                firstPoint = pd.point;
            }
        }

        return firstPoint != null;
    }

    /// <summary>
    /// Lấy GameObject cuối cùng (theo index lớn nhất) trong pointData.
    /// </summary>
    /// <param name="lastPoint"></param>
    /// <returns>
    /// true nếu có point hợp lệ, false nếu không có point nào hợp lệ.
    /// </returns>
    public bool TryGetLastPoint(out GameObject lastPoint)
    {
        lastPoint = null;
        if (pointData == null || pointData.Length == 0) return false;

        int bestIndex = int.MinValue;
        for (int i = 0; i < pointData.Length; i++)
        {
            var pd = pointData[i];
            if (pd.point == null) continue;

            if (pd.index >= bestIndex) // lấy index lớn nhất
            {
                bestIndex = pd.index;
                lastPoint = pd.point;
            }
        }

        return lastPoint != null;
    }

    /// <summary>
    /// Tính khoảng cách dọc path từ point đầu tiên tới một point cụ thể trong danh sách bake.
    /// </summary>
    /// <param name="targetPoint">Point cần tìm khoảng cách.</param>
    /// <param name="distance">Khoảng cách tích lũy từ first point tới target point.</param>
    /// <param name="includeInactive">Nếu false thì bỏ qua point inactive.</param>
    /// <param name="epsilon">Ngưỡng bỏ qua segment quá ngắn.</param>
    /// <returns>true nếu targetPoint thuộc path đã bake.</returns>
    public bool TryGetDistanceToPoint(
        GameObject targetPoint,
        out float distance,
        bool includeInactive = true,
        float epsilon = 1e-5f)
    {
        distance = 0f;

        if (targetPoint == null) return false;
        if (pointData == null || pointData.Length == 0) return false;

        List<PointData> sorted = new List<PointData>(pointData.Length);
        for (int i = 0; i < pointData.Length; i++)
        {
            var point = pointData[i].point;
            if (point == null) continue;
            if (!includeInactive && !point.activeSelf) continue;

            sorted.Add(pointData[i]);
        }

        if (sorted.Count == 0) return false;

        sorted.Sort((a, b) => a.index.CompareTo(b.index));

        if (sorted[0].point == targetPoint)
        {
            distance = 0f;
            return true;
        }

        for (int i = 1; i < sorted.Count; i++)
        {
            var previousPoint = sorted[i - 1].point;
            var currentPoint = sorted[i].point;
            if (previousPoint == null || currentPoint == null)
                continue;

            float segmentLength = Vector3.Distance(
                previousPoint.transform.position,
                currentPoint.transform.position);

            if (segmentLength > epsilon)
                distance += segmentLength;

            if (currentPoint == targetPoint)
                return true;
        }

        distance = 0f;
        return false;
    }

    #endregion
}

public class PointBaker : MonoBehaviour
{
    public enum PointSampleLayoutMode
    {
        Custom = 0,
        EvenlySpacedOnPath = 1,
    }

    private List<GameObject> points = new();
    private List<int> pointIndex = new();

    public ListPoint listPoint = new ListPoint(); // puclic để các hệ thống khác lấy data

    [Header("Sample Layout")]
    [SerializeField] private PointSampleLayoutMode sampleLayoutMode = PointSampleLayoutMode.Custom;

#if UNITY_EDITOR

    [Header("---------------------Gizmos Show----------------------------------------------")]
    [SerializeField] bool showGizmos = true;          // Bật/tắt toàn bộ Gizmos
    //[SerializeField] bool showLines = true;           // Bật/tắt đường nối
    //[SerializeField] bool showSpheres = true;         // Bật/tắt điểm
    [SerializeField] Color gizmoLineColor = Color.cyan;
    //[SerializeField] Color gizmoPointColor = Color.yellow;
    //[Range(0.05f, 100f), SerializeField]
    //float pointSize = 0.1f; // kích thước điểm
    [SerializeField] Color gizmoPointColor = new Color(1f, 0.85f, 0.2f, 0.9f);
    [SerializeField, Range(0.025f, 2f)] float pointSlotHandleScale = 0.125f;
    [SerializeField] bool showPointLabels = true;

    void OnDrawGizmos()
    {
        if (!showGizmos)
            return;

        List<ListPoint.PointData> previewPoints = BuildPreviewPointData();
        if (previewPoints.Count == 0)
            return;

        Gizmos.color = gizmoLineColor;
        for (int i = 0; i < previewPoints.Count - 1; i++)
        {
            var a = previewPoints[i].point;
            var b = previewPoints[i + 1].point;
            if (a && b)
                Gizmos.DrawLine(a.transform.position, b.transform.position);
        }

        for (int i = 0; i < previewPoints.Count; i++)
        {
            GameObject point = previewPoints[i].point;
            if (point == null)
                continue;

            Vector3 position = point.transform.position;
            float handleSize = HandleUtility.GetHandleSize(position);
            float slotSize = Mathf.Max(0.05f, handleSize * pointSlotHandleScale);
            Vector3 slotExtents = Vector3.one * slotSize;

            Gizmos.color = gizmoPointColor;
            Gizmos.DrawCube(position, slotExtents * 0.35f);
            Gizmos.DrawWireCube(position, slotExtents);

            if (!showPointLabels)
                continue;

            Handles.color = gizmoPointColor;
            Handles.Label(position + Vector3.up * (slotSize * 0.8f), $"[{previewPoints[i].index}]");
        }
    }

    private List<ListPoint.PointData> BuildPreviewPointData()
    {
        var previewPoints = new List<ListPoint.PointData>(listPoint?.pointData?.Length ?? 0);

        if (listPoint?.pointData != null)
        {
            for (int i = 0; i < listPoint.pointData.Length; i++)
            {
                var pointData = listPoint.pointData[i];
                if (pointData.point == null)
                    continue;

                previewPoints.Add(pointData);
            }
        }

        if (previewPoints.Count == 0)
        {
            var taggedPoints = GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && t.CompareTag("RoadPoint"))
                .Select((t, index) => new ListPoint.PointData(t.gameObject, index));

            previewPoints.AddRange(taggedPoints);
        }

        previewPoints.Sort((left, right) => left.index.CompareTo(right.index));
        return previewPoints;
    }


    [CustomEditor(typeof(PointBaker))]
    public class PointBakerEditor : Editor
    {
        PointBaker baker;

        private void OnEnable()
        {
            baker = (PointBaker)target;
        }

        private void Reset()
        {
            baker = (PointBaker)target;

            if (Application.isPlaying) return;

            BakePoints(baker);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            if (GUILayout.Button("Apply Sample Layout"))
            {
                ApplySampleLayout(baker);
            }

            if (GUILayout.Button("Bake Road Points"))
            {
                BakePoints(baker);
            }
        }

        /// <summary>
        /// Bake toàn bộ RoadPoint con (theo tag "RoadPoint") vào PointBaker:
        /// - Thu thập danh sách RoadPoint theo thứ tự duyệt hierarchy
        /// - Đánh dấu các index quan trọng (first/last/middle/...)
        /// - Tự động add BoxCollider trigger + đổi tên GameObject theo vai trò
        /// - Ghi dữ liệu vào:
        ///   + baker.points (List<GameObject>)
        ///   + baker.pointIndex (List<int>)  : danh sách index quan trọng
        ///   + baker.listPoint.pointData     : nguồn dữ liệu duy nhất (mỗi point kèm index)
        ///   + baker.listPoint.pointCount    : số lượng point
        /// </summary>
        /// <param name="baker">Component PointBaker cần được bake dữ liệu.</param>
        private void BakePoints(PointBaker baker)
        {
            if (baker == null) return;

            Undo.RecordObject(baker, "Bake Road Points");

            // 1) Collect road points (tag RoadPoint)
            List<GameObject> roadPoints = baker
                .GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && t.CompareTag("RoadPoint"))
                .Select(t => t.gameObject)
                .ToList();

            int count = roadPoints.Count;
            if (count < 1)
            {
                Debug.LogWarning("Không đủ RoadPoint để bake.");
                return;
            }

            ApplySampleLayout(baker, roadPoints);

            // 2) Set baker.points
            baker.points = roadPoints;

            // 3) Compute important indices (giữ logic cũ)
            var pairs = new List<KeyValuePair<int, string>>();
            int firstIndex = 0;
            int lastIndex = count - 1;

            pairs.Add(new(firstIndex, "Point First"));
            pairs.Add(new(lastIndex, "Point Last"));

            if (count == 3)
            {
                pairs.Add(new(1, "Point Middle"));
            }
            else if (count == 4)
            {
                pairs.Add(new(1, "Point OnSession"));
                pairs.Add(new(2, "Point Prepare End"));
            }
            else // count >= 5
            {
                pairs.Add(new(1, "Point Second"));
                pairs.Add(new(count / 2, "Point Middle"));
                pairs.Add(new(count - 2, "Point Penultimate"));
            }

            // loại trùng + sort
            var importantPoints = pairs
                .GroupBy(p => p.Key)
                .Select(g => g.First())
                .Where(p => p.Key >= 0 && p.Key < count)
                .OrderBy(p => p.Key)
                .ToList();

            // 4) Rename/collider
            var importantSet = importantPoints.Select(p => p.Key).ToHashSet();

            foreach (var kv in importantPoints)
                AddColliderAndRename(roadPoints[kv.Key], kv.Value);

            for (int i = 0; i < count; i++)
            {
                if (importantSet.Contains(i)) continue;
                AddColliderAndRename(roadPoints[i], $"Point {i}");
            }

            // 5) Save important indices in baker.pointIndex
            baker.pointIndex = importantPoints.Select(p => p.Key).ToList();

            // 6) Build ListPoint WITHOUT allPoints/allIndex
            if (baker.listPoint == null)
                baker.listPoint = new ListPoint();

            //baker.listPoint.pointCount = count;

            // pointData là nguồn dữ liệu duy nhất: mỗi phần tử ứng với 1 RoadPoint và index của nó.
            baker.listPoint.pointData = new ListPoint.PointData[count];

            for (int i = 0; i < count; i++)
            {
                baker.listPoint.pointData[i] = new ListPoint.PointData
                {
                    point = roadPoints[i],
                    index = i
                };
            }

            EditorUtility.SetDirty(baker);
            PrefabUtility.RecordPrefabInstancePropertyModifications(baker);

            Debug.Log($"Bake xong: {count} RoadPoint. ImportantIndex=[{string.Join(", ", baker.pointIndex)}]");
        }

        private void ApplySampleLayout(PointBaker baker, List<GameObject> roadPoints = null)
        {
            if (baker == null || baker.sampleLayoutMode == PointSampleLayoutMode.Custom)
                return;

            roadPoints ??= baker
                .GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && t.CompareTag("RoadPoint"))
                .Select(t => t.gameObject)
                .ToList();

            if (roadPoints.Count < 2)
            {
                Debug.LogWarning("Cần ít nhất 2 RoadPoint để áp dụng sample layout.");
                return;
            }

            switch (baker.sampleLayoutMode)
            {
                case PointSampleLayoutMode.EvenlySpacedOnPath:
                    ApplyEvenlySpacedOnPathLayout(roadPoints);
                    break;
            }
        }

        private void ApplyEvenlySpacedOnPathLayout(List<GameObject> roadPoints)
        {
            if (roadPoints == null || roadPoints.Count < 2)
                return;

            var tempListPoint = new ListPoint
            {
                pointData = new ListPoint.PointData[roadPoints.Count]
            };

            for (int i = 0; i < roadPoints.Count; i++)
            {
                tempListPoint.pointData[i] = new ListPoint.PointData(roadPoints[i], i);
            }

            var pathCache = new ListPoint.PathCache();
            if (!tempListPoint.BuildPathCache(pathCache))
            {
                Debug.LogWarning("Không thể rải đều RoadPoint vì path hiện tại không hợp lệ.");
                return;
            }

            Transform[] pointTransforms = roadPoints
                .Where(point => point != null)
                .Select(point => point.transform)
                .ToArray();

            Undo.RecordObjects(pointTransforms, "Apply Evenly Spaced Road Points");

            int pointCount = roadPoints.Count;
            for (int i = 0; i < pointCount; i++)
            {
                if (roadPoints[i] == null)
                    continue;

                float t = pointCount <= 1 ? 0f : i / (float)(pointCount - 1);
                float distance = pathCache.totalLength * t;

                if (!tempListPoint.EvaluateByDistance(pathCache, distance, out Vector3 position))
                    continue;

                roadPoints[i].transform.position = position;
                EditorUtility.SetDirty(roadPoints[i].transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(roadPoints[i].transform);
            }
        }

        private void AddColliderAndRename(GameObject point, string newName)
        {
            if (point == null) return;

            //var collider = point.GetComponent<BoxCollider>() ?? point.AddComponent<BoxCollider>();

            BoxCollider collider = point.GetComponent<BoxCollider>();

            if (collider == null)
            {
                collider = point.AddComponent<BoxCollider>();
            }

            collider.isTrigger = true;
            collider.size = Vector3.one * 1f;

            point.name = newName;
            EditorUtility.SetDirty(point);
            PrefabUtility.RecordPrefabInstancePropertyModifications(point);
        }
    }
#endif
}

