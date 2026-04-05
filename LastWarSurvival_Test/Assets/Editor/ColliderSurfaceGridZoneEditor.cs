using UnityEditor;
using UnityEngine;
using Vit.SpawnKit.Components;

[CustomEditor(typeof(ColliderSurfaceGridZone))]
public sealed class ColliderSurfaceGridZoneEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
            SceneView.RepaintAll();

        if (targets.Length != 1) return;

        var zone = (ColliderSurfaceGridZone)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Info", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Resolved Collider", zone.ZoneCollider, typeof(Collider), true);
            EditorGUILayout.IntField("Occupied Slots", zone.OccupiedSlotCount);
            EditorGUILayout.IntField("Available Slots", zone.GetAvailableSlotCount());
        }

        if (zone.ZoneCollider == null)
        {
            EditorGUILayout.HelpBox(
                "Gan component nay len GameObject co Collider, hoac set truong Zone Collider thu cong.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            "Grid preview duoc ve trong Scene view. Khi Draw Preview Only When Selected dang bat, hay chon zone de xem luoi.",
            MessageType.Info);
    }
}
