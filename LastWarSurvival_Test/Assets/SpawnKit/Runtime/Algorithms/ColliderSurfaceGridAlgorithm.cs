using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Pooling;

namespace Vit.SpawnKit.Algorithms
{
/// <summary>
/// Selects grid cells inside a collider, prioritizing cells nearest the collider center.
/// Occupied cells persist across spawn requests until the spawned instance returns to pool.
/// </summary>
public sealed class ColliderSurfaceGridAlgorithm : ITrySpawnAlgorithm, ISpawnBatchReset, ISpawnResultCallback, IGridSlotReservationOwner
{
    private const float ClosestPointToleranceSqr = 0.0001f;

    private readonly Collider _collider;
    private readonly float _cellSize;
    private readonly float _edgePadding;
    private readonly float _verticalOffset;
    private readonly ColliderGridPlaneAnchor _anchor;
    private readonly bool _useColliderAxes;
    private readonly bool _alignRotationToZone;

    private readonly List<GridCell> _candidateCells = new List<GridCell>(128);
    private readonly Dictionary<int, ReservedPlacement> _requestPlacements = new Dictionary<int, ReservedPlacement>(32);
    private readonly Dictionary<long, SpawnGridSlotReservation> _slotReservations = new Dictionary<long, SpawnGridSlotReservation>(128);
    private readonly HashSet<long> _occupiedSlots = new HashSet<long>();
    private readonly List<long> _releasedKeysBuffer = new List<long>(16);

    public ColliderSurfaceGridAlgorithm(
        Collider collider,
        float cellSize = 0.75f,
        float edgePadding = 0f,
        float verticalOffset = 0f,
        ColliderGridPlaneAnchor anchor = ColliderGridPlaneAnchor.Bottom,
        bool useColliderAxes = true,
        bool alignRotationToZone = true)
    {
        _collider = collider;
        _cellSize = Mathf.Max(0.01f, cellSize);
        _edgePadding = Mathf.Max(0f, edgePadding);
        _verticalOffset = verticalOffset;
        _anchor = anchor;
        _useColliderAxes = useColliderAxes;
        _alignRotationToZone = alignRotationToZone;
    }

    public int OccupiedSlotCount
    {
        get
        {
            PruneReleasedReservations();
            return _occupiedSlots.Count;
        }
    }

    public bool Matches(
        Collider collider,
        float cellSize,
        float edgePadding,
        float verticalOffset,
        ColliderGridPlaneAnchor anchor,
        bool useColliderAxes,
        bool alignRotationToZone)
    {
        return _collider == collider
               && Mathf.Approximately(_cellSize, Mathf.Max(0.01f, cellSize))
               && Mathf.Approximately(_edgePadding, Mathf.Max(0f, edgePadding))
               && Mathf.Approximately(_verticalOffset, verticalOffset)
               && _anchor == anchor
               && _useColliderAxes == useColliderAxes
               && _alignRotationToZone == alignRotationToZone;
    }

    public int GetAvailableSlotCount()
    {
        PruneReleasedReservations();
        RefreshCandidateCells();

        int available = 0;
        for (int i = 0; i < _candidateCells.Count; i++)
        {
            if (_occupiedSlots.Contains(_candidateCells[i].Key)) continue;
            available++;
        }

        return available;
    }

    public void GetPreviewCells(List<ColliderSurfaceGridCellPreview> results)
    {
        if (results == null) return;

        results.Clear();
        PruneReleasedReservations();
        RefreshCandidateCells();

        for (int i = 0; i < _candidateCells.Count; i++)
        {
            var cell = _candidateCells[i];
            results.Add(new ColliderSurfaceGridCellPreview(
                cell.Key,
                cell.X,
                cell.Z,
                cell.Position,
                cell.Rotation,
                _occupiedSlots.Contains(cell.Key)));
        }
    }

    public void ResetPlaced()
    {
        _requestPlacements.Clear();
        PruneReleasedReservations();
        RefreshCandidateCells();
    }

