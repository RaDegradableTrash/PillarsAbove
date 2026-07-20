using System;
using System.Collections.Generic;
using System.IO;
using PillarsAbove;
using PillarsAbove.BuildingTiles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildingTilePrefabBuilder
{
    private const string SourcePath = "Assets/BuildingTiles.fbx";
    private const string OutputFolder = "Assets/Prefabs/BuildingTiles";
    private const string CatalogPath = "Assets/Resources/BuildingTileCatalog.asset";
    // Each tile is placed in a 1x1x1 cell at runtime scale 0.5 (PillarForgeEngine.BuildingTileVisualScale),
    // so a prefab whose root-space bounds are ~1.96 fills its cell as a cube with a thin seam gap.
    private const float NormalizedTileRootExtent = 1.96f;
    private const TileFace AllCubeFaces =
        TileFace.PositiveX | TileFace.NegativeX |
        TileFace.PositiveY | TileFace.NegativeY |
        TileFace.PositiveZ | TileFace.NegativeZ;

    [InitializeOnLoadMethod]
    private static void BuildCatalogWhenMissing()
    {
        if (AssetDatabase.LoadAssetAtPath<BuildingTileCatalog>(CatalogPath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/Seal_TopSingle.prefab") == null ||
            SourceModelNeedsReadableImport() ||
            BuildingTilePrefabRootsNeedRebuild() ||
            BuildingTilePrefabFaceDataNeedsRebuild() ||
            BuildingTilePrefabsNeedSquareNormalization())
        {
            EditorApplication.delayCall += Rebuild;
        }
        else
        {
            EditorApplication.delayCall += ValidatePresets;
        }
    }

    private static bool BuildingTilePrefabRootsNeedRebuild()
    {
        var sample = AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/Cube_X+-Y+-Z+.prefab");
        if (sample == null)
        {
            return true;
        }

        var transform = sample.transform;
        return transform.localRotation != Quaternion.identity ||
               transform.localScale != Vector3.one ||
               transform.parent != null;
    }

    private static bool BuildingTilePrefabsNeedSquareNormalization()
    {
        var cube = AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/Cube_X+.prefab");
        if (cube == null)
        {
            return false;
        }

        var instance = UnityEngine.Object.Instantiate(cube);
        try
        {
            var size = MeasureLocalExtent(instance);
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                return false;
            }

            var max = Mathf.Max(size.x, size.y, size.z);
            var min = Mathf.Min(size.x, size.y, size.z);
            // Already normalized prefabs read as a cube; legacy rectangular prefabs trigger a rebuild.
            return max > min * 1.05f;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static bool SourceModelNeedsReadableImport()
    {
        var importer = AssetImporter.GetAtPath(SourcePath) as ModelImporter;
        return importer != null && !importer.isReadable;
    }

    private static bool BuildingTilePrefabFaceDataNeedsRebuild()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { OutputFolder }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null || !prefab.name.StartsWith("Cube_", StringComparison.Ordinal))
            {
                continue;
            }

            var definition = prefab.GetComponent<BuildingTileDefinition>();
            if (definition == null)
            {
                return true;
            }

            if (definition.CanonicalOpenFaces != ParseCubeFaces(prefab.name))
            {
                return true;
            }

            var expectedOverride = RootIdentityOpenFacesOverride(prefab.name);
            if (definition.RootIdentityOpenFacesOverride != expectedOverride)
            {
                return true;
            }
        }

        return false;
    }

    [MenuItem("Tools/Pillars Above/Rebuild Building Tile Prefabs %#b")]
    public static void Rebuild()
    {
        EnsureSourceModelReadable();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (model == null) throw new InvalidOperationException("Missing " + SourcePath);

        EnsureFolder("Assets/Prefabs");
        EnsureFolder(OutputFolder);
        EnsureFolder("Assets/Resources");

        foreach (var oldGuid in AssetDatabase.FindAssets("t:Prefab", new[] { OutputFolder }))
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(oldGuid));
        }

        var modelInstance = UnityEngine.Object.Instantiate(model);
        var sources = modelInstance.GetComponentsInChildren<Transform>(true);
        var prefabs = new List<GameObject>();
        // Derive a single per-axis root-space factor from the cube mesh and apply it to every tile so
        // cubes become unit cells (1:1:1) while seals scale by the same factor and keep their design size.
        var cubeRootFactor = ComputeCubeRootFactor(modelInstance);
        try
        {
            foreach (var source in sources)
            {
                if (!source.name.StartsWith("Cube_", StringComparison.Ordinal) &&
                    !source.name.StartsWith("Seal_", StringComparison.Ordinal)) continue;

                var tile = new GameObject(NormalizeName(source.name));
                var visual = UnityEngine.Object.Instantiate(source.gameObject);
                visual.name = "Visual";
                visual.transform.SetParent(tile.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = source.localRotation;
                visual.transform.localScale = source.localScale;
                tile.transform.position = Vector3.zero;
                tile.transform.rotation = Quaternion.identity;
                tile.transform.localScale = Vector3.one;
                ApplyRootScaleToLocal(visual, cubeRootFactor);
                ConfigureDefinition(tile);
                AddMeshColliders(tile);

                var path = OutputFolder + "/" + tile.name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(tile, path);
                prefabs.Add(prefab);
                UnityEngine.Object.DestroyImmediate(tile);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(modelInstance);
        }

        prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        var catalog = AssetDatabase.LoadAssetAtPath<BuildingTileCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<BuildingTileCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.ReplaceContents(prefabs);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Built " + prefabs.Count + " independent BuildingTiles prefabs and refreshed " + CatalogPath);
        BuildingTileCatalogWindow.Open();
        EditorApplication.delayCall += ValidatePresets;
    }

    private static void EnsureSourceModelReadable()
    {
        var importer = AssetImporter.GetAtPath(SourcePath) as ModelImporter;
        if (importer == null || importer.isReadable)
        {
            return;
        }

        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    [MenuItem("Tools/Pillars Above/Validate Building Tile Presets")]
    public static void ValidatePresets()
    {
        CleanupLeakedValidationTiles();
        HideSceneSourceBuildingTiles();
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { OutputFolder });
        var names = new HashSet<string>();
        var cubeCount = 0;
        var topCount = 0;
        var bottomCount = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var definition = prefab == null ? null : prefab.GetComponent<BuildingTileDefinition>();
            if (definition == null) throw new InvalidOperationException(path + " has no BuildingTileDefinition");
            if (!names.Add(prefab.name)) throw new InvalidOperationException("Duplicate tile name " + prefab.name);
            if (definition.Layer == TileLayer.Cube) cubeCount++;
            else if (definition.Layer == TileLayer.SealTop) topCount++;
            else bottomCount++;
        }
        if (cubeCount != 26 || topCount != 5 || bottomCount != 5)
            throw new InvalidOperationException($"Expected 26 Cube, 5 Top and 5 Bottom prefabs; got {cubeCount}, {topCount}, {bottomCount}.");
        ValidateCubeShellCoverage();
        ValidateSealCornerCoverage();
        var placementResult = ValidateRuntimePlacement();
        Debug.Log($"Building tile presets valid: {cubeCount} Cube, {topCount} Seal_Top, {bottomCount} Seal_Bottom. Cube prefabs use 24 axis-aligned rotations; open faces connect only to open faces; closed faces connect only to closed faces; Seal edge corner bits must match. {placementResult}");
    }

    private static void ValidateCubeShellCoverage()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<BuildingTileCatalog>(CatalogPath);
        var variants = new HashSet<TileFace>();
        foreach (var definition in catalog.Definitions(TileLayer.Cube))
        {
            for (var rotation = 0; rotation < definition.RotationCount; rotation++)
            {
                variants.Add(definition.GetOpenFaces(rotation));
            }
        }

        var requiredCount = 0;
        for (var x = 0; x < 3; x++)
        for (var y = 0; y < 3; y++)
        for (var z = 0; z < 3; z++)
        {
            if (x == 1 && y == 1 && z == 1)
            {
                continue;
            }

            var exteriorClosed = TileFace.None;
            if (x < 1) exteriorClosed |= TileFace.NegativeX;
            else if (x > 1) exteriorClosed |= TileFace.PositiveX;
            if (y < 1) exteriorClosed |= TileFace.NegativeY;
            else if (y > 1) exteriorClosed |= TileFace.PositiveY;
            if (z < 1) exteriorClosed |= TileFace.NegativeZ;
            else if (z > 1) exteriorClosed |= TileFace.PositiveZ;

            var required = (TileFace.PositiveX | TileFace.NegativeX |
                            TileFace.PositiveY | TileFace.NegativeY |
                            TileFace.PositiveZ | TileFace.NegativeZ) & ~exteriorClosed;
            if (!variants.Contains(required))
            {
                throw new InvalidOperationException("No Cube prefab rotation covers shell opening mask " + required + ".");
            }

            requiredCount++;
        }

        if (requiredCount != 26)
            throw new InvalidOperationException("Expected 26 shell masks, got " + requiredCount + ".");
    }

    private static void ValidateSealCornerCoverage()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<BuildingTileCatalog>(CatalogPath);
        ValidateSealLayerCoverage(catalog, TileLayer.SealTop);
        ValidateSealLayerCoverage(catalog, TileLayer.SealBottom);
    }

    private static void ValidateSealLayerCoverage(BuildingTileCatalog catalog, TileLayer layer)
    {
        var variants = new HashSet<SealCorner>();
        foreach (var definition in catalog.Definitions(layer))
        {
            for (var rotation = 0; rotation < definition.RotationCount; rotation++)
            {
                variants.Add(definition.GetSealCorners(rotation));
            }
        }

        for (var mask = 1; mask <= (int)SealCorner.All; mask++)
        {
            var required = (SealCorner)mask;
            if (!variants.Contains(required))
            {
                throw new InvalidOperationException(layer + " has no prefab rotation for corner mask " + required + ".");
            }
        }
    }


    private static string ValidateRuntimePlacement()
    {
        var host = new GameObject("Building placement validation host") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var engine = host.AddComponent<PillarForgeEngine>();
            return engine.RunPlacementValidation();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CleanupLeakedValidationTiles()
    {
        foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
        {
            if (!IsGeneratedRuntimeTileName(go.name) && go.name != "Validation Building Tile Prefabs")
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static void HideSceneSourceBuildingTiles()
    {
        foreach (var transform in UnityEngine.Object.FindObjectsOfType<Transform>(true))
        {
            if (transform.name != "BuildingTiles" || transform.gameObject.scene.name == null)
            {
                continue;
            }

            var path = AssetDatabase.GetAssetPath(transform.gameObject);
            if (!string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (!transform.gameObject.activeSelf)
            {
                continue;
            }

            transform.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(transform.gameObject.scene);
        }
    }

    private static bool IsGeneratedRuntimeTileName(string name)
    {
        if (name.StartsWith("Support_Cube_", StringComparison.Ordinal))
        {
            return true;
        }

        if (!name.StartsWith("Cube_", StringComparison.Ordinal) && !name.StartsWith("Seal_", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = name.Split('_');
        if (parts.Length < 4)
        {
            return false;
        }

        int value;
        return int.TryParse(parts[parts.Length - 1], out value) &&
               int.TryParse(parts[parts.Length - 2], out value) &&
               int.TryParse(parts[parts.Length - 3], out value);
    }

    private static void ConfigureDefinition(GameObject tile)
    {
        var definition = tile.GetComponent<BuildingTileDefinition>() ?? tile.AddComponent<BuildingTileDefinition>();
        if (tile.name.StartsWith("Cube_", StringComparison.Ordinal))
        {
            var canonicalOpenFaces = ParseCubeFaces(tile.name);
            var rootIdentityOpenFaces = RootIdentityOpenFacesOverride(tile.name);
            definition.Configure(TileLayer.Cube, canonicalOpenFaces, SealCorner.None, 10, rootIdentityOpenFaces);
            return;
        }

        var layer = tile.name.StartsWith("Seal_Top", StringComparison.Ordinal) ? TileLayer.SealTop : TileLayer.SealBottom;
        definition.Configure(layer, TileFace.None, ParseSealCorners(tile.name));
    }

    private static string NormalizeName(string sourceName)
    {
        return sourceName == "Seal_TopSingle.001" ? "Seal_TopSingle" : sourceName;
    }

    private static TileFace ParseCubeFaces(string name)
    {
        if (name == "Cube_Individual") return TileFace.None;
        var suffix = name.Substring("Cube_".Length);
        var result = TileFace.None;
        for (var i = 0; i < suffix.Length; i++)
        {
            var axis = suffix[i];
            if (axis != 'X' && axis != 'Y' && axis != 'Z') continue;
            if (i + 1 >= suffix.Length || suffix[i + 1] != '+')
                throw new InvalidOperationException("Invalid positive-first tile name: " + name);
            result |= Face(axis, true);
            i++;
            if (i + 1 < suffix.Length && suffix[i + 1] == '-')
            {
                result |= Face(axis, false);
                i++;
            }
        }
        return result;
    }

    private static TileFace Face(char axis, bool positive)
    {
        if (axis == 'X') return positive ? TileFace.PositiveX : TileFace.NegativeX;
        if (axis == 'Y') return positive ? TileFace.PositiveY : TileFace.NegativeY;
        return positive ? TileFace.PositiveZ : TileFace.NegativeZ;
    }

    private static TileFace RootIdentityOpenFacesOverride(string tileName)
    {
        return tileName == "Cube_X+Y+Z+"
            ? TileFace.NegativeX | TileFace.PositiveY | TileFace.NegativeZ
            : TileFace.None;
    }

    private static TileFace DetectRootIdentityOpenFaces(GameObject tile, TileFace canonicalOpenFaces)
    {
        var filters = tile.GetComponentsInChildren<MeshFilter>(true);
        var transformedVertices = new List<Vector3>();
        var filterMatrices = new Dictionary<MeshFilter, Matrix4x4>();
        for (var filterIndex = 0; filterIndex < filters.Length; filterIndex++)
        {
            var filter = filters[filterIndex];
            if (filter.sharedMesh == null)
            {
                continue;
            }

            var matrix = tile.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            filterMatrices[filter] = matrix;
            var vertices = filter.sharedMesh.vertices;
            for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                transformedVertices.Add(matrix.MultiplyPoint3x4(vertices[vertexIndex]));
            }
        }

        if (transformedVertices.Count == 0)
        {
            return canonicalOpenFaces;
        }

        var bounds = new Bounds(transformedVertices[0], Vector3.zero);
        for (var i = 1; i < transformedVertices.Count; i++)
        {
            bounds.Encapsulate(transformedVertices[i]);
        }

        var scores = new Dictionary<TileFace, float>
        {
            { TileFace.PositiveX, 0f }, { TileFace.NegativeX, 0f },
            { TileFace.PositiveY, 0f }, { TileFace.NegativeY, 0f },
            { TileFace.PositiveZ, 0f }, { TileFace.NegativeZ, 0f }
        };
        foreach (var pair in filterMatrices)
        {
            var mesh = pair.Key.sharedMesh;
            var matrix = pair.Value;
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            for (var triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
            {
                var a = matrix.MultiplyPoint3x4(vertices[triangles[triangleIndex]]);
                var b = matrix.MultiplyPoint3x4(vertices[triangles[triangleIndex + 1]]);
                var c = matrix.MultiplyPoint3x4(vertices[triangles[triangleIndex + 2]]);
                var areaVector = Vector3.Cross(b - a, c - a) * 0.5f;
                var centroid = (a + b + c) / 3f;
                AccumulateFaceScore(scores, TileFace.PositiveX, centroid.x, bounds.center.x, bounds.extents.x, Mathf.Abs(areaVector.x), true);
                AccumulateFaceScore(scores, TileFace.NegativeX, centroid.x, bounds.center.x, bounds.extents.x, Mathf.Abs(areaVector.x), false);
                AccumulateFaceScore(scores, TileFace.PositiveY, centroid.y, bounds.center.y, bounds.extents.y, Mathf.Abs(areaVector.y), true);
                AccumulateFaceScore(scores, TileFace.NegativeY, centroid.y, bounds.center.y, bounds.extents.y, Mathf.Abs(areaVector.y), false);
                AccumulateFaceScore(scores, TileFace.PositiveZ, centroid.z, bounds.center.z, bounds.extents.z, Mathf.Abs(areaVector.z), true);
                AccumulateFaceScore(scores, TileFace.NegativeZ, centroid.z, bounds.center.z, bounds.extents.z, Mathf.Abs(areaVector.z), false);
            }
        }

        var rankedFaces = new List<KeyValuePair<TileFace, float>>(scores);
        rankedFaces.Sort((a, b) => b.Value.CompareTo(a.Value));
        var closedFaceCount = 6 - CountFaces(canonicalOpenFaces);
        var closedFaces = TileFace.None;
        for (var i = 0; i < closedFaceCount && i < rankedFaces.Count; i++)
        {
            closedFaces |= rankedFaces[i].Key;
        }

        return AllCubeFaces & ~closedFaces;
    }

    private static void AccumulateFaceScore(
        Dictionary<TileFace, float> scores,
        TileFace face,
        float coordinate,
        float center,
        float extent,
        float projectedArea,
        bool positive)
    {
        if (extent <= 0.0001f || projectedArea <= 0.000001f)
        {
            return;
        }

        var outerThreshold = extent * 0.35f;
        if (positive ? coordinate >= center + outerThreshold : coordinate <= center - outerThreshold)
        {
            scores[face] += projectedArea;
        }
    }

    private static int CountFaces(TileFace faces)
    {
        var count = 0;
        if ((faces & TileFace.PositiveX) != 0) count++;
        if ((faces & TileFace.NegativeX) != 0) count++;
        if ((faces & TileFace.PositiveY) != 0) count++;
        if ((faces & TileFace.NegativeY) != 0) count++;
        if ((faces & TileFace.PositiveZ) != 0) count++;
        if ((faces & TileFace.NegativeZ) != 0) count++;
        return count;
    }

    private static SealCorner ParseSealCorners(string name)
    {
        // Canonical orientation follows the FBX's +X/+Z corner. Quarter turns generate every orientation.
        if (name.Contains("Quadric")) return SealCorner.All;
        if (name.Contains("Triple")) return SealCorner.PositiveXPositiveZ | SealCorner.PositiveXNegativeZ | SealCorner.NegativeXPositiveZ;
        if (name.Contains("Diagonal")) return SealCorner.PositiveXPositiveZ | SealCorner.NegativeXNegativeZ;
        if (name.Contains("Double")) return SealCorner.PositiveXPositiveZ | SealCorner.PositiveXNegativeZ;
        if (name.Contains("Single")) return SealCorner.PositiveXPositiveZ;
        throw new InvalidOperationException("Unknown seal preset " + name);
    }

    private static void AddMeshColliders(GameObject root)
    {
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
            var collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
        }
    }

    private static Vector3 ComputeCubeRootFactor(GameObject modelInstance)
    {
        foreach (var source in modelInstance.GetComponentsInChildren<Transform>(true))
        {
            if (!source.name.StartsWith("Cube_", StringComparison.Ordinal))
            {
                continue;
            }

            var size = MeasureVisualExtentFromSource(source);
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                break;
            }

            return new Vector3(
                NormalizedTileRootExtent / size.x,
                NormalizedTileRootExtent / size.y,
                NormalizedTileRootExtent / size.z);
        }

        return Vector3.one;
    }

    // Extent the source mesh will occupy once parented as a Visual with the FBX axis/scale conversion.
    private static Vector3 MeasureVisualExtentFromSource(Transform source)
    {
        var visualLocal = Matrix4x4.TRS(Vector3.zero, source.localRotation, source.localScale);
        return MeasureFiltersExtent(source, filter => visualLocal * (source.worldToLocalMatrix * filter.transform.localToWorldMatrix));
    }

    // Extent of all descendant meshes expressed in root local space.
    private static Vector3 MeasureLocalExtent(GameObject root)
    {
        var rootToLocal = root.transform.worldToLocalMatrix;
        return MeasureFiltersExtent(root.transform, filter => rootToLocal * filter.transform.localToWorldMatrix);
    }

    private static Vector3 MeasureFiltersExtent(Component root, Func<MeshFilter, Matrix4x4> matrixFor)
    {
        var first = true;
        var min = Vector3.zero;
        var max = Vector3.zero;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            var matrix = matrixFor(filter);
            var vertices = filter.sharedMesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
            {
                var point = matrix.MultiplyPoint3x4(vertices[i]);
                if (first)
                {
                    min = point;
                    max = point;
                    first = false;
                }
                else
                {
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
            }
        }

        return first ? Vector3.zero : max - min;
    }

    // Folds a desired root-space per-axis scale into the Visual's localScale. The Visual carries the
    // FBX axis-conversion rotation, so a root axis maps to a (possibly different) local axis; for the
    // 90-degree import rotation this is a clean per-axis swap and keeps "Visual" a direct child.
    private static void ApplyRootScaleToLocal(GameObject visual, Vector3 rootScale)
    {
        if (rootScale == Vector3.one)
        {
            return;
        }

        var rotation = visual.transform.localRotation;
        var localMultiplier = new Vector3(
            RootAxisScale(rotation * Vector3.right, rootScale),
            RootAxisScale(rotation * Vector3.up, rootScale),
            RootAxisScale(rotation * Vector3.forward, rootScale));
        visual.transform.localScale = Vector3.Scale(visual.transform.localScale, localMultiplier);
    }

    private static float RootAxisScale(Vector3 rotatedAxis, Vector3 rootScale)
    {
        var x = Mathf.Abs(rotatedAxis.x);
        var y = Mathf.Abs(rotatedAxis.y);
        var z = Mathf.Abs(rotatedAxis.z);
        if (x >= y && x >= z) return rootScale.x;
        if (y >= z) return rootScale.y;
        return rootScale.z;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}

public sealed class BuildingTileCatalogWindow : EditorWindow
{
    private Vector2 scroll;

    [MenuItem("Window/Pillars Above/Building Tile Catalog Inspector")]
    public static void Open()
    {
        var window = GetWindow<BuildingTileCatalogWindow>("Building Tile Catalog");
        window.minSize = new Vector2(760f, 500f);
        window.Show();
    }

    private void OnGUI()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<BuildingTileCatalog>("Assets/Resources/BuildingTileCatalog.asset");
        if (catalog == null)
        {
            EditorGUILayout.HelpBox("Run Tools > Pillars Above > Rebuild Building Tile Prefabs first.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(catalog.Prefabs.Count + " independent prefabs - click any preview to select it", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        const float cardWidth = 180f;
        var columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 20f) / cardWidth));
        var index = 0;
        while (index < catalog.Prefabs.Count)
        {
            EditorGUILayout.BeginHorizontal();
            for (var column = 0; column < columns && index < catalog.Prefabs.Count; column++, index++)
            {
                var prefab = catalog.Prefabs[index];
                var definition = prefab.GetComponent<BuildingTileDefinition>();
                EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(cardWidth - 8f));
                var preview = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
                if (GUILayout.Button(preview, GUILayout.Width(cardWidth - 20f), GUILayout.Height(110f))) Selection.activeObject = prefab;
                EditorGUILayout.LabelField(prefab.name, EditorStyles.boldLabel, GUILayout.Width(cardWidth - 16f));
                var preset = definition.Layer == TileLayer.Cube
                    ? "Open: " + definition.CanonicalOpenFaces
                    : "Corners: " + definition.CanonicalSealCorners;
                EditorGUILayout.LabelField(definition.Layer + " | " + preset, EditorStyles.miniLabel, GUILayout.Width(cardWidth - 16f));
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        Repaint();
    }
}
