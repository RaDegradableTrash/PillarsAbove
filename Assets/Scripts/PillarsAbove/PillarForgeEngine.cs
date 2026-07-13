using System;
using System.Collections.Generic;
using UnityEngine;

namespace PillarsAbove
{
    public sealed class PillarForgeEngine : MonoBehaviour
    {
        private enum ToolMode
        {
            Carve,
            Build,
            Restore
        }

        private const int GridWidth = 25;
        private const int GridHeight = 60;
        private const int GridDepth = 25;
        private const float CellSize = 1f;

        private readonly bool[,,] stone = new bool[GridWidth, GridHeight, GridDepth];
        private readonly HashSet<Vector3Int> buildings = new HashSet<Vector3Int>();
        private readonly List<Vector3> vertices = new List<Vector3>(32768);
        private readonly List<int> stoneTriangles = new List<int>(65536);
        private readonly List<int> sunStoneTriangles = new List<int>(32768);
        private readonly List<int> buildingTriangles = new List<int>(16384);
        private readonly List<Vector3> normals = new List<Vector3>(32768);
        private readonly Dictionary<int, Vector3Int> faceToCell = new Dictionary<int, Vector3Int>();
        private readonly List<GameObject> buildPreviewCells = new List<GameObject>(24);

        private Mesh pillarMesh;
        private MeshFilter pillarFilter;
        private MeshCollider pillarCollider;
        private MeshRenderer pillarRenderer;
        private Camera sceneCamera;
        private Material stoneMaterial;
        private Material sunStoneMaterial;
        private Material buildingMaterial;
        private Material oceanMaterial;
        private Material previewMaterial;
        private ToolMode toolMode = ToolMode.Carve;
        private Vector2 orbitAngles = new Vector2(42f, 24f);
        private float orbitDistance = 72f;
        private float targetHeight = 30f;
        private Vector3Int hoveredCell = new Vector3Int(-1, -1, -1);
        private Vector3 hoveredNormal = Vector3.zero;
        private GameObject cursor;
        private int carvedCells;
        private int buildingCells;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<PillarForgeEngine>() != null)
            {
                return;
            }