    public bool TryGetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
    {
        if (_requestPlacements.TryGetValue(index, out var reservedPlacement))
        {
            position = reservedPlacement.Position;
            rotation = reservedPlacement.Rotation;
            return true;
        }

        position = _collider != null ? _collider.bounds.center : Vector3.zero;
        rotation = Quaternion.identity;

        if (!TryReserveNextCell(out reservedPlacement))
            return false;

        _requestPlacements[index] = reservedPlacement;
        position = reservedPlacement.Position;
        rotation = reservedPlacement.Rotation;
        return true;
    }

    public void GetPose(int index, uint seed, out Vector3 position, out Quaternion rotation)
    {
        if (!TryGetPose(index, seed, out position, out rotation))
        {
            position = _collider != null ? _collider.bounds.center : Vector3.zero;
            rotation = Quaternion.identity;
        }
    }

    public void OnSpawnSucceeded(int index, GameObject instance)
    {
        if (instance == null)
        {
            OnSpawnFailed(index);
            return;
        }

        if (!_requestPlacements.TryGetValue(index, out var reservedPlacement))
            return;

        var reservation = instance.GetComponent<SpawnGridSlotReservation>();
        if (reservation == null)
            reservation = instance.AddComponent<SpawnGridSlotReservation>();

        reservation.Bind(this, reservedPlacement.Key);
        _slotReservations[reservedPlacement.Key] = reservation;
        _requestPlacements.Remove(index);
    }

    public void OnSpawnFailed(int index)
    {
        if (!_requestPlacements.TryGetValue(index, out var reservedPlacement))
            return;

        _occupiedSlots.Remove(reservedPlacement.Key);
        _requestPlacements.Remove(index);
    }

    void IGridSlotReservationOwner.ReleaseReservedSlot(long slotKey, SpawnGridSlotReservation reservation)
    {
        if (_slotReservations.TryGetValue(slotKey, out var currentReservation))
        {
            if (currentReservation == null || currentReservation == reservation)
                _slotReservations.Remove(slotKey);
        }

        _occupiedSlots.Remove(slotKey);
    }

    private bool TryReserveNextCell(out ReservedPlacement reservedPlacement)
    {
        reservedPlacement = default;
        RefreshCandidateCells();

        for (int i = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (_occupiedSlots.Contains(candidate.Key)) continue;

            _occupiedSlots.Add(candidate.Key);
            reservedPlacement = new ReservedPlacement(candidate.Key, candidate.Position, candidate.Rotation);
            return true;
        }

        return false;
    }

    private void RefreshCandidateCells()
    {
        _candidateCells.Clear();

        if (!TryBuildFrame(out var frame))
            return;

        int maxX = Mathf.Max(0, Mathf.CeilToInt(frame.HalfRangeX / _cellSize));
        int maxZ = Mathf.Max(0, Mathf.CeilToInt(frame.HalfRangeZ / _cellSize));

        for (int z = -maxZ; z <= maxZ; z++)
        {
            for (int x = -maxX; x <= maxX; x++)
            {
                if (!TryCreateCandidate(frame, x, z, out var candidate)) continue;
                _candidateCells.Add(candidate);
            }
        }

        _candidateCells.Sort(GridCellComparer.Instance);
    }

