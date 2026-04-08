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
    private const int CenterCellCount = 5;

    private readonly Collider _collider;
    private readonly float _cellSize;
    private readonly float _edgePadding;
    private readonly float _verticalOffset;
    private readonly ColliderGridPlaneAnchor _anchor;
    private readonly ColliderSurfaceGridCenterCell _preferredCenterCell;
    private readonly bool _includeCenterSlot;
    private readonly bool _useColliderAxes;
    private readonly bool _alignRotationToZone;
    private readonly bool _randomizeCellPositions;
    private readonly float _randomCellOffsetStrength;
    private readonly uint _randomCellOffsetSeed;

    private readonly List<GridCell> _candidateCells = new List<GridCell>(128);
    private readonly Dictionary<int, ReservedPlacement> _requestPlacements = new Dictionary<int, ReservedPlacement>(32);
    private readonly Dictionary<long, SpawnGridSlotReservation> _slotReservations = new Dictionary<long, SpawnGridSlotReservation>(128);
    private readonly HashSet<long> _occupiedSlots = new HashSet<long>();
    private readonly HashSet<long> _blockedSlots = new HashSet<long>();
    private readonly List<long> _releasedKeysBuffer = new List<long>(16);
    private readonly GridCell[] _centerCells = new GridCell[CenterCellCount];
    private readonly bool[] _hasCenterCells = new bool[CenterCellCount];
    private readonly float[] _centerCellDistances = new float[CenterCellCount];

    private int _sortReferenceX;
    private int _sortReferenceZ;

    public ColliderSurfaceGridAlgorithm(
        Collider collider,
        float cellSize = 0.75f,
        float edgePadding = 0f,
        float verticalOffset = 0f,
        ColliderGridPlaneAnchor anchor = ColliderGridPlaneAnchor.Bottom,
        ColliderSurfaceGridCenterCell preferredCenterCell = ColliderSurfaceGridCenterCell.WholeGrid,
        bool includeCenterSlot = true,
        bool useColliderAxes = true,
        bool alignRotationToZone = true,
        bool randomizeCellPositions = false,
        float randomCellOffsetStrength = 0f,
        int randomCellOffsetSeed = 0)
    {
        _collider = collider;
        _cellSize = Mathf.Max(0.01f, cellSize);
        _edgePadding = Mathf.Max(0f, edgePadding);
        _verticalOffset = verticalOffset;
        _anchor = anchor;
        _preferredCenterCell = preferredCenterCell;
        _includeCenterSlot = includeCenterSlot;
        _useColliderAxes = useColliderAxes;
        _alignRotationToZone = alignRotationToZone;
        _randomizeCellPositions = randomizeCellPositions;
        _randomCellOffsetStrength = Mathf.Clamp(randomCellOffsetStrength, 0f, 0.45f);
        _randomCellOffsetSeed = unchecked((uint)randomCellOffsetSeed);
    }

    public int OccupiedSlotCount
    {
        get
        {
            PruneReleasedReservations();
            return CountUnavailableSlots();
        }
    }

    public void SetBlockedSlots(IEnumerable<long> blockedSlots)
    {
        _blockedSlots.Clear();

        if (blockedSlots == null)
            return;

        foreach (long blockedSlot in blockedSlots)
            _blockedSlots.Add(blockedSlot);
    }

    public bool Matches(
        Collider collider,
        float cellSize,
        float edgePadding,
        float verticalOffset,
        ColliderGridPlaneAnchor anchor,
        ColliderSurfaceGridCenterCell preferredCenterCell,
        bool includeCenterSlot,
        bool useColliderAxes,
        bool alignRotationToZone,
        bool randomizeCellPositions,
        float randomCellOffsetStrength,
        int randomCellOffsetSeed)
    {
        return _collider == collider
               && Mathf.Approximately(_cellSize, Mathf.Max(0.01f, cellSize))
               && Mathf.Approximately(_edgePadding, Mathf.Max(0f, edgePadding))
               && Mathf.Approximately(_verticalOffset, verticalOffset)
               && _anchor == anchor
               && _preferredCenterCell == preferredCenterCell
               && _includeCenterSlot == includeCenterSlot
               && _useColliderAxes == useColliderAxes
               && _alignRotationToZone == alignRotationToZone
               && _randomizeCellPositions == randomizeCellPositions
               && Mathf.Approximately(_randomCellOffsetStrength, Mathf.Clamp(randomCellOffsetStrength, 0f, 0.45f))
               && _randomCellOffsetSeed == unchecked((uint)randomCellOffsetSeed);
    }

    public int GetAvailableSlotCount()
    {
        PruneReleasedReservations();
        RefreshCandidateCells();

        int available = 0;
        for (int i = 0; i < _candidateCells.Count; i++)
        {
            if (IsSlotUnavailable(_candidateCells[i].Key)) continue;
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
                IsSlotUnavailable(cell.Key)));
        }
    }

    public void GetCenterCellPreviews(List<ColliderSurfaceGridCenterCellPreview> results)
    {
        if (results == null) return;

        results.Clear();
        PruneReleasedReservations();
        RefreshCandidateCells();

        for (int i = 0; i < CenterCellCount; i++)
        {
            if (!_hasCenterCells[i]) continue;

            var centerCell = _centerCells[i];
            results.Add(new ColliderSurfaceGridCenterCellPreview(
                (ColliderSurfaceGridCenterCell)i,
                centerCell.Key,
                centerCell.Position,
                centerCell.Rotation,
                _preferredCenterCell == (ColliderSurfaceGridCenterCell)i));
        }
    }

    public bool TryReserveNextPlacement(out ColliderSurfaceGridPlacement placement)
    {
        placement = default;

        if (!TryReserveNextCell(out var reservedPlacement))
            return false;

        placement = new ColliderSurfaceGridPlacement(
            reservedPlacement.Key,
            reservedPlacement.Position,
            reservedPlacement.Rotation);
        return true;
    }

    public bool TryGetClosestPlacement(out ColliderSurfaceGridPlacement placement)
    {
        placement = default;
        PruneReleasedReservations();
        RefreshCandidateCells();

        if (_candidateCells.Count <= 0)
            return false;

        placement = CreatePlacement(_candidateCells[0]);
        return true;
    }

    public bool TryGetCenterPlacement(
        ColliderSurfaceGridCenterCell centerCell,
        out ColliderSurfaceGridPlacement placement)
    {
        placement = default;
        PruneReleasedReservations();
        RefreshCandidateCells();

        int centerCellIndex = (int)centerCell;
        if (centerCellIndex < 0 || centerCellIndex >= CenterCellCount)
            return false;

        if (!_hasCenterCells[centerCellIndex])
            return false;

        placement = CreatePlacement(_centerCells[centerCellIndex]);
        return true;
    }

    public bool TryGetNextAvailablePlacement(ISet<long> excludedKeys, out ColliderSurfaceGridPlacement placement)
    {
        placement = default;
        PruneReleasedReservations();
        RefreshCandidateCells();

        for (int i = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (IsSlotUnavailable(candidate.Key)) continue;
            if (excludedKeys != null && excludedKeys.Contains(candidate.Key)) continue;

            placement = CreatePlacement(candidate);
            return true;
        }

        return false;
    }

    public bool TryGetRandomAvailablePlacement(
        uint seed,
        uint salt,
        ISet<long> excludedKeys,
        out ColliderSurfaceGridPlacement placement)
    {
        placement = default;
        PruneReleasedReservations();
        RefreshCandidateCells();

        int availableCount = 0;
        for (int i = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (IsSlotUnavailable(candidate.Key)) continue;
            if (excludedKeys != null && excludedKeys.Contains(candidate.Key)) continue;
            availableCount++;
        }

        if (availableCount <= 0)
            return false;

        uint randomState = Hash(seed ^ salt ^ 0x9E3779B9u);
        int targetIndex = (int)(randomState % (uint)availableCount);

        for (int i = 0, availableIndex = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (IsSlotUnavailable(candidate.Key)) continue;
            if (excludedKeys != null && excludedKeys.Contains(candidate.Key)) continue;

            if (availableIndex == targetIndex)
            {
                placement = CreatePlacement(candidate);
                return true;
            }

            availableIndex++;
        }

        return false;
    }

    public bool TryGetRandomPlacement(
        uint seed,
        uint salt,
        ISet<long> excludedKeys,
        out ColliderSurfaceGridPlacement placement)
    {
        placement = default;
        PruneReleasedReservations();
        RefreshCandidateCells();

        int candidateCount = 0;
        for (int i = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (_blockedSlots.Contains(candidate.Key)) continue;
            if (excludedKeys != null && excludedKeys.Contains(candidate.Key)) continue;
            candidateCount++;
        }

        if (candidateCount <= 0)
            return false;

        uint randomState = Hash(seed ^ salt ^ 0x7f4a7c15u);
        int targetIndex = (int)(randomState % (uint)candidateCount);

        for (int i = 0, candidateIndex = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (_blockedSlots.Contains(candidate.Key)) continue;
            if (excludedKeys != null && excludedKeys.Contains(candidate.Key)) continue;

            if (candidateIndex == targetIndex)
            {
                placement = CreatePlacement(candidate);
                return true;
            }

            candidateIndex++;
        }

        return false;
    }

    public bool TryGetRandomOccupiedPlacement(
        uint seed,
        uint salt,
        ISet<long> excludedKeys,
        out ColliderSurfaceGridPlacement placement)
    {
        placement = default;
        PruneReleasedReservations();
        RefreshCandidateCells();

        int occupiedCount = 0;
        for (int i = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (_blockedSlots.Contains(candidate.Key)) continue;
            if (!_occupiedSlots.Contains(candidate.Key)) continue;
            if (excludedKeys != null && excludedKeys.Contains(candidate.Key)) continue;
            occupiedCount++;
        }

        if (occupiedCount <= 0)
            return false;

        uint randomState = Hash(seed ^ salt ^ 0x1f123bb5u);
        int targetIndex = (int)(randomState % (uint)occupiedCount);

        for (int i = 0, occupiedIndex = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (_blockedSlots.Contains(candidate.Key)) continue;
            if (!_occupiedSlots.Contains(candidate.Key)) continue;
            if (excludedKeys != null && excludedKeys.Contains(candidate.Key)) continue;

            if (occupiedIndex == targetIndex)
            {
                placement = CreatePlacement(candidate);
                return true;
            }

            occupiedIndex++;
        }

        return false;
    }

    public bool TryGetNearestAvailablePlacement(
        Vector3 referencePosition,
        ISet<long> excludedKeys,
        out ColliderSurfaceGridPlacement placement)
    {
        placement = default;
        PruneReleasedReservations();
        RefreshCandidateCells();

        bool found = false;
        float bestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (IsSlotUnavailable(candidate.Key)) continue;
            if (excludedKeys != null && excludedKeys.Contains(candidate.Key)) continue;

            float distanceSqr = (candidate.Position - referencePosition).sqrMagnitude;
            if (found && distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            placement = CreatePlacement(candidate);
            found = true;
        }

        return found;
    }

    public void BindReservation(GameObject instance, in ColliderSurfaceGridPlacement placement)
    {
        if (instance == null)
        {
            ReleasePlacement(placement.Key);
            return;
        }

        var reservation = instance.GetComponent<SpawnGridSlotReservation>();
        if (reservation == null)
            reservation = instance.AddComponent<SpawnGridSlotReservation>();

        reservation.Bind(this, placement.Key);
        _occupiedSlots.Add(placement.Key);
        _slotReservations[placement.Key] = reservation;
    }

    public void ReleaseReservation(long slotKey)
    {
        ReleasePlacement(slotKey);
    }

    public bool IsSlotOccupied(long slotKey)
    {
        PruneReleasedReservations();
        return _occupiedSlots.Contains(slotKey);
    }

    public bool TryGetPlacementPose(long slotKey, out Vector3 position, out Quaternion rotation)
    {
        position = _collider != null ? _collider.bounds.center : Vector3.zero;
        rotation = Quaternion.identity;

        if (!TryBuildFrame(out var frame))
            return false;

        UnpackKey(slotKey, out int x, out int z);
        if (!TryCreateCandidate(frame, x, z, out var candidate))
            return false;

        position = candidate.Position;
        rotation = candidate.Rotation;
        return true;
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

    private void ReleasePlacement(long slotKey)
    {
        _slotReservations.Remove(slotKey);
        _occupiedSlots.Remove(slotKey);
    }

    private bool TryReserveNextCell(out ReservedPlacement reservedPlacement)
    {
        reservedPlacement = default;
        RefreshCandidateCells();

        for (int i = 0; i < _candidateCells.Count; i++)
        {
            var candidate = _candidateCells[i];
            if (IsSlotUnavailable(candidate.Key)) continue;

            _occupiedSlots.Add(candidate.Key);
            reservedPlacement = new ReservedPlacement(candidate.Key, candidate.Position, candidate.Rotation);
            return true;
        }

        return false;
    }

    private static ColliderSurfaceGridPlacement CreatePlacement(in GridCell cell)
    {
        return new ColliderSurfaceGridPlacement(cell.Key, cell.Position, cell.Rotation);
    }

    private void RefreshCandidateCells()
    {
        _candidateCells.Clear();

        if (!TryBuildFrame(out var frame))
            return;

        // Floor here so edgePadding truly removes the outer ring of centers near the collider boundary.
        int maxX = Mathf.Max(0, Mathf.FloorToInt(frame.HalfRangeX / _cellSize));
        int maxZ = Mathf.Max(0, Mathf.FloorToInt(frame.HalfRangeZ / _cellSize));

        for (int z = -maxZ; z <= maxZ; z++)
        {
            for (int x = -maxX; x <= maxX; x++)
            {
                if (!_includeCenterSlot && x == 0 && z == 0) continue;
                if (!TryCreateCandidate(frame, x, z, out var candidate)) continue;
                _candidateCells.Add(candidate);
            }
        }

        RefreshCenterCells(frame);
        _candidateCells.Sort(CompareGridCells);
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

        Vector3 baseSurfacePoint = frame.Center + frame.Right * (x * _cellSize) + frame.Forward * (z * _cellSize);
        if (!IsPointInsideCollider(baseSurfacePoint))
            return false;

        Vector3 surfacePoint = baseSurfacePoint;
        if (_randomizeCellPositions && _randomCellOffsetStrength > 0f)
        {
            Vector3 randomizedSurfacePoint = baseSurfacePoint + ResolveCellOffset(frame, x, z);
            if (IsPointInsideCollider(randomizedSurfacePoint))
                surfacePoint = randomizedSurfacePoint;
        }

        Vector3 position = surfacePoint + frame.Up * _verticalOffset;
        candidate = new GridCell(
            PackKey(x, z),
            x,
            z,
            position,
            frame.Rotation);
        return true;
    }

    private void RefreshCenterCells(in GridFrame frame)
    {
        for (int i = 0; i < CenterCellCount; i++)
        {
            _hasCenterCells[i] = false;
            _centerCellDistances[i] = float.PositiveInfinity;
        }

        for (int i = 0; i < _candidateCells.Count; i++)
        {
            GridCell candidate = _candidateCells[i];

            for (int centerCellIndex = 0; centerCellIndex < CenterCellCount; centerCellIndex++)
            {
                Vector3 targetPosition = ResolveCenterCellTarget(frame, (ColliderSurfaceGridCenterCell)centerCellIndex);
                float distanceSqr = (candidate.Position - targetPosition).sqrMagnitude;
                if (_hasCenterCells[centerCellIndex] && distanceSqr >= _centerCellDistances[centerCellIndex])
                    continue;

                _centerCells[centerCellIndex] = candidate;
                _centerCellDistances[centerCellIndex] = distanceSqr;
                _hasCenterCells[centerCellIndex] = true;
            }
        }

        int preferredCenterCellIndex = Mathf.Clamp((int)_preferredCenterCell, 0, CenterCellCount - 1);
        if (_hasCenterCells[preferredCenterCellIndex])
        {
            _sortReferenceX = _centerCells[preferredCenterCellIndex].X;
            _sortReferenceZ = _centerCells[preferredCenterCellIndex].Z;
            return;
        }

        _sortReferenceX = 0;
        _sortReferenceZ = 0;
    }

    private Vector3 ResolveCenterCellTarget(in GridFrame frame, ColliderSurfaceGridCenterCell centerCell)
    {
        float halfCenterOffsetX = frame.HalfRangeX * 0.5f;
        float halfCenterOffsetZ = frame.HalfRangeZ * 0.5f;

        switch (centerCell)
        {
            case ColliderSurfaceGridCenterCell.TopLeftQuadrant:
                return frame.Center - frame.Right * halfCenterOffsetX + frame.Forward * halfCenterOffsetZ;

            case ColliderSurfaceGridCenterCell.TopRightQuadrant:
                return frame.Center + frame.Right * halfCenterOffsetX + frame.Forward * halfCenterOffsetZ;

            case ColliderSurfaceGridCenterCell.BottomLeftQuadrant:
                return frame.Center - frame.Right * halfCenterOffsetX - frame.Forward * halfCenterOffsetZ;

            case ColliderSurfaceGridCenterCell.BottomRightQuadrant:
                return frame.Center + frame.Right * halfCenterOffsetX - frame.Forward * halfCenterOffsetZ;

            case ColliderSurfaceGridCenterCell.WholeGrid:
            default:
                return frame.Center;
        }
    }

    private bool IsPointInsideCollider(Vector3 point)
    {
        Vector3 closestPoint = _collider.ClosestPoint(point);
        return (closestPoint - point).sqrMagnitude <= ClosestPointToleranceSqr;
    }

    private Vector3 ResolveCellOffset(in GridFrame frame, int x, int z)
    {
        uint hashX = Hash(PackCellHash(x, z, 0x68bc21ebu));
        uint hashZ = Hash(PackCellHash(x, z, 0x02e5be93u));

        float offsetRange = _cellSize * _randomCellOffsetStrength;
        float offsetX = Mathf.Lerp(-offsetRange, offsetRange, hashX / (float)uint.MaxValue);
        float offsetZ = Mathf.Lerp(-offsetRange, offsetRange, hashZ / (float)uint.MaxValue);

        return frame.Right * offsetX + frame.Forward * offsetZ;
    }

    private uint PackCellHash(int x, int z, uint salt)
    {
        unchecked
        {
            uint hash = _randomCellOffsetSeed ^ salt;
            hash ^= (uint)x * 0x9E3779B9u;
            hash ^= (uint)z * 0x85EBCA6Bu;
            return hash;
        }
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

    private bool IsSlotUnavailable(long slotKey)
    {
        return _occupiedSlots.Contains(slotKey) || _blockedSlots.Contains(slotKey);
    }

    private int CountUnavailableSlots()
    {
        if (_blockedSlots.Count <= 0)
            return _occupiedSlots.Count;

        int unavailableCount = _occupiedSlots.Count;
        foreach (long blockedSlot in _blockedSlots)
        {
            if (_occupiedSlots.Contains(blockedSlot))
                continue;

            unavailableCount++;
        }

        return unavailableCount;
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

    private static void UnpackKey(long key, out int x, out int z)
    {
        x = (int)(key >> 32);
        z = (int)key;
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    private int CompareGridCells(GridCell a, GridCell b)
    {
        int ax = a.X - _sortReferenceX;
        int az = a.Z - _sortReferenceZ;
        int bx = b.X - _sortReferenceX;
        int bz = b.Z - _sortReferenceZ;

        int aRadiusSqr = ax * ax + az * az;
        int bRadiusSqr = bx * bx + bz * bz;

        int compare = aRadiusSqr.CompareTo(bRadiusSqr);
        if (compare != 0) return compare;

        int aManhattanDistance = Mathf.Abs(ax) + Mathf.Abs(az);
        int bManhattanDistance = Mathf.Abs(bx) + Mathf.Abs(bz);
        compare = aManhattanDistance.CompareTo(bManhattanDistance);
        if (compare != 0) return compare;

        compare = Mathf.Abs(az).CompareTo(Mathf.Abs(bz));
        if (compare != 0) return compare;

        compare = Mathf.Abs(ax).CompareTo(Mathf.Abs(bx));
        if (compare != 0) return compare;

        compare = az.CompareTo(bz);
        if (compare != 0) return compare;

        return ax.CompareTo(bx);
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

        public GridCell(long key, int x, int z, Vector3 position, Quaternion rotation)
        {
            Key = key;
            X = x;
            Z = z;
            Position = position;
            Rotation = rotation;
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

}

public enum ColliderSurfaceGridCenterCell
{
    WholeGrid = 0,
    TopLeftQuadrant = 1,
    TopRightQuadrant = 2,
    BottomLeftQuadrant = 3,
    BottomRightQuadrant = 4,
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

public readonly struct ColliderSurfaceGridCenterCellPreview
{
    public readonly ColliderSurfaceGridCenterCell CenterCell;
    public readonly long Key;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly bool IsSelected;

    public ColliderSurfaceGridCenterCellPreview(
        ColliderSurfaceGridCenterCell centerCell,
        long key,
        Vector3 position,
        Quaternion rotation,
        bool isSelected)
    {
        CenterCell = centerCell;
        Key = key;
        Position = position;
        Rotation = rotation;
        IsSelected = isSelected;
    }
}

public readonly struct ColliderSurfaceGridPlacement
{
    public readonly long Key;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;

    public ColliderSurfaceGridPlacement(long key, Vector3 position, Quaternion rotation)
    {
        Key = key;
        Position = position;
        Rotation = rotation;
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

    public long SlotKey => _slotKey;
    public bool IsBound => _isBound;

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

    public void ReleaseReservationNow()
    {
        ReleaseReservation();
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