            var host = new GameObject("Pillars Above Engine");
            host.AddComponent<PillarForgeEngine>();
        }

        private void Awake()
        {
            ConfigureCamera();
            ConfigureLighting();
            CreateMaterials();
            CreatePillarObjects();
            CreateOcean();
            SeedPillar();
            RebuildMesh();
            CreateCursor();
            CreateBuildPreviewPool();
        }

        private void Update()
        {
            HandleCameraInput();
            UpdateCamera();
            UpdateHover();
            HandleToolInput();
            AnimateOcean();
            UpdateCursor();
        }

        private void OnGUI()
        {
            const int size = 42;
            GUI.Box(new Rect(14, 14, 214, 182), GUIContent.none);
            DrawToolButton(new Rect(24, 24, size, size), ToolMode.Carve, "-");
            DrawToolButton(new Rect(72, 24, size, size), ToolMode.Build, "+");
            DrawToolButton(new Rect(120, 24, size, size), ToolMode.Restore, "o");

            GUI.Label(new Rect(24, 76, 168, 24), "stone: " + CountStoneCells());
            GUI.Label(new Rect(24, 100, 168, 24), "carved: " + carvedCells);
            GUI.Label(new Rect(24, 124, 168, 24), "built: " + buildingCells);
            GUI.Label(new Rect(24, 148, 190, 24), "height: " + GridHeight);
        }

        private void DrawToolButton(Rect rect, ToolMode mode, string label)
        {
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = toolMode == mode ? new Color(0.86f, 0.72f, 0.42f) : Color.white;
            if (GUI.Button(rect, label))
            {
                toolMode = mode;
            }
            GUI.backgroundColor = previous;
        }

        private void ConfigureCamera()
        {
            sceneCamera = Camera.main;
            if (sceneCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                sceneCamera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<AudioListener>();
            }

            sceneCamera.clearFlags = CameraClearFlags.Skybox;
            sceneCamera.fieldOfView = 38f;
            sceneCamera.nearClipPlane = 0.1f;
            sceneCamera.farClipPlane = 500f;
            sceneCamera.backgroundColor = new Color(0.58f, 0.66f, 0.68f);
        }

        private void ConfigureLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.66f, 0.56f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.35f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.19f, 0.18f, 0.16f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.55f, 0.61f, 0.61f);
            RenderSettings.fogStartDistance = 80f;
            RenderSettings.fogEndDistance = 250f;

            var existing = FindObjectOfType<Light>();
            if (existing == null)
            {
                var lightObject = new GameObject("Sun");
                existing = lightObject.AddComponent<Light>();
            }

            existing.type = LightType.Directional;
            existing.intensity = 1.35f;
            existing.color = new Color(1f, 0.82f, 0.58f);
            existing.transform.rotation = Quaternion.Euler(44f, -38f, 0f);
            existing.shadows = LightShadows.Soft;

            var fillObject = new GameObject("Soft Blue Fill");
            fillObject.transform.SetParent(transform);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.22f;
            fill.color = new Color(0.46f, 0.58f, 0.70f);
            fill.transform.rotation = Quaternion.Euler(32f, 140f, 0f);
        }

        private void CreateMaterials()
        {
            stoneMaterial = new Material(Shader.Find("Standard"));
            stoneMaterial.name = "Runtime Stratified Stone";
            stoneMaterial.color = new Color(0.43f, 0.38f, 0.30f);
            stoneMaterial.SetFloat("_Glossiness", 0.08f);

            sunStoneMaterial = new Material(Shader.Find("Standard"));
            sunStoneMaterial.name = "Runtime Sunlit Ochre Stone";
            sunStoneMaterial.color = new Color(0.62f, 0.49f, 0.34f);
            sunStoneMaterial.SetFloat("_Glossiness", 0.10f);

            buildingMaterial = new Material(Shader.Find("Standard"));
            buildingMaterial.name = "Runtime Cliff Buildings";
            buildingMaterial.color = new Color(0.88f, 0.68f, 0.42f);
            buildingMaterial.SetFloat("_Glossiness", 0.18f);

            oceanMaterial = new Material(Shader.Find("Standard"));
            oceanMaterial.name = "Runtime Vast Green Sea";
            oceanMaterial.color = new Color(0.02f, 0.58f, 0.66f);
            oceanMaterial.SetFloat("_Glossiness", 0.92f);
            oceanMaterial.SetFloat("_Metallic", 0.02f);

            previewMaterial = new Material(Shader.Find("Standard"));
            previewMaterial.name = "Runtime Cell Cursor";
            previewMaterial.color = new Color(0.96f, 0.82f, 0.38f, 0.42f);
            previewMaterial.SetFloat("_Mode", 3f);
            previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            previewMaterial.SetInt("_ZWrite", 0);
            previewMaterial.DisableKeyword("_ALPHATEST_ON");
            previewMaterial.EnableKeyword("_ALPHABLEND_ON");
            previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            previewMaterial.renderQueue = 3000;
        }

        private void CreatePillarObjects()
        {
            var pillar = new GameObject("Voxelized Megalith Pillar");
            pillar.transform.SetParent(transform);
            pillarFilter = pillar.AddComponent<MeshFilter>();
            pillarRenderer = pillar.AddComponent<MeshRenderer>();
            pillarCollider = pillar.AddComponent<MeshCollider>();
            pillarMesh = new Mesh { name = "Runtime Pillar Mesh" };
            pillarMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            pillarFilter.sharedMesh = pillarMesh;
            pillarRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            pillarRenderer.receiveShadows = true;
            pillarRenderer.sharedMaterials = new[] { stoneMaterial, sunStoneMaterial, buildingMaterial };
        }

        private void CreateOcean()
        {
            var ocean = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ocean.name = "Open Sea";
            ocean.transform.SetParent(transform);
            ocean.transform.position = new Vector3(0f, -0.16f, 0f);
            ocean.transform.localScale = new Vector3(140f, 1f, 140f);
            var renderer = ocean.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = oceanMaterial;
            renderer.receiveShadows = true;

            var horizon = GameObject.CreatePrimitive(PrimitiveType.Plane);
            horizon.name = "Distant Pale Sea Shelf";
            horizon.transform.SetParent(transform);
            horizon.transform.position = new Vector3(0f, -0.18f, 0f);
            horizon.transform.localScale = new Vector3(360f, 1f, 360f);
            var horizonRenderer = horizon.GetComponent<MeshRenderer>();
            var horizonMaterial = new Material(Shader.Find("Standard"));
            horizonMaterial.name = "Runtime Hazy Horizon Water";
            horizonMaterial.color = new Color(0.18f, 0.66f, 0.70f);
            horizonMaterial.SetFloat("_Glossiness", 0.70f);
            horizonRenderer.sharedMaterial = horizonMaterial;

            CreateWaterGleam("Water Gleam A", new Vector3(-34f, -0.11f, 24f), new Vector3(9f, 0.018f, 0.18f), 18f);
            CreateWaterGleam("Water Gleam B", new Vector3(28f, -0.105f, -18f), new Vector3(7f, 0.018f, 0.14f), -24f);
            CreateWaterGleam("Water Gleam C", new Vector3(6f, -0.10f, 42f), new Vector3(11f, 0.018f, 0.16f), 6f);
        }

        private void CreateWaterGleam(string name, Vector3 position, Vector3 scale, float yaw)
        {
            var gleam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gleam.name = name;
            gleam.transform.SetParent(transform);
            gleam.transform.position = position;
            gleam.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            gleam.transform.localScale = scale;

            var gleamMaterial = new Material(Shader.Find("Standard"));
            gleamMaterial.name = name + " Material";
            gleamMaterial.color = new Color(0.62f, 0.92f, 0.86f);
            gleamMaterial.SetFloat("_Glossiness", 0.96f);
            gleam.GetComponent<MeshRenderer>().sharedMaterial = gleamMaterial;
            Destroy(gleam.GetComponent<BoxCollider>());
        }

        private void CreateCursor()
        {
            cursor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cursor.name = "Grid Cursor";
            cursor.transform.localScale = Vector3.one * 0.92f;
            cursor.GetComponent<MeshRenderer>().sharedMaterial = previewMaterial;
            Destroy(cursor.GetComponent<BoxCollider>());
            cursor.SetActive(false);
        }

        private void CreateBuildPreviewPool()
        {
            for (var i = 0; i < 18; i++)
            {
                var preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                preview.name = "Build Preview Cell " + i;
                preview.transform.SetParent(transform);
                preview.transform.localScale = Vector3.one * 0.86f;
                preview.GetComponent<MeshRenderer>().sharedMaterial = previewMaterial;
                Destroy(preview.GetComponent<BoxCollider>());
                preview.SetActive(false);
                buildPreviewCells.Add(preview);
            }
        }

        private void SeedPillar()
        {
            var center = new Vector2((GridWidth - 1) * 0.5f, (GridDepth - 1) * 0.5f);
            const float radius = 10.6f;

            for (var x = 0; x < GridWidth; x++)
            {
                for (var z = 0; z < GridDepth; z++)
                {
                    var delta = new Vector2(x, z) - center;
                    var noise = Mathf.Sin(x * 1.7f + z * 0.37f) * 0.5f + Mathf.Cos(z * 1.13f) * 0.35f;
                    var localRadius = radius + noise;
                    var inside = delta.magnitude <= localRadius;

                    for (var y = 0; y < GridHeight; y++)
                    {
                        if (!inside)
                        {
                            continue;
                        }

                        var taper = Mathf.InverseLerp(GridHeight, 0f, y) * 1.2f;
                        stone[x, y, z] = delta.magnitude <= localRadius - Mathf.Max(0f, y - 28f) * 0.08f + taper;
                    }
                }
            }

            CarveCavity(new Vector3Int(12, 9, 2), 2);
            CarveCavity(new Vector3Int(4, 18, 12), 2);
            CarveCavity(new Vector3Int(19, 26, 12), 2);
        }

        private void HandleCameraInput()
        {
            var cameraGesture = Input.GetMouseButton(1) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (cameraGesture)
            {
                orbitAngles.x += Input.GetAxis("Mouse X") * 4.2f;
                targetHeight -= Input.GetAxis("Mouse Y") * 0.42f;
            }

            orbitDistance = Mathf.Clamp(orbitDistance - Input.mouseScrollDelta.y * 3.4f, 24f, 110f);

            if (Input.GetKey(KeyCode.Q))
            {
                targetHeight -= Time.deltaTime * 12f;
            }
            if (Input.GetKey(KeyCode.E))
            {
                targetHeight += Time.deltaTime * 12f;
            }
            targetHeight = Mathf.Clamp(targetHeight, 3f, GridHeight - 3f);

            if (Input.GetKeyDown(KeyCode.Alpha1)) toolMode = ToolMode.Carve;
            if (Input.GetKeyDown(KeyCode.Alpha2)) toolMode = ToolMode.Build;
            if (Input.GetKeyDown(KeyCode.Alpha3)) toolMode = ToolMode.Restore;
        }

        private void UpdateCamera()
        {
            var target = new Vector3(0f, targetHeight, 0f);
            var rotation = Quaternion.Euler(orbitAngles.y, orbitAngles.x, 0f);
            sceneCamera.transform.position = target + rotation * new Vector3(0f, 0f, -orbitDistance);
            sceneCamera.transform.LookAt(target);
        }

        private void UpdateHover()
        {
            hoveredCell = new Vector3Int(-1, -1, -1);
            hoveredNormal = Vector3.zero;

            if (GUIUtility.hotControl != 0 || Input.mousePosition.x < 244f && Screen.height - Input.mousePosition.y < 210f)
            {
                return;
            }

            var ray = sceneCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (!pillarCollider.Raycast(ray, out hit, 500f))
            {
                return;
            }

            hoveredNormal = hit.normal;
            hoveredCell = WorldToCell(hit.point - hit.normal * 0.025f);
        }

        private void HandleToolInput()
        {
            if (!Input.GetMouseButtonDown(0) || !IsInside(hoveredCell))
            {
                return;
            }

            if (toolMode == ToolMode.Carve)
            {
                CarveCavity(hoveredCell, 2);
            }
            else if (toolMode == ToolMode.Build)
            {
                BuildModule(hoveredCell, hoveredNormal);
            }
            else
            {
                RestoreStone(hoveredCell, 2);
            }

            RebuildMesh();
        }

        private void UpdateCursor()
        {
            if (!IsInside(hoveredCell))
            {
                cursor.SetActive(false);
                HideBuildPreview();
                return;
            }

            if (toolMode == ToolMode.Build)
            {
                cursor.SetActive(false);
                UpdateBuildPreview();
                return;
            }

            HideBuildPreview();
            cursor.SetActive(true);
            cursor.transform.position = CellToWorld(hoveredCell);
        }

        private void UpdateBuildPreview()
        {
            var cells = GetBuildCells(hoveredCell, hoveredNormal);
            var previewIndex = 0;
            for (var i = 0; i < cells.Count && previewIndex < buildPreviewCells.Count; i++)
            {
                var cell = cells[i];
                if (!IsInside(cell) || stone[cell.x, cell.y, cell.z])
                {
                    continue;
                }

                var preview = buildPreviewCells[previewIndex++];
                preview.transform.position = CellToWorld(cell);
                preview.SetActive(true);
            }

            for (var i = previewIndex; i < buildPreviewCells.Count; i++)
            {
                buildPreviewCells[i].SetActive(false);
            }
        }

        private void HideBuildPreview()
        {
            for (var i = 0; i < buildPreviewCells.Count; i++)
            {
                buildPreviewCells[i].SetActive(false);
            }
        }

        private void AnimateOcean()
        {
            var ocean = transform.Find("Open Sea");
            var horizon = transform.Find("Distant Pale Sea Shelf");
            var wave = Mathf.Sin(Time.time * 0.65f) * 0.035f;

            if (ocean != null)
            {
                ocean.position = new Vector3(0f, -0.16f + wave, 0f);
            }

            if (horizon != null)
            {
                horizon.position = new Vector3(0f, -0.20f + wave * 0.35f, 0f);
            }

            AnimateWaterGleam("Water Gleam A", wave, 0.4f);
            AnimateWaterGleam("Water Gleam B", wave, 1.7f);
            AnimateWaterGleam("Water Gleam C", wave, 2.6f);
        }

        private void AnimateWaterGleam(string gleamName, float wave, float phase)
        {
            var gleam = transform.Find(gleamName);
            if (gleam == null)
            {
                return;
            }

            var position = gleam.position;
            position.y = -0.095f + wave * 0.55f + Mathf.Sin(Time.time * 0.4f + phase) * 0.01f;
            gleam.position = position;
        }

        private void CarveCavity(Vector3Int center, int radius)
        {
            for (var x = center.x - radius; x <= center.x + radius; x++)
            {
                for (var y = center.y - radius; y <= center.y + radius; y++)
                {
                    for (var z = center.z - radius; z <= center.z + radius; z++)
                    {
                        var cell = new Vector3Int(x, y, z);
                        if (!IsInside(cell) || Vector3Int.Distance(cell, center) > radius + 0.25f)
                        {
                            continue;
                        }

                        if (stone[x, y, z])
                        {
                            carvedCells++;
                        }
                        stone[x, y, z] = false;
                        buildings.Remove(cell);
                    }
                }
            }
        }

        private void RestoreStone(Vector3Int center, int radius)
        {
            for (var x = center.x - radius; x <= center.x + radius; x++)
            {
                for (var y = center.y - radius; y <= center.y + radius; y++)
                {
                    for (var z = center.z - radius; z <= center.z + radius; z++)
                    {
                        var cell = new Vector3Int(x, y, z);
                        if (!IsInside(cell) || Vector3Int.Distance(cell, center) > radius + 0.15f)
                        {
                            continue;
                        }

                        stone[x, y, z] = true;
                        buildings.Remove(cell);
                    }
                }
            }
        }

        private void BuildModule(Vector3Int anchor, Vector3 normal)
        {
            var cells = GetBuildCells(anchor, normal);

            foreach (var cell in cells)
            {
                if (!IsInside(cell) || stone[cell.x, cell.y, cell.z])
                {
                    continue;
                }

                if (buildings.Add(cell))
                {
                    buildingCells++;
                }
            }
        }

        private List<Vector3Int> GetBuildCells(Vector3Int anchor, Vector3 normal)
        {
            var outward = PrincipalHorizontal(normal);
            if (outward == Vector3Int.zero)
            {
                outward = PrincipalHorizontal(CellToWorld(anchor).normalized);
            }

            var tangent = Mathf.Abs(outward.x) > 0 ? new Vector3Int(0, 0, 1) : new Vector3Int(1, 0, 0);
            var baseCell = anchor + outward;
            var cells = new List<Vector3Int>(18);

            for (var width = -1; width <= 1; width++)
            {
                for (var height = 0; height <= 2; height++)
                {
                    for (var depth = 0; depth <= 1; depth++)
                    {
                        cells.Add(baseCell + tangent * width + Vector3Int.up * height + outward * depth);
                    }
                }
            }

            return cells;
        }

        private void RebuildMesh()
        {
            vertices.Clear();
            normals.Clear();
            stoneTriangles.Clear();
            sunStoneTriangles.Clear();
            buildingTriangles.Clear();
            faceToCell.Clear();

            for (var x = 0; x < GridWidth; x++)
            {
                for (var y = 0; y < GridHeight; y++)
                {
                    for (var z = 0; z < GridDepth; z++)
                    {
                        var cell = new Vector3Int(x, y, z);
                        if (stone[x, y, z])
                        {
                            AddVisibleCell(cell, stoneTriangles, IsStoneSolid, true, true);
                        }
                        else if (buildings.Contains(cell))
                        {
                            AddVisibleCell(cell, buildingTriangles, IsOccupied, false, false);
                        }
                    }
                }
            }

            pillarMesh.Clear();
            pillarMesh.SetVertices(vertices);
            pillarMesh.SetNormals(normals);
            pillarMesh.subMeshCount = 3;
            pillarMesh.SetTriangles(stoneTriangles, 0);
            pillarMesh.SetTriangles(sunStoneTriangles, 1);
            pillarMesh.SetTriangles(buildingTriangles, 2);
            pillarMesh.RecalculateBounds();
            pillarCollider.sharedMesh = null;
            pillarCollider.sharedMesh = pillarMesh;
        }

        private void AddVisibleCell(Vector3Int cell, List<int> targetTriangles, Func<Vector3Int, bool> occupancy, bool trackFaces, bool stylizeStone)
        {
            for (var i = 0; i < FaceDirections.Length; i++)
            {
                var neighbor = cell + FaceDirections[i];
                if (occupancy(neighbor))
                {
                    continue;
                }

                AddFace(cell, i, targetTriangles, trackFaces);
                if (stylizeStone)
                {
                    AddStoneRelief(cell, i);
                }
                else
                {
                    AddBuildingRelief(cell, i);
                }
            }
        }

        private void AddFace(Vector3Int cell, int faceIndex, List<int> targetTriangles, bool trackFaces)
        {
            var start = vertices.Count;
            var center = CellToWorld(cell);
            var corners = FaceCorners[faceIndex];
            var faceNormal = (Vector3)FaceDirections[faceIndex];

            for (var i = 0; i < 4; i++)
            {
                vertices.Add(center + corners[i] * CellSize);
                normals.Add(faceNormal);
            }

            AddDoubleSidedQuad(targetTriangles, start);

            if (!trackFaces)
            {
                return;
            }

            faceToCell[start / 4] = cell;
        }

        private void AddStoneRelief(Vector3Int cell, int faceIndex)
        {
            if (FaceDirections[faceIndex] == Vector3Int.down)
            {
                return;
            }

            var seed = Stable01(cell.x * 41 + cell.y * 97 + cell.z * 59 + faceIndex * 17);
            var raised = seed > 0.76f || FaceDirections[faceIndex] == Vector3Int.up;
            if (!raised)
            {
                return;
            }

            var normal = (Vector3)FaceDirections[faceIndex];
            Vector3 tangentA;
            Vector3 tangentB;
            GetFaceAxes(faceIndex, out tangentA, out tangentB);

            var center = CellToWorld(cell) + normal * (CellSize * 0.503f);
            var horizontalBias = Stable01(cell.x * 23 + cell.z * 31 + faceIndex * 7) - 0.5f;
            var verticalBias = Stable01(cell.y * 29 + cell.x * 13 + faceIndex * 11) - 0.5f;
            center += tangentA * horizontalBias * 0.08f + tangentB * verticalBias * 0.08f + normal * (0.015f + seed * 0.035f);

            var width = Mathf.Lerp(0.44f, 0.76f, Stable01(cell.x * 11 + cell.z * 73 + faceIndex * 5));
            var height = Mathf.Lerp(0.18f, 0.54f, Stable01(cell.y * 19 + cell.z * 43 + faceIndex * 3));
            if (FaceDirections[faceIndex] == Vector3Int.up)
            {
                width = Mathf.Lerp(0.72f, 0.96f, seed);
                height = Mathf.Lerp(0.72f, 0.96f, Stable01(cell.x * 7 + cell.z * 17));
                center += normal * 0.04f;
            }

            AddFlatPanel(center, normal, tangentA, tangentB, width, height, seed > 0.88f ? sunStoneTriangles : stoneTriangles);

            if (faceIndex != 2 && seed > 0.82f)
            {
                var ledgeCenter = center - tangentB * 0.23f + normal * 0.05f;
                AddFlatPanel(ledgeCenter, normal, tangentA, tangentB, width * 0.86f, 0.10f, sunStoneTriangles);
            }
        }

        private void AddBuildingRelief(Vector3Int cell, int faceIndex)
        {
            if (FaceDirections[faceIndex] == Vector3Int.down)
            {
                return;
            }

            var normal = (Vector3)FaceDirections[faceIndex];
            Vector3 tangentA;
            Vector3 tangentB;
            GetFaceAxes(faceIndex, out tangentA, out tangentB);
            var center = CellToWorld(cell) + normal * (CellSize * 0.56f);
            var seed = Stable01(cell.x * 53 + cell.y * 37 + cell.z * 83 + faceIndex * 11);

            AddFlatPanel(center, normal, tangentA, tangentB, Mathf.Lerp(0.72f, 0.94f, seed), Mathf.Lerp(0.72f, 0.96f, 1f - seed), buildingTriangles);

            if (faceIndex != 2 && seed > 0.38f)
            {
                AddFlatPanel(center + normal * 0.035f + tangentB * 0.12f, normal, tangentA, tangentB, 0.22f, 0.28f, stoneTriangles);
            }
        }

        private void AddFlatPanel(Vector3 center, Vector3 normal, Vector3 tangentA, Vector3 tangentB, float width, float height, List<int> targetTriangles)
        {
            var start = vertices.Count;
            var halfA = tangentA * (width * CellSize * 0.5f);
            var halfB = tangentB * (height * CellSize * 0.5f);

            vertices.Add(center - halfA - halfB);
            vertices.Add(center + halfA - halfB);
            vertices.Add(center + halfA + halfB);
            vertices.Add(center - halfA + halfB);

            for (var i = 0; i < 4; i++)
            {
                normals.Add(normal);
            }

            AddDoubleSidedQuad(targetTriangles, start);
        }

        private void AddDoubleSidedQuad(List<int> targetTriangles, int start)
        {
            targetTriangles.Add(start);
            targetTriangles.Add(start + 1);
            targetTriangles.Add(start + 2);
            targetTriangles.Add(start);
            targetTriangles.Add(start + 2);
            targetTriangles.Add(start + 3);

            targetTriangles.Add(start + 2);
            targetTriangles.Add(start + 1);
            targetTriangles.Add(start);
            targetTriangles.Add(start + 3);
            targetTriangles.Add(start + 2);
            targetTriangles.Add(start);
        }

        private void GetFaceAxes(int faceIndex, out Vector3 tangentA, out Vector3 tangentB)
        {
            if (faceIndex == 0 || faceIndex == 1)
            {
                tangentA = Vector3.forward;
                tangentB = Vector3.up;
                return;
            }

            if (faceIndex == 2 || faceIndex == 3)
            {
                tangentA = Vector3.right;
                tangentB = Vector3.forward;
                return;
            }

            tangentA = Vector3.right;
            tangentB = Vector3.up;
        }

        private float Stable01(int value)
        {
            unchecked
            {
                var x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352d;
                x ^= x >> 15;
                x *= 0x846ca68b;
                x ^= x >> 16;
                return (x & 0xffff) / 65535f;
            }
        }

        private bool IsStoneSolid(Vector3Int cell)
        {
            return IsInside(cell) && stone[cell.x, cell.y, cell.z];
        }

        private bool IsOccupied(Vector3Int cell)
        {
            return IsStoneSolid(cell) || buildings.Contains(cell);
        }

        private bool IsInside(Vector3Int cell)
        {
            return cell.x >= 0 && cell.x < GridWidth &&
                   cell.y >= 0 && cell.y < GridHeight &&
                   cell.z >= 0 && cell.z < GridDepth;
        }

        private Vector3 CellToWorld(Vector3Int cell)
        {
            return new Vector3(
                (cell.x - (GridWidth - 1) * 0.5f) * CellSize,
                cell.y * CellSize + CellSize * 0.5f,
                (cell.z - (GridDepth - 1) * 0.5f) * CellSize);
        }

        private Vector3Int WorldToCell(Vector3 world)
        {
            return new Vector3Int(
                Mathf.RoundToInt(world.x / CellSize + (GridWidth - 1) * 0.5f),
                Mathf.FloorToInt(world.y / CellSize),
                Mathf.RoundToInt(world.z / CellSize + (GridDepth - 1) * 0.5f));
        }

        private Vector3Int PrincipalHorizontal(Vector3 normal)
        {
            if (Mathf.Abs(normal.x) > Mathf.Abs(normal.z))
            {
                return normal.x >= 0f ? Vector3Int.right : Vector3Int.left;
            }

            if (Mathf.Abs(normal.z) > 0.05f)
            {
                return normal.z >= 0f ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
            }

            return Vector3Int.zero;
        }

        private int CountStoneCells()
        {
            var count = 0;
            for (var x = 0; x < GridWidth; x++)
            {
                for (var y = 0; y < GridHeight; y++)
                {
                    for (var z = 0; z < GridDepth; z++)
                    {
                        if (stone[x, y, z])
                        {
                            count++;
                        }
                    }
                }
            }
            return count;
        }

        private static readonly Vector3Int[] FaceDirections =
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.up,
            Vector3Int.down,
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        private static readonly Vector3[][] FaceCorners =
        {
            new[] { new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f) },
            new[] { new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, 0.5f) },
            new[] { new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f) },
            new[] { new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f) },
            new[] { new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f) },
            new[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f) }
        };
    }
}
