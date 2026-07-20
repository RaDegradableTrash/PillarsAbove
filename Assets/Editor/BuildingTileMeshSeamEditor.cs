using System.Collections.Generic;
using PillarsAbove.BuildingTiles;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BuildingTileDefinition))]
public sealed class BuildingTileMeshSeamEditor : Editor
{
    private static readonly TileFace[] Faces =
    {
        TileFace.PositiveX, TileFace.NegativeX, TileFace.PositiveY,
        TileFace.NegativeY, TileFace.PositiveZ, TileFace.NegativeZ
    };

    private static readonly Color[] FaceColors =
    {
        new Color(1f, .3f, .3f), new Color(.65f, .1f, .1f),
        new Color(.3f, 1f, .3f), new Color(.1f, .65f, .1f),
        new Color(.3f, .65f, 1f), new Color(.1f, .3f, .7f)
    };

    private int selectedFace;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var definition = (BuildingTileDefinition)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Mesh Seam Cage", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each colored handle binds nearby mesh vertices. Neighboring cells pair handles one-to-one and move both meshes to their midpoint. Keep opposite faces at the same handle count.",
            MessageType.Info);
        selectedFace = GUILayout.Toolbar(selectedFace, new[] { "+X", "-X", "+Y", "-Y", "+Z", "-Z" });

        if (GUILayout.Button("Open Six-Face Detail Editor", GUILayout.Height(28f)))
            BuildingTileSixFaceEditorWindow.Open(definition, Faces[selectedFace]);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create 6 Face Cages")) InitializeFromMeshBounds(definition);
            if (GUILayout.Button("Snap Handles To Mesh")) SnapToNearestVertices(definition);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Handle To Selected Face")) AddHandle(definition, Faces[selectedFace]);
            if (GUILayout.Button("Validate Pair Counts")) ValidatePairCounts(definition);
        }
    }

    private void OnSceneGUI()
    {
        var definition = (BuildingTileDefinition)target;
        var profile = definition.MeshSeams.Find(Faces[selectedFace]);
        if (profile == null || profile.Anchors.Count == 0) return;

        Handles.color = FaceColors[selectedFace];
        var points = profile.Anchors;
        for (var i = 0; i < points.Count; i++)
        {
            var world = definition.transform.TransformPoint(points[i]);
            var size = HandleUtility.GetHandleSize(world) * 0.065f;
            EditorGUI.BeginChangeCheck();
            var moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);
            Handles.Label(world, Faces[selectedFace] + " " + i);
            if (!EditorGUI.EndChangeCheck()) continue;
            Undo.RecordObject(definition, "Move tile seam handle");
            points[i] = definition.transform.InverseTransformPoint(moved);
            EditorUtility.SetDirty(definition);
        }

        if (points.Count > 1)
        {
            var loop = new Vector3[points.Count + 1];
            for (var i = 0; i < points.Count; i++) loop[i] = definition.transform.TransformPoint(points[i]);
            loop[points.Count] = loop[0];
            Handles.DrawAAPolyLine(3f, loop);
        }
    }

    [MenuItem("Tools/Pillars Above/Mesh Seams/Create Cages On Selected Tiles")]
    private static void CreateCagesOnSelection()
    {
        var definitions = Selection.GetFiltered<BuildingTileDefinition>(
            SelectionMode.Deep | SelectionMode.DeepAssets | SelectionMode.Editable);
        if (definitions.Length == 0)
        {
            Debug.LogWarning("Select one or more BuildingTile prefab roots first.");
            return;
        }
        foreach (var definition in definitions) InitializeFromMeshBounds(definition);
    }

    private static void InitializeFromMeshBounds(BuildingTileDefinition definition)
    {
        if (!TryGetRootBounds(definition, out var bounds))
        {
            Debug.LogWarning("No readable MeshFilter was found under " + definition.name + ".", definition);
            return;
        }
        Undo.RecordObject(definition, "Create tile seam cages");
        definition.MeshSeams.Profiles.Clear();
        for (var i = 0; i < Faces.Length; i++)
            definition.MeshSeams.Profiles.Add(new BuildingTileSeamProfile(Faces[i], BuildingTileMeshDeformer.BoundsFaceCorners(bounds, Faces[i])));
        SnapToNearestVertices(definition, false);
        EditorUtility.SetDirty(definition);
        SceneView.RepaintAll();
    }

    private static void SnapToNearestVertices(BuildingTileDefinition definition, bool recordUndo = true)
    {
        var vertices = CollectRootVertices(definition);
        if (vertices.Count == 0) return;
        if (recordUndo) Undo.RecordObject(definition, "Snap tile seam handles");
        foreach (var profile in definition.MeshSeams.Profiles)
        {
            if (profile == null) continue;
            for (var i = 0; i < profile.Anchors.Count; i++)
            {
                var best = vertices[0];
                var bestDistance = (best - profile.Anchors[i]).sqrMagnitude;
                for (var v = 1; v < vertices.Count; v++)
                {
                    var distance = (vertices[v] - profile.Anchors[i]).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = vertices[v];
                }
                profile.Anchors[i] = best;
            }
        }
        EditorUtility.SetDirty(definition);
        SceneView.RepaintAll();
    }

    private static void AddHandle(BuildingTileDefinition definition, TileFace face)
    {
        var profile = definition.MeshSeams.Find(face);
        if (profile == null)
        {
            if (!TryGetRootBounds(definition, out var bounds)) return;
            profile = new BuildingTileSeamProfile(face, BuildingTileMeshDeformer.BoundsFaceCorners(bounds, face));
            Undo.RecordObject(definition, "Add tile seam profile");
            definition.MeshSeams.Profiles.Add(profile);
        }
        else
        {
            Undo.RecordObject(definition, "Add tile seam handle");
            var anchors = profile.Anchors;
            anchors.Add(anchors.Count == 0 ? Vector3.zero : anchors[anchors.Count - 1]);
        }
        EditorUtility.SetDirty(definition);
    }

    private static void ValidatePairCounts(BuildingTileDefinition definition)
    {
        var pairs = new[]
        {
            new[] { TileFace.PositiveX, TileFace.NegativeX },
            new[] { TileFace.PositiveY, TileFace.NegativeY },
            new[] { TileFace.PositiveZ, TileFace.NegativeZ }
        };
        for (var i = 0; i < pairs.Length; i++)
        {
            var a = definition.MeshSeams.Find(pairs[i][0]);
            var b = definition.MeshSeams.Find(pairs[i][1]);
            var countA = a == null ? 0 : a.Anchors.Count;
            var countB = b == null ? 0 : b.Anchors.Count;
            if (countA != countB)
            {
                Debug.LogError(definition.name + " seam mismatch: " + pairs[i][0] + " has " + countA + " handles, " + pairs[i][1] + " has " + countB + ".", definition);
                return;
            }
        }
        Debug.Log(definition.name + " seam handle counts are valid for one-to-one stitching.", definition);
    }

    private static bool TryGetRootBounds(BuildingTileDefinition definition, out Bounds bounds)
    {
        var vertices = CollectRootVertices(definition);
        bounds = new Bounds();
        if (vertices.Count == 0) return false;
        bounds = new Bounds(vertices[0], Vector3.zero);
        for (var i = 1; i < vertices.Count; i++) bounds.Encapsulate(vertices[i]);
        return true;
    }

    private static List<Vector3> CollectRootVertices(BuildingTileDefinition definition)
    {
        var result = new List<Vector3>();
        var filters = definition.GetComponentsInChildren<MeshFilter>(true);
        for (var i = 0; i < filters.Length; i++)
        {
            var mesh = filters[i].sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var matrix = definition.transform.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
            var vertices = mesh.vertices;
            for (var v = 0; v < vertices.Length; v++) result.Add(matrix.MultiplyPoint3x4(vertices[v]));
        }
        return result;
    }
}