    private bool TryBuildFrame(out GridFrame frame)
    {
        frame = default;

        if (_collider == null || !_collider.enabled || !_collider.gameObject.activeInHierarchy)
            return false;

        var bounds = _collider.bounds;
        if (bounds.size.sqrMagnitude <= Mathf.Epsilon)
            return false;

        Vector3 up = _useColliderAxes ? _collider.transform.up : Vector3.up;
        if (up.sqrMagnitude <= Mathf.Epsilon)
            up = Vector3.up;
        else
            up.Normalize();

        Vector3 right = _useColliderAxes ? _collider.transform.right : Vector3.right;
        right = Vector3.ProjectOnPlane(right, up);
        if (right.sqrMagnitude <= Mathf.Epsilon)
            right = Vector3.Cross(up, Vector3.forward);
        if (right.sqrMagnitude <= Mathf.Epsilon)
            right = Vector3.Cross(up, Vector3.right);
        right.Normalize();

        Vector3 forward = Vector3.Cross(up, right);
        if (forward.sqrMagnitude <= Mathf.Epsilon)
            forward = Vector3.forward;
        else
            forward.Normalize();

        Vector3 anchorCenter = ResolveAnchorCenter(bounds.center, up);
        Quaternion rotation = _alignRotationToZone
            ? Quaternion.LookRotation(forward, up)
            : Quaternion.identity;

        float halfRangeX = Mathf.Max(0f, ProjectAabbHalfExtent(bounds.extents, right) - _edgePadding);
        float halfRangeZ = Mathf.Max(0f, ProjectAabbHalfExtent(bounds.extents, forward) - _edgePadding);

        frame = new GridFrame(anchorCenter, right, forward, up, rotation, halfRangeX, halfRangeZ);
        return true;
    }

    private bool TryCreateCandidate(in GridFrame frame, int x, int z, out GridCell candidate)
    {
        candidate = default;

        Vector3 surfacePoint = frame.Center + frame.Right * (x * _cellSize) + frame.Forward * (z * _cellSize);
        Vector3 closestPoint = _collider.ClosestPoint(surfacePoint);
        if ((closestPoint - surfacePoint).sqrMagnitude > ClosestPointToleranceSqr)
            return false;

        Vector3 position = surfacePoint + frame.Up * _verticalOffset;
        candidate = new GridCell(
            PackKey(x, z),
            x,
            z,
            position,
            frame.Rotation,
            x * x + z * z,
            Mathf.Abs(x) + Mathf.Abs(z));
        return true;
    }

    private Vector3 ResolveAnchorCenter(Vector3 colliderCenter, Vector3 up)
    {
        switch (_anchor)
        {
            case ColliderGridPlaneAnchor.Bottom:
                return ResolveSupportPoint(colliderCenter, -up);
            case ColliderGridPlaneAnchor.Top:
                return ResolveSupportPoint(colliderCenter, up);
            case ColliderGridPlaneAnchor.Center:
            default:
                return colliderCenter;
        }
    }

    private Vector3 ResolveSupportPoint(Vector3 colliderCenter, Vector3 direction)
    {
        float castDistance = Mathf.Max(_collider.bounds.extents.magnitude * 4f, _cellSize * 4f, 1f);
        Vector3 target = colliderCenter + direction.normalized * castDistance;
        return _collider.ClosestPoint(target);
    }

    private void PruneReleasedReservations()
    {
        if (_slotReservations.Count == 0) return;

        _releasedKeysBuffer.Clear();

        foreach (var pair in _slotReservations)
        {
            var reservation = pair.Value;
            if (reservation != null && reservation.IsBoundTo(this, pair.Key))
                continue;

            _releasedKeysBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _releasedKeysBuffer.Count; i++)
        {
            long key = _releasedKeysBuffer[i];
            _slotReservations.Remove(key);
            _occupiedSlots.Remove(key);
        }
    }

    private static float ProjectAabbHalfExtent(Vector3 extents, Vector3 axis)
    {
        Vector3 absAxis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        return extents.x * absAxis.x + extents.y * absAxis.y + extents.z * absAxis.z;
    }

    private static long PackKey(int x, int z)
    {
        unchecked
        {
            return ((long)x << 32) ^ (uint)z;
        }
    }

