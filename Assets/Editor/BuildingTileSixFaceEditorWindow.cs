using System;
using System.Collections.Generic;
using PillarsAbove.BuildingTiles;
using UnityEditor;
using UnityEngine;

public sealed class BuildingTileSixFaceEditorWindow : EditorWindow
{
    private struct Segment
    {
        public Vector3 A;
        public Vector3 B;
    }

    private sealed class FaceGeometry
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Segment> Segments = new List<Segment>();
        public Bounds Bounds;
        public bool HasBounds;
    }

    private static readonly TileFace[] Faces =
    {
        TileFace.PositiveX, TileFace.NegativeX, TileFace.PositiveY,
        TileFace.NegativeY, TileFace.PositiveZ, TileFace.NegativeZ
    };

    private static readonly string[] FaceLabels = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
    private static readonly Color MeshEdgeColor = new Color(.36f, .42f, .5f, .8f);
    private static readonly Color VertexColor = new Color(.15f, .82f, 1f, 1f);
    private static readonly Color BoundVertexColor = new Color(.2f, 1f, .42f, 1f);
    private static readonly Color MidpointColor = new Color(1f, .78f, .18f, 1f);
    private static readonly Color MarkerColor = new Color(1f, .2f, .78f, 1f);

    [SerializeField] private BuildingTileDefinition definition;
    [SerializeField] private int selectedFace;
    [SerializeField] private bool showAllMidpoints = true;

    public static void Open(BuildingTileDefinition target, TileFace face)
    {
        var window = GetWindow<BuildingTileSixFaceEditorWindow>("Tile Face Detail");
        window.minSize = new Vector2(720f, 520f);
        window.definition = target;
        window.selectedFace = Mathf.Max(0, Array.IndexOf(Faces, face));
        window.Show();
        window.Focus();
    }

    [MenuItem("Window/Pillars Above/Building Tile Six-Face Editor")]
    private static void OpenFromMenu()
    {
        var target = Selection.activeGameObject == null
            ? null
            : Selection.activeGameObject.GetComponentInParent<BuildingTileDefinition>();
        if (target == null) target = FindDefaultDefinition();
        Open(target, TileFace.PositiveX);
    }

    private void OnEnable()
    {
        if (definition == null) definition = FindDefaultDefinition();
    }

    private void OnSelectionChange()
    {
        if (Selection.activeGameObject == null) return;
        var selected = Selection.activeGameObject.GetComponentInParent<BuildingTileDefinition>();
        if (selected == null || selected == definition) return;
        definition = selected;
        Repaint();
    }

    private void OnGUI()
    {
        HandleKeyboardFaceSwitch();
        DrawHeader();
        if (definition == null)
        {
            EditorGUILayout.HelpBox("Select a Building Tile prefab root, then open this window again.", MessageType.Info);
            return;
        }

        var available = position.width - 166f;
        var canvasRect = new Rect(10f, 82f, Mathf.Max(420f, available - 20f), Mathf.Max(340f, position.height - 126f));
        var sidebarRect = new Rect(canvasRect.xMax + 10f, 82f, 146f, canvasRect.height);
        var geometry = CollectFaceGeometry(definition, Faces[selectedFace]);
        DrawFaceCanvas(canvasRect, geometry);
        DrawSidebar(sidebarRect, geometry);
        DrawFooter(canvasRect);
    }

    private void DrawHeader()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            definition = (BuildingTileDefinition)EditorGUILayout.ObjectField(definition, typeof(BuildingTileDefinition), true, GUILayout.MinWidth(240f));
            GUILayout.FlexibleSpace();
            showAllMidpoints = GUILayout.Toggle(showAllMidpoints, "显示边中点", EditorStyles.toolbarButton, GUILayout.Width(92f));
        }
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(
            definition == null ? "六面详情" : definition.name + " / " + FaceLabels[selectedFace] + " 面详情",
            EditorStyles.boldLabel);
        EditorGUILayout.LabelField("左键点击顶点或黄色中点添加标记；右键点击洋红标记删除。快捷键 1–6 切换面。", EditorStyles.miniLabel);
    }

    private void DrawFaceCanvas(Rect rect, FaceGeometry geometry)
    {
        var face = Faces[selectedFace];
        var logicallyOpen = (definition.GetOpenFaces(0) & face) != 0;
        EditorGUI.DrawRect(rect, logicallyOpen ? new Color(.06f, .16f, .1f) : new Color(.15f, .075f, .075f));
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        if (!geometry.HasBounds || geometry.Vertices.Count == 0)
        {
            GUI.Label(new Rect(rect.x + 18f, rect.y + 18f, rect.width - 36f, 40f), "当前面没有找到可读 Mesh 顶点。", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var profile = definition.MeshSeams.Find(face);
        var anchors = profile == null ? null : profile.Anchors;
        Handles.BeginGUI();
        Handles.color = MeshEdgeColor;
        for (var i = 0; i < geometry.Segments.Count; i++)
        {
            var a = Project(geometry.Segments[i].A, geometry.Bounds, face, rect);
            var b = Project(geometry.Segments[i].B, geometry.Bounds, face, rect);
            Handles.DrawAAPolyLine(1.25f, a, b);
        }

        if (anchors != null)
        {
            for (var i = 0; i < anchors.Count; i++)
            {
                var point = Project(anchors[i], geometry.Bounds, face, rect);
                var radius = ProjectedBindRadius(definition.MeshSeams.VertexBindRadius, geometry.Bounds, face, rect);
                Handles.color = new Color(.1f, 1f, .3f, .13f);
                Handles.DrawSolidDisc(point, Vector3.forward, radius);
            }
        }

        for (var i = 0; i < geometry.Vertices.Count; i++)
        {
            var vertex = geometry.Vertices[i];
            var bound = IsBound(vertex, anchors, definition.MeshSeams.VertexBindRadius);
            DrawDisc(Project(vertex, geometry.Bounds, face, rect), 4.5f, bound ? BoundVertexColor : VertexColor);
        }

        if (showAllMidpoints)
        {
            for (var i = 0; i < geometry.Segments.Count; i++)
            {
                var midpoint = (geometry.Segments[i].A + geometry.Segments[i].B) * .5f;
                DrawDiamond(Project(midpoint, geometry.Bounds, face, rect), 3.5f, MidpointColor);
            }
        }

        if (anchors != null)
        {
            for (var i = 0; i < anchors.Count; i++)
            {
                var point = Project(anchors[i], geometry.Bounds, face, rect);
                DrawDisc(point, 7f, MarkerColor);
                Handles.color = Color.white;
                Handles.Label(point + new Vector2(8f, -11f), i.ToString());
            }
        }
        Handles.EndGUI();

        HandleCanvasClick(rect, geometry, anchors);
    }

    private void HandleCanvasClick(Rect rect, FaceGeometry geometry, List<Vector3> anchors)
    {
        var current = Event.current;
        if (current.type != EventType.MouseDown || !rect.Contains(current.mousePosition)) return;
        if (current.button == 1 && anchors != null && anchors.Count > 0)
        {
            var best = NearestPointIndex(anchors, current.mousePosition, geometry.Bounds, Faces[selectedFace], rect, 13f);
            if (best >= 0)
            {
                Undo.RecordObject(definition, "Remove tile seam marker");
                anchors.RemoveAt(best);
                EditorUtility.SetDirty(definition);
                current.Use();
                Repaint();
            }
            return;
        }
        if (current.button != 0) return;

        var clicked = FindClickablePoint(current.mousePosition, geometry, rect, out var point);
        if (!clicked) return;
        AddMarker(point);
        current.Use();
        Repaint();
    }

    private bool FindClickablePoint(Vector2 mouse, FaceGeometry geometry, Rect rect, out Vector3 point)
    {
        point = Vector3.zero;
        var bestDistance = 11f * 11f;
        var found = false;
        for (var i = 0; i < geometry.Vertices.Count; i++)
        {
            var distance = (Project(geometry.Vertices[i], geometry.Bounds, Faces[selectedFace], rect) - mouse).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance; point = geometry.Vertices[i]; found = true;
        }
        if (!showAllMidpoints) return found;
        for (var i = 0; i < geometry.Segments.Count; i++)
        {
            var midpoint = (geometry.Segments[i].A + geometry.Segments[i].B) * .5f;
            var distance = (Project(midpoint, geometry.Bounds, Faces[selectedFace], rect) - mouse).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance; point = midpoint; found = true;
        }
        return found;
    }

    private void AddMarker(Vector3 point)
    {
        var face = Faces[selectedFace];
        var profile = definition.MeshSeams.Find(face);
        Undo.RecordObject(definition, "Add tile seam marker");
        if (profile == null)
        {
            profile = new BuildingTileSeamProfile(face, Array.Empty<Vector3>());
            definition.MeshSeams.Profiles.Add(profile);
        }
        for (var i = 0; i < profile.Anchors.Count; i++)
            if ((profile.Anchors[i] - point).sqrMagnitude < .0000001f) return;
        profile.Anchors.Add(point);
        EditorUtility.SetDirty(definition);
    }

    private void DrawSidebar(Rect rect, FaceGeometry geometry)
    {
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        GUI.Label(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 20f), "方向缩略图", EditorStyles.boldLabel);
        DrawFaceThumbnail(new Rect(rect.x + 8f, rect.y + 34f, rect.width - 16f, 150f));

        var face = Faces[selectedFace];
        var open = (definition.GetOpenFaces(0) & face) != 0;
        var profile = definition.MeshSeams.Find(face);
        var anchors = profile == null ? 0 : profile.Anchors.Count;
        var y = rect.y + 194f;
        GUI.Label(new Rect(rect.x + 8f, y, rect.width - 16f, 24f), "当前：" + FaceLabels[selectedFace], EditorStyles.largeLabel);
        y += 30f;
        DrawStatusRow(rect, ref y, open ? BoundVertexColor : new Color(1f, .35f, .3f), open ? "逻辑开口" : "逻辑封闭");
        DrawStatusRow(rect, ref y, BoundVertexColor, "已绑定顶点");
        DrawStatusRow(rect, ref y, VertexColor, "可选顶点 " + geometry.Vertices.Count);
        DrawStatusRow(rect, ref y, MidpointColor, "可选边中点");
        DrawStatusRow(rect, ref y, MarkerColor, "标记点 " + anchors);
    }

    private void DrawFaceThumbnail(Rect rect)
    {
        var size = 31f;
        var gap = 3f;
        var origin = new Vector2(rect.x + 6f, rect.y + 8f);
        var cells = new[]
        {
            new Vector2(2, 1), new Vector2(0, 1), new Vector2(1, 0),
            new Vector2(1, 2), new Vector2(3, 1), new Vector2(1, 1)
        };
        for (var i = 0; i < Faces.Length; i++)
        {
            var cell = new Rect(origin.x + cells[i].x * (size + gap), origin.y + cells[i].y * (size + gap), size, size);
            var old = GUI.backgroundColor;
            GUI.backgroundColor = i == selectedFace ? MarkerColor : new Color(.42f, .46f, .52f);
            if (GUI.Button(cell, FaceLabels[i], EditorStyles.miniButton))
            {
                selectedFace = i;
                GUI.FocusControl(null);
                Repaint();
            }
            GUI.backgroundColor = old;
        }
    }

    private static void DrawStatusRow(Rect rect, ref float y, Color color, string text)
    {
        EditorGUI.DrawRect(new Rect(rect.x + 10f, y + 3f, 11f, 11f), color);
        GUI.Label(new Rect(rect.x + 27f, y, rect.width - 35f, 18f), text, EditorStyles.miniLabel);
        y += 20f;
    }

    private static void DrawFooter(Rect canvasRect)
    {
        var y = canvasRect.yMax + 6f;
        GUI.Label(new Rect(canvasRect.x, y, canvasRect.width, 20f),
            "绿色范围＝与相邻单元格连接时会被形变的影响区；青色＝未绑定顶点；黄色＝边中点；洋红＝标记点。",
            EditorStyles.miniLabel);
    }

    private void HandleKeyboardFaceSwitch()
    {
        var current = Event.current;
        if (current.type != EventType.KeyDown) return;
        var index = current.keyCode >= KeyCode.Alpha1 && current.keyCode <= KeyCode.Alpha6
            ? current.keyCode - KeyCode.Alpha1
            : -1;
        if (index < 0) return;
        selectedFace = index;
        current.Use();
        Repaint();
    }

    private static FaceGeometry CollectFaceGeometry(BuildingTileDefinition target, TileFace face)
    {
        var result = new FaceGeometry();
        var allPoints = new List<Vector3>();
        var filters = target.GetComponentsInChildren<MeshFilter>(true);
        for (var i = 0; i < filters.Length; i++)
        {
            var mesh = filters[i].sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var matrix = target.transform.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
            var vertices = mesh.vertices;
            for (var v = 0; v < vertices.Length; v++) allPoints.Add(matrix.MultiplyPoint3x4(vertices[v]));
        }
        if (allPoints.Count == 0) return result;
        var rootBounds = new Bounds(allPoints[0], Vector3.zero);
        for (var i = 1; i < allPoints.Count; i++) rootBounds.Encapsulate(allPoints[i]);
        result.Bounds = rootBounds;
        result.HasBounds = true;
        var tolerance = Mathf.Max(.0001f, LargestExtent(rootBounds) * .035f);

        for (var f = 0; f < filters.Length; f++)
        {
            var mesh = filters[f].sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var matrix = target.transform.worldToLocalMatrix * filters[f].transform.localToWorldMatrix;
            var source = mesh.vertices;
            var rootVertices = new Vector3[source.Length];
            var onFace = new bool[source.Length];
            for (var v = 0; v < source.Length; v++)
            {
                rootVertices[v] = matrix.MultiplyPoint3x4(source[v]);
                onFace[v] = DistanceToFace(rootVertices[v], rootBounds, face) <= tolerance;
                if (onFace[v]) AddUnique(result.Vertices, rootVertices[v]);
            }
            var triangles = mesh.triangles;
            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                AddFaceSegment(result.Segments, rootVertices, onFace, triangles[t], triangles[t + 1]);
                AddFaceSegment(result.Segments, rootVertices, onFace, triangles[t + 1], triangles[t + 2]);
                AddFaceSegment(result.Segments, rootVertices, onFace, triangles[t + 2], triangles[t]);
            }
        }
        return result;
    }

    private static void AddFaceSegment(List<Segment> segments, Vector3[] vertices, bool[] onFace, int a, int b)
    {
        if (!onFace[a] || !onFace[b] || (vertices[a] - vertices[b]).sqrMagnitude < .0000001f) return;
        var midpoint = (vertices[a] + vertices[b]) * .5f;
        for (var i = 0; i < segments.Count; i++)
            if (((segments[i].A + segments[i].B) * .5f - midpoint).sqrMagnitude < .0000001f) return;
        segments.Add(new Segment { A = vertices[a], B = vertices[b] });
    }

    private static void AddUnique(List<Vector3> points, Vector3 point)
    {
        for (var i = 0; i < points.Count; i++)
            if ((points[i] - point).sqrMagnitude < .0000001f) return;
        points.Add(point);
    }

    private static Vector2 Project(Vector3 point, Bounds bounds, TileFace face, Rect rect)
    {
        GetAxes(face, out var horizontal, out var vertical);
        var h0 = Vector3.Dot(bounds.min, horizontal);
        var h1 = Vector3.Dot(bounds.max, horizontal);
        var v0 = Vector3.Dot(bounds.min, vertical);
        var v1 = Vector3.Dot(bounds.max, vertical);
        var minH = Mathf.Min(h0, h1);
        var maxH = Mathf.Max(h0, h1);
        var minV = Mathf.Min(v0, v1);
        var maxV = Mathf.Max(v0, v1);
        var u = Mathf.InverseLerp(minH, maxH, Vector3.Dot(point, horizontal));
        var v = Mathf.InverseLerp(minV, maxV, Vector3.Dot(point, vertical));
        return new Vector2(Mathf.Lerp(rect.x + 24f, rect.xMax - 24f, u), Mathf.Lerp(rect.yMax - 24f, rect.y + 24f, v));
    }

    private static float ProjectedBindRadius(float radius, Bounds bounds, TileFace face, Rect rect)
    {
        GetAxes(face, out var horizontal, out _);
        var span = Mathf.Max(.0001f, Vector3.Dot(bounds.max - bounds.min, new Vector3(Mathf.Abs(horizontal.x), Mathf.Abs(horizontal.y), Mathf.Abs(horizontal.z))));
        return Mathf.Clamp(radius / span * (rect.width - 48f), 5f, 42f);
    }

    private static void GetAxes(TileFace face, out Vector3 horizontal, out Vector3 vertical)
    {
        if (face == TileFace.PositiveX) { horizontal = Vector3.back; vertical = Vector3.up; return; }
        if (face == TileFace.NegativeX) { horizontal = Vector3.forward; vertical = Vector3.up; return; }
        if (face == TileFace.PositiveY) { horizontal = Vector3.right; vertical = Vector3.forward; return; }
        if (face == TileFace.NegativeY) { horizontal = Vector3.right; vertical = Vector3.back; return; }
        if (face == TileFace.PositiveZ) { horizontal = Vector3.right; vertical = Vector3.up; return; }
        horizontal = Vector3.left; vertical = Vector3.up;
    }

    private static float DistanceToFace(Vector3 point, Bounds bounds, TileFace face)
    {
        if (face == TileFace.PositiveX) return Mathf.Abs(bounds.max.x - point.x);
        if (face == TileFace.NegativeX) return Mathf.Abs(point.x - bounds.min.x);
        if (face == TileFace.PositiveY) return Mathf.Abs(bounds.max.y - point.y);
        if (face == TileFace.NegativeY) return Mathf.Abs(point.y - bounds.min.y);
        if (face == TileFace.PositiveZ) return Mathf.Abs(bounds.max.z - point.z);
        return Mathf.Abs(point.z - bounds.min.z);
    }

    private static float LargestExtent(Bounds bounds)
    {
        return Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
    }

    private static bool IsBound(Vector3 vertex, List<Vector3> anchors, float radius)
    {
        if (anchors == null) return false;
        var radiusSqr = radius * radius;
        for (var i = 0; i < anchors.Count; i++)
            if ((anchors[i] - vertex).sqrMagnitude <= radiusSqr) return true;
        return false;
    }

    private static int NearestPointIndex(List<Vector3> points, Vector2 mouse, Bounds bounds, TileFace face, Rect rect, float maxPixels)
    {
        var best = -1;
        var bestDistance = maxPixels * maxPixels;
        for (var i = 0; i < points.Count; i++)
        {
            var distance = (Project(points[i], bounds, face, rect) - mouse).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance; best = i;
        }
        return best;
    }

    private static void DrawDisc(Vector2 center, float radius, Color color)
    {
        Handles.color = color;
        Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, radius);
    }

    private static void DrawDiamond(Vector2 center, float radius, Color color)
    {
        Handles.color = color;
        Handles.DrawAAConvexPolygon(
            new Vector3(center.x, center.y - radius), new Vector3(center.x + radius, center.y),
            new Vector3(center.x, center.y + radius), new Vector3(center.x - radius, center.y));
    }

    private static BuildingTileDefinition FindDefaultDefinition()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BuildingTiles/Cube_Individual.prefab");
        if (prefab != null) return prefab.GetComponent<BuildingTileDefinition>();
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/BuildingTiles" });
        for (var i = 0; i < guids.Length; i++)
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (prefab != null && prefab.TryGetComponent(out BuildingTileDefinition found)) return found;
        }
        return null;
    }
}
