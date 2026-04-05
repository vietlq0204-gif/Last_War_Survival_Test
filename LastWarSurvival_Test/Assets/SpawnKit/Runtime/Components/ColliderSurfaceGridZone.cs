using System.Collections.Generic;
using UnityEngine;
using Vit.SpawnKit.Algorithms;

namespace Vit.SpawnKit.Components
{
/// <summary>
/// Authoring component for a collider-backed grid spawn zone with scene preview.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("SpawnKit/Collider Surface Grid Zone")]
public sealed class ColliderSurfaceGridZone : MonoBehaviour
{
    [Header("Zone")]
    [Tooltip("Something...")]
    [SerializeField] private Collider zoneCollider;
    [SerializeField, Min(0.1f)] private float cellSize = 0.5f;
    [SerializeField, Min(0f)] private float edgePadding = 0.05f;
    [SerializeField] private ColliderGridPlaneAnchor anchor = ColliderGridPlaneAnchor.Center;
    [SerializeField] private float verticalOffset = 0f;
    [SerializeField] private bool includeCenterSlot = false;
    [SerializeField] private bool useColliderAxes = true;
    [SerializeField] private bool alignRotationToZone = true;

    [Header("Preview")]
    [SerializeField] private bool drawGridPreview = true;
    [SerializeField] private bool drawPreviewOnlyWhenSelected = false;
    [SerializeField] private bool drawFilledCells = true;
    [SerializeField, Range(0.1f, 1f)] private float previewCellFill = 0.2f;
    [SerializeField, Min(0.001f)] private float previewThickness = 0.02f;
    [SerializeField] private bool drawColliderBounds = true;
    [SerializeField] private Color availableCellColor = Color.green;
    [SerializeField] private Color occupiedCellColor = Color.orange;
    [SerializeField] private Color gridOutlineColor = Color.white;
    [SerializeField] private Color colliderBoundsColor = Color.blue;

    private readonly List<ColliderSurfaceGridCellPreview> _previewCells = new List<ColliderSurfaceGridCellPreview>(256);
    private ColliderSurfaceGridAlgorithm _algorithm;

    public Collider ZoneCollider => ResolveCollider();
    public int OccupiedSlotCount => ResolveAlgorithm() != null ? _algorithm.OccupiedSlotCount : 0;

    public ColliderSurfaceGridAlgorithm ResolveAlgorithm()
    {
        var collider = ResolveCollider();
        if (collider == null)
        {
            _algorithm = null;
            return null;
        }

        if (_algorithm != null && _algorithm.Matches(
                collider,
                cellSize,
                edgePadding,
                verticalOffset,
                anchor,
                includeCenterSlot,
                useColliderAxes,
                alignRotationToZone))
            return _algorithm;

        _algorithm = new ColliderSurfaceGridAlgorithm(
            collider,
            cellSize,
            edgePadding,
            verticalOffset,
            anchor,
            includeCenterSlot,
            useColliderAxes,
            alignRotationToZone);
        return _algorithm;
    }

    public int GetAvailableSlotCount()
    {
        var algorithm = ResolveAlgorithm();
        return algorithm != null ? algorithm.GetAvailableSlotCount() : 0;
    }

    private void Reset()
    {
        CacheColliderReference();
    }

    private void OnEnable()
    {
        CacheColliderReference();
    }

    private void OnValidate()
    {
        cellSize = Mathf.Max(0.01f, cellSize);
        edgePadding = Mathf.Max(0f, edgePadding);
        previewCellFill = Mathf.Clamp(previewCellFill, 0.1f, 1f);
        previewThickness = Mathf.Max(0.001f, previewThickness);
        CacheColliderReference();
    }

    private void OnDrawGizmos()
    {
        if (!drawGridPreview || drawPreviewOnlyWhenSelected) return;
        DrawPreviewGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGridPreview) return;
        DrawPreviewGizmos();
    }

    private void DrawPreviewGizmos()
    {
        var algorithm = ResolveAlgorithm();
        var collider = ResolveCollider();
        if (algorithm == null || collider == null) return;

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
                Gizmos.color = cell.IsOccupied ? occupiedCellColor : availableCellColor;
                Gizmos.DrawCube(Vector3.zero, previewSize);
            }

            Gizmos.color = gridOutlineColor;
            Gizmos.DrawWireCube(Vector3.zero, previewSize);
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private Collider ResolveCollider()
    {
        if (zoneCollider != null) return zoneCollider;
        CacheColliderReference();
        return zoneCollider;
    }

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