    private readonly struct GridFrame
    {
        public readonly Vector3 Center;
        public readonly Vector3 Right;
        public readonly Vector3 Forward;
        public readonly Vector3 Up;
        public readonly Quaternion Rotation;
        public readonly float HalfRangeX;
        public readonly float HalfRangeZ;

        public GridFrame(
            Vector3 center,
            Vector3 right,
            Vector3 forward,
            Vector3 up,
            Quaternion rotation,
            float halfRangeX,
            float halfRangeZ)
        {
            Center = center;
            Right = right;
            Forward = forward;
            Up = up;
            Rotation = rotation;
            HalfRangeX = halfRangeX;
            HalfRangeZ = halfRangeZ;
        }
    }

    private readonly struct GridCell
    {
        public readonly long Key;
        public readonly int X;
        public readonly int Z;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly int RadiusSqr;
        public readonly int ManhattanDistance;

        public GridCell(long key, int x, int z, Vector3 position, Quaternion rotation, int radiusSqr, int manhattanDistance)
        {
            Key = key;
            X = x;
            Z = z;
            Position = position;
            Rotation = rotation;
            RadiusSqr = radiusSqr;
            ManhattanDistance = manhattanDistance;
        }
    }

    private readonly struct ReservedPlacement
    {
        public readonly long Key;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public ReservedPlacement(long key, Vector3 position, Quaternion rotation)
        {
            Key = key;
            Position = position;
            Rotation = rotation;
        }
    }

    private sealed class GridCellComparer : IComparer<GridCell>
    {
        public static readonly GridCellComparer Instance = new GridCellComparer();

        public int Compare(GridCell a, GridCell b)
        {
            int compare = a.RadiusSqr.CompareTo(b.RadiusSqr);
            if (compare != 0) return compare;

            compare = a.ManhattanDistance.CompareTo(b.ManhattanDistance);
            if (compare != 0) return compare;

            compare = Mathf.Abs(a.Z).CompareTo(Mathf.Abs(b.Z));
            if (compare != 0) return compare;

            compare = Mathf.Abs(a.X).CompareTo(Mathf.Abs(b.X));
            if (compare != 0) return compare;

            compare = a.Z.CompareTo(b.Z);
            if (compare != 0) return compare;

            return a.X.CompareTo(b.X);
        }
    }
}

public enum ColliderGridPlaneAnchor
{
    Center = 0,
    Bottom = 1,
    Top = 2,
}

public readonly struct ColliderSurfaceGridCellPreview
{
    public readonly long Key;
    public readonly int X;
    public readonly int Z;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly bool IsOccupied;

    public ColliderSurfaceGridCellPreview(
        long key,
        int x,
        int z,
        Vector3 position,
        Quaternion rotation,
        bool isOccupied)
    {
        Key = key;
        X = x;
        Z = z;
        Position = position;
        Rotation = rotation;
        IsOccupied = isOccupied;
    }
}

public interface IGridSlotReservationOwner
{
    void ReleaseReservedSlot(long slotKey, SpawnGridSlotReservation reservation);
}

/// <summary>
/// Persists a grid slot reservation on a pooled instance and releases it when the instance leaves play.
/// </summary>
public sealed class SpawnGridSlotReservation : MonoBehaviour, ISpawnPoolCallbacks
{
    private IGridSlotReservationOwner _owner;
    private long _slotKey;
    private bool _isBound;

    public void Bind(IGridSlotReservationOwner owner, long slotKey)
    {
        ReleaseReservation();
        _owner = owner;
        _slotKey = slotKey;
        _isBound = owner != null;
    }

    public bool IsBoundTo(IGridSlotReservationOwner owner, long slotKey)
    {
        return _isBound && _owner == owner && _slotKey == slotKey;
    }

    public void OnSpawnedFromPool()
    {
    }

    public void OnDespawnedToPool()
    {
        ReleaseReservation();
    }

    private void OnDestroy()
    {
        ReleaseReservation();
    }

    private void ReleaseReservation()
    {
        if (!_isBound) return;

        var owner = _owner;
        long slotKey = _slotKey;

        _owner = null;
        _slotKey = 0;
        _isBound = false;

        owner?.ReleaseReservedSlot(slotKey, this);
    }
}
}
