using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PillarsAbove.BuildingTiles;
using UnityEngine;

namespace PillarsAbove
{
    public sealed class PillarForgeEngine : MonoBehaviour
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        private struct CGPoint
        {
            public double x;
            public double y;
        }

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern IntPtr CGEventCreate(IntPtr source);

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern CGPoint CGEventGetLocation(IntPtr eventRef);

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern void CFRelease(IntPtr cf);

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern int CGWarpMouseCursorPosition(CGPoint newCursorPosition);
#endif

        private enum ToolMode
        {
            Carve,
            Build,
            Restore
        }

        private enum StructureKind
        {
            PlatformA0100,
            HouseDoorA1001,
            HouseWallA1002,
            ColumnA3100
        }

        [Flags]
        private enum ConnectorFlags
        {
            None = 0,
            GroundConnect = 1 << 0,
            WallConnect = 1 << 1,
            VerticalSupport = 1 << 2,
            Door = 1 << 3
        }

        private struct StructureRule
        {
            public StructureRule(string code, ConnectorFlags connectors, int priority)
            {
                Code = code;
                Connectors = connectors;
                Priority = priority;
            }

            public readonly string Code;
            public readonly ConnectorFlags Connectors;
            public readonly int Priority;
        }

        private struct ShoreContactSample
        {
            public ShoreContactSample(Vector3 center, Vector3 normal, Vector3Int cell, int seed)
            {
                Center = center;
                Normal = normal;
                Cell = cell;
                Seed = seed;
            }

            public readonly Vector3 Center;
            public readonly Vector3 Normal;
            public readonly Vector3Int Cell;
            public readonly int Seed;
        }

        private struct BuildingTileChoice
        {
            public BuildingTileChoice(BuildingTileDefinition definition, int rotation)
            {
                Definition = definition;
                Rotation = rotation;
            }

            public readonly BuildingTileDefinition Definition;
            public readonly int Rotation;
        }

        private const int GridWidth = 151;
        private const int GridHeight = 160;
        private const int GridDepth = 151;
        private const float CellSize = 1f;
        private const float PillarRadius = 71f;
        private const float BuildingTileVisualCellSize = 1f;
        private const float BuildingTileVisualScale = 0.5f;
        private const int LocalWfcRadius = 3;
        private const string OceanFoamShaderName = "PillarsAbove/OceanFoam";
        private const string PillarShaderName = "PillarsAbove/StratifiedPillar";
        private const string DayNightSkyboxResourcePath = "Skybox/MinecraftDayNightSkybox";
        private const float OceanWaveAmplitude = 2.25f;
        private const float OceanWaveSpeed = 0.64f;
        private const float OceanPrimaryWaveLength = 52f;
        private const float OceanSecondaryWaveLength = 21f;
        private const float OceanGerstnerSteepness = 0.52f;
        private const float PillarWaterlineDrop = 24f;
        private const float OceanWaveScaleMultiplier = 1f;
        private const float CameraOrbitSpeed = 13.5f;
        private const float CameraVerticalDragSpeed = 1.65f;
        private const float CameraKeyVerticalSpeed = 34f;
        private const float CameraZoomSpeed = 15f;
        private const float CameraMinimumDistance = 105f;
        private const float CameraMaximumDistance = 800f;
        private const int HouseFootprintCells = 3;
        private const int HouseMountainEmbedDepthCells = 1;
        private const int HouseMountainEmbedMaxCells = HouseFootprintCells * HouseFootprintCells;
        private const int PillarRoomCapacity = 2;
        private const int PillarCoverageCells = HouseFootprintCells;
        private const TileFace AllCubeFaces =
            TileFace.PositiveX | TileFace.NegativeX |
            TileFace.PositiveY | TileFace.NegativeY |
            TileFace.PositiveZ | TileFace.NegativeZ;

        private readonly bool[,,] stone = new bool[GridWidth, GridHeight, GridDepth];
        private readonly Dictionary<Vector3Int, StructureKind> structures = new Dictionary<Vector3Int, StructureKind>();
        private readonly Dictionary<Vector3Int, Vector3Int> structureFacing = new Dictionary<Vector3Int, Vector3Int>();
        private readonly HashSet<Vector3Int> dirtyStructures = new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> buildingModuleCenters = new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> structuralPillarCenters = new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> roomVolumeCells = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, Vector3Int> roomPillarAssignments = new Dictionary<Vector3Int, Vector3Int>();
        private readonly List<Vector3> vertices = new List<Vector3>(32768);
        private readonly List<int> stoneTriangles = new List<int>(65536);
        private readonly List<int> sunStoneTriangles = new List<int>(32768);
        private readonly List<int> buildingTriangles = new List<int>(16384);
        private readonly List<Vector3> normals = new List<Vector3>(32768);
        private readonly Dictionary<int, Vector3Int> faceToCell = new Dictionary<int, Vector3Int>();
        private readonly List<GameObject> buildPreviewCells = new List<GameObject>(27);
        private readonly Dictionary<Vector3Int, GameObject> buildingTileInstances = new Dictionary<Vector3Int, GameObject>();
        private readonly Dictionary<Vector3Int, GameObject> supportTileInstances = new Dictionary<Vector3Int, GameObject>();
        private readonly Dictionary<Vector3Int, TileFace> buildingTileOpenFaces = new Dictionary<Vector3Int, TileFace>();
        private readonly List<ShoreContactSample> shoreContacts = new List<ShoreContactSample>(256);
        private Mesh seaMesh;

        private Mesh pillarMesh;
        private MeshFilter pillarFilter;
        private MeshCollider pillarCollider;
        private MeshRenderer pillarRenderer;
        private Camera sceneCamera;
        private Material stoneMaterial;
        private Material sunStoneMaterial;
        private Material buildingMaterial;
        private Material buildingTileMaterial;
        private Material oceanMaterial;
        private Material previewMaterial;
        private Transform cameraRoot;
        private Transform lightingRoot;
        private Transform pillarRoot;
        private Transform oceanRoot;
        private Transform interactionRoot;
        private Transform buildingTileRoot;
        private BuildingTileCatalog buildingTileCatalog;
        private BuildingTileDefinition closedCubeTile;
        private ToolMode toolMode = ToolMode.Carve;
        private Vector2 orbitAngles = new Vector2(42f, 18f);
        private float orbitDistance = 320f;
        private float targetHeight = 52f;
        private Vector2 targetHorizontal;
        private bool selfTestCameraOverride;
        private Vector3 selfTestCameraPosition;
        private Vector3 selfTestCameraTarget;
        private bool cameraGestureActive;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        private CGPoint savedCursorPosition;
        private bool hasSavedCursorPosition;
#endif
        private Vector3Int hoveredCell = new Vector3Int(-1, -1, -1);
        private Vector3 hoveredNormal = Vector3.zero;
        private GameObject cursor;
        private string runtimeSelfTestStatus = "selftest: T/F7";
        private int carvedCells;
        private int buildingCells;
        private int stoneCellCount;
        private bool initialized;
        private bool suppressTileInstantiation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            foreach (var engine in FindObjectsOfType<PillarForgeEngine>())
            {
                if ((engine.gameObject.hideFlags & HideFlags.HideAndDontSave) == 0)
                {
                    return;
                }
            }

            foreach (var leakedValidationEngine in FindObjectsOfType<PillarForgeEngine>())
            {
                if ((leakedValidationEngine.gameObject.hideFlags & HideFlags.HideAndDontSave) != 0)
                {
                    Destroy(leakedValidationEngine.gameObject);
                }
            }

            var host = new GameObject("Pillars Above Engine");
            host.AddComponent<PillarForgeEngine>();
        }

        private void Awake()
        {
            if ((gameObject.hideFlags & HideFlags.HideAndDontSave) != 0)
            {
                return;
            }

            InitializeRuntimeScene();
        }

        private void InitializeRuntimeScene()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            HideSceneSourceBuildingTiles();
            var previousName = transform.name;
            if (string.IsNullOrEmpty(previousName))
            {
                previousName = "Pillars Above Engine";
            }

            transform.name = previousName;
            CreateHierarchyRoots();
            ConfigureCamera();
            ConfigureLighting();
            CreateMaterials();
            LoadBuildingTileCatalog();
            CreatePillarObjects();
            CreateOcean();
            SeedPillar();
            RebuildMesh();
            CreateCursor();
            CreateBuildPreviewPool();
        }

        private void HideSceneSourceBuildingTiles()
        {
            var source = GameObject.Find("BuildingTiles");
            if (source != null)
            {
                source.SetActive(false);
            }
        }

        public string RunPlacementValidation()
        {
            suppressTileInstantiation = true;
            try
            {
                InitializeValidationScene();
                var summaries = new List<string>();
                summaries.Add(ValidatePlacementScenario(new Vector3Int(8, 40, 17), Vector3Int.right, 4));
                summaries.Add(ValidatePlacementScenario(new Vector3Int(26, 44, 17), Vector3Int.left, 4));
                summaries.Add(ValidatePlacementScenario(new Vector3Int(17, 48, 8), new Vector3Int(0, 0, 1), 4));
                summaries.Add(ValidatePlacementScenario(new Vector3Int(17, 52, 26), new Vector3Int(0, 0, -1), 4));
                summaries.Add(ValidatePartiallyEmbeddedPlacement());
                return "placement validation ok: " + string.Join("; ", summaries);
            }
            finally
            {
                suppressTileInstantiation = false;
                ResetForValidation();
            }
        }

        private string DirectionName(Vector3Int direction)
        {
            if (direction == Vector3Int.right) return "X+";
            if (direction == Vector3Int.left) return "X-";
            if (direction.z > 0) return "Z+";
            return "Z-";
        }

        private string ValidatePlacementScenario(Vector3Int anchor, Vector3Int outward, int roomCount)
        {
            ResetForValidation();
            for (var i = 0; i < roomCount; i++)
            {
                BuildModule(anchor + outward * (i * HouseFootprintCells), outward);
            }

            var expectedShellCells = (HouseFootprintCells * HouseFootprintCells * HouseFootprintCells - 1) * roomCount;
            var columnCount = CountStructures(StructureKind.ColumnA3100);
            if (buildingCells != expectedShellCells)
            {
                throw new InvalidOperationException("Expected " + expectedShellCells + " building cells, got " + buildingCells + ".");
            }

            if (buildingTileInstances.Count != expectedShellCells || buildingTileOpenFaces.Count != expectedShellCells)
            {
                throw new InvalidOperationException("Expected " + expectedShellCells + " runtime Cube tile choices, got " + buildingTileInstances.Count + ".");
            }

            if (buildingModuleCenters.Count != roomCount)
            {
                throw new InvalidOperationException("Expected " + roomCount + " placed rooms, got " + buildingModuleCenters.Count + ".");
            }

            var expectedPillars = (roomCount + 1) / 2;
            if (structuralPillarCenters.Count != expectedPillars)
            {
                throw new InvalidOperationException("Expected " + expectedPillars + " pillars below alternating fully suspended rooms, got " + structuralPillarCenters.Count + ".");
            }

            for (var i = 0; i < roomCount; i++)
            {
                var center = anchor + outward * (i * HouseFootprintCells + 2);
                var shouldHavePillar = i % 2 == 0;
                if (structuralPillarCenters.Contains(center) != shouldHavePillar)
                {
                    throw new InvalidOperationException("Pillar center mismatch at room " + i + " center " + center + ".");
                }
            }

            if (supportTileInstances.Count != columnCount)
            {
                throw new InvalidOperationException("Expected support tile choices for every column cell, got " + supportTileInstances.Count + " for " + columnCount + " column cells.");
            }

            return DirectionName(outward) + " rooms=" + roomCount + " shellCells=" + buildingCells + " cubeTiles=" + buildingTileInstances.Count + " hollowCenters=" + roomCount + " pillars=" + structuralPillarCenters.Count + " columns=" + columnCount + " supportTiles=" + supportTileInstances.Count;
        }

        private string ValidatePartiallyEmbeddedPlacement()
        {
            ResetForValidation();
            var anchor = new Vector3Int(8, 40, 17);
            var outward = Vector3Int.right;
            var embeddedCells = new[]
            {
                anchor + outward,
                anchor + outward + Vector3Int.up,
                anchor + outward + new Vector3Int(0, 0, 1)
            };

            for (var i = 0; i < embeddedCells.Length; i++)
            {
                var cell = embeddedCells[i];
                stone[cell.x, cell.y, cell.z] = true;
                stoneCellCount++;
            }

            BuildModule(anchor, outward);
            for (var i = 0; i < embeddedCells.Length; i++)
            {
                var cell = embeddedCells[i];
                if (IsStoneSolid(cell) || !structures.ContainsKey(cell))
                {
                    throw new InvalidOperationException("House did not replace allowed rear-layer mountain cell " + cell + ".");
                }
            }

            ResetForValidation();
            var blockedInterior = anchor + outward * 2 + Vector3Int.up;
            stone[blockedInterior.x, blockedInterior.y, blockedInterior.z] = true;
            stoneCellCount++;
            BuildModule(anchor, outward);
            if (buildingModuleCenters.Count != 0)
            {
                throw new InvalidOperationException("House was allowed to embed beyond its rear mountain layer.");
            }

            return "partialEmbed=" + embeddedCells.Length + " deepOverlapBlocked";
        }

        private void InitializeValidationScene()
        {
            if (buildingTileRoot == null)
            {
                var root = new GameObject("Validation Building Tile Prefabs");
                root.hideFlags = HideFlags.HideAndDontSave;
                root.transform.SetParent(transform, false);
                buildingTileRoot = root.transform;
            }

            LoadBuildingTileCatalog();
        }

        private void CreateHierarchyRoots()
        {
            transform.name = "Pillars Above Runtime Scene";
            cameraRoot = CreateRoot("00 Camera Rig");
            lightingRoot = CreateRoot("10 Lighting");
            pillarRoot = CreateRoot("20 Pillar Grid World");
            oceanRoot = CreateRoot("30 Ocean System");
            buildingTileRoot = CreateRoot("35 Building Tile Prefabs");
            interactionRoot = CreateRoot("40 Interaction Preview");
        }

        private Transform CreateRoot(string rootName)
        {
            var root = new GameObject(rootName);
            root.transform.SetParent(transform);
            return root.transform;
        }

        private void Update()
        {
            HandleCameraInput();
            UpdateCamera();
            UpdateHover();
            HandleToolInput();
            HandleRuntimeSelfTestInput();
            UpdateCursor();
        }

        private void OnGUI()
        {
            const int size = 42;
            GUI.Box(new Rect(14, 14, 250, 206), GUIContent.none);
            DrawToolButton(new Rect(24, 24, size, size), ToolMode.Carve, "-");
            DrawToolButton(new Rect(72, 24, size, size), ToolMode.Build, "+");
            DrawToolButton(new Rect(120, 24, size, size), ToolMode.Restore, "o");

            GUI.Label(new Rect(24, 76, 168, 24), "stone: " + CountStoneCells());
            GUI.Label(new Rect(24, 100, 168, 24), "carved: " + carvedCells);
            GUI.Label(new Rect(24, 124, 168, 24), "built: " + buildingCells);
            GUI.Label(new Rect(24, 148, 220, 24), "cells: " + structures.Count);
            GUI.Label(new Rect(24, 172, 220, 24), runtimeSelfTestStatus);
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
            sceneCamera.orthographic = false;
            sceneCamera.fieldOfView = 36f;
            sceneCamera.nearClipPlane = 0.1f;
            sceneCamera.farClipPlane = 6000f;
            sceneCamera.backgroundColor = new Color(0.055f, 0.14f, 0.18f);
            sceneCamera.depthTextureMode |= DepthTextureMode.Depth;
            sceneCamera.cullingMask = ~0;
            sceneCamera.depth = 100f;
            sceneCamera.enabled = true;
            sceneCamera.tag = "MainCamera";
            sceneCamera.transform.SetParent(cameraRoot);

            foreach (var camera in FindObjectsOfType<Camera>())
            {
                if (camera == sceneCamera)
                {
                    continue;
                }

                camera.enabled = false;
                camera.tag = "Untagged";
            }
        }

        private void ConfigureLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.38f, 0.41f, 0.46f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.26f, 0.30f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.11f, 0.13f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.44f, 0.49f, 0.52f);
            RenderSettings.fogStartDistance = 280f;
            RenderSettings.fogEndDistance = 1400f;

            var existing = FindObjectOfType<Light>();
            if (existing == null)
            {
                var lightObject = new GameObject("Sun");
                existing = lightObject.AddComponent<Light>();
            }

            existing.type = LightType.Directional;
            existing.name = "Sun Key Light";
            existing.transform.SetParent(lightingRoot);
            existing.intensity = 1.45f;
            existing.color = new Color(1f, 0.90f, 0.76f);
            existing.transform.rotation = Quaternion.Euler(38f, 32f, 0f);
            existing.shadows = LightShadows.Soft;
            RenderSettings.sun = existing;

            var fillLightObject = new GameObject("Building Tile Soft Fill Light");
            fillLightObject.transform.SetParent(lightingRoot);
            var fillLight = fillLightObject.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.22f;
            fillLight.color = new Color(0.62f, 0.72f, 1f, 1f);
            fillLight.transform.rotation = Quaternion.Euler(24f, 220f, 0f);
            fillLight.shadows = LightShadows.None;

            var skyboxTemplate = Resources.Load<Material>(DayNightSkyboxResourcePath);
            if (skyboxTemplate != null)
            {
                RenderSettings.skybox = skyboxTemplate;
            }

            var dayNightController = FindObjectOfType<global::DayNightSkyboxController>();
            if (dayNightController == null)
            {
                var controllerObject = new GameObject("Day Night Skybox Controller");
                controllerObject.transform.SetParent(lightingRoot);
                dayNightController = controllerObject.AddComponent<global::DayNightSkyboxController>();
            }
            else
            {
                dayNightController.transform.SetParent(lightingRoot);
            }

            dayNightController.sunLight = existing;
            dayNightController.skyboxTemplate = skyboxTemplate;
            dayNightController.autoAdvance = true;
            dayNightController.timeOfDay = 0.36f;
            dayNightController.dayLengthSeconds = 240f;
            dayNightController.sunAzimuth = 46f;
            dayNightController.daySunIntensity = 1.45f;
            dayNightController.nightSunIntensity = 0.12f;
            dayNightController.daySunColor = new Color(1f, 0.90f, 0.76f, 1f);
            dayNightController.nightSunColor = new Color(0.30f, 0.38f, 0.58f, 1f);
            dayNightController.moonMaxIntensity = 0.58f;
            dayNightController.moonDawnDuskIntensity = 0.16f;
            dayNightController.moonColor = new Color(0.48f, 0.62f, 1f, 1f);
            dayNightController.forceRealtimeShadows = true;
            dayNightController.shadowMode = LightShadows.Soft;
            dayNightController.dayShadowStrength = 0.42f;
            dayNightController.nightShadowStrength = 0.22f;
            dayNightController.shadowBias = 0.065f;
            dayNightController.shadowNormalBias = 0.62f;
            dayNightController.shadowNearPlane = 0.20f;
            dayNightController.shadowCustomResolution = 4096;
            dayNightController.enforceQualityShadowProfile = true;
            dayNightController.qualityShadowDistance = 180f;
            dayNightController.qualityShadowCascades = 4;
            dayNightController.enforceShadowAntiBandingProfile = false;
            dayNightController.useAdaptiveShadowBudget = false;
            dayNightController.ambientIntensity = 0.74f;
            dayNightController.nightVisibilityAmbientBoost = 0.26f;
            dayNightController.enableWorldFeedback = true;
            dayNightController.controlAmbient = true;
            dayNightController.controlFog = true;
            dayNightController.controlReflection = true;
            dayNightController.useTrilightAmbient = true;
            dayNightController.dayAmbientSky = new Color(0.58f, 0.66f, 0.76f, 1f);
            dayNightController.dayAmbientEquator = new Color(0.42f, 0.39f, 0.34f, 1f);
            dayNightController.dayAmbientGround = new Color(0.28f, 0.26f, 0.24f, 1f);
            dayNightController.nightAmbientSky = new Color(0.18f, 0.24f, 0.42f, 1f);
            dayNightController.nightAmbientEquator = new Color(0.14f, 0.17f, 0.28f, 1f);
            dayNightController.nightAmbientGround = new Color(0.10f, 0.10f, 0.16f, 1f);
            dayNightController.dayFog = new Color(0.48f, 0.56f, 0.64f, 1f);
            dayNightController.nightFog = new Color(0.05f, 0.08f, 0.16f, 1f);
            dayNightController.dayFogDensity = 0.00025f;
            dayNightController.duskFogDensity = 0.00065f;
            dayNightController.nightFogDensity = 0.0011f;
            dayNightController.dawnFogDensity = 0.00075f;
            dayNightController.dayFogStartDistance = 280f;
            dayNightController.duskFogStartDistance = 150f;
            dayNightController.nightFogStartDistance = 120f;
            dayNightController.dawnFogStartDistance = 170f;
            dayNightController.dayFogEndDistance = 1400f;
            dayNightController.duskFogEndDistance = 900f;
            dayNightController.nightFogEndDistance = 720f;
            dayNightController.dawnFogEndDistance = 980f;
            dayNightController.dayReflectionIntensity = 0.92f;
            dayNightController.nightReflectionIntensity = 0.58f;
            dayNightController.dayExposure = 0.92f;
            dayNightController.nightExposure = 0.36f;

            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowDistance = 220f;
            QualitySettings.shadowCascades = 4;
        }

        private void CreateMaterials()
        {
            stoneMaterial = new Material(FindPillarShader());
            stoneMaterial.name = "Runtime Stratified Stone";
            ConfigurePillarMaterial(
                stoneMaterial,
                new Color(0.25f, 0.26f, 0.27f, 1f),
                new Color(0.46f, 0.40f, 0.33f, 1f),
                new Color(0.64f, 0.54f, 0.40f, 1f));

            sunStoneMaterial = new Material(FindPillarShader());
            sunStoneMaterial.name = "Runtime Stone Relief";
            ConfigurePillarMaterial(
                sunStoneMaterial,
                new Color(0.25f, 0.26f, 0.27f, 1f),
                new Color(0.46f, 0.40f, 0.33f, 1f),
                new Color(0.64f, 0.54f, 0.40f, 1f));

            buildingMaterial = new Material(Shader.Find("Standard"));
            buildingMaterial.name = "Runtime Hidden Structure Occupancy";
            buildingMaterial.color = new Color(1f, 1f, 1f, 0f);
            buildingMaterial.SetFloat("_Mode", 3f);
            buildingMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            buildingMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            buildingMaterial.SetInt("_ZWrite", 0);
            buildingMaterial.DisableKeyword("_ALPHATEST_ON");
            buildingMaterial.EnableKeyword("_ALPHABLEND_ON");
            buildingMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            buildingMaterial.renderQueue = 3000;

            var buildingTileShader = Shader.Find("Standard");
            if (buildingTileShader == null)
            {
                buildingTileShader = Shader.Find("Diffuse");
            }

            if (buildingTileShader == null)
            {
                buildingTileShader = Shader.Find("Unlit/Color");
            }

            buildingTileMaterial = new Material(buildingTileShader);
            buildingTileMaterial.name = "Runtime White Building Tiles Lit";
            var buildingTileColor = new Color(0.82f, 0.88f, 0.90f, 1f);
            buildingTileMaterial.color = buildingTileColor;
            if (buildingTileMaterial.HasProperty("_BaseColor"))
            {
                buildingTileMaterial.SetColor("_BaseColor", buildingTileColor);
            }

            if (buildingTileMaterial.HasProperty("_Color"))
            {
                buildingTileMaterial.SetColor("_Color", buildingTileColor);
            }

            if (buildingTileMaterial.HasProperty("_Glossiness"))
            {
                buildingTileMaterial.SetFloat("_Glossiness", 0.08f);
            }

            if (buildingTileMaterial.HasProperty("_Metallic"))
            {
                buildingTileMaterial.SetFloat("_Metallic", 0f);
            }

            if (buildingTileMaterial.HasProperty("_Surface"))
            {
                buildingTileMaterial.SetFloat("_Surface", 0f);
            }

            if (buildingTileMaterial.HasProperty("_Mode"))
            {
                buildingTileMaterial.SetFloat("_Mode", 0f);
            }

            if (buildingTileMaterial.HasProperty("_Cull"))
            {
                buildingTileMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }

            buildingTileMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            buildingTileMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            buildingTileMaterial.SetInt("_ZWrite", 1);
            buildingTileMaterial.DisableKeyword("_ALPHATEST_ON");
            buildingTileMaterial.DisableKeyword("_ALPHABLEND_ON");
            buildingTileMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            buildingTileMaterial.renderQueue = -1;

            if (buildingTileMaterial.HasProperty("_EmissionColor"))
            {
                buildingTileMaterial.DisableKeyword("_EMISSION");
                buildingTileMaterial.SetColor("_EmissionColor", Color.black);
            }

            oceanMaterial = new Material(FindOceanShader());
            oceanMaterial.name = "Runtime Vast Green Sea";
            oceanMaterial.color = new Color(0.08f, 0.62f, 0.62f);
            oceanMaterial.SetFloat("_Glossiness", 0.38f);
            oceanMaterial.SetFloat("_Metallic", 0.02f);
            ConfigureOceanFoamMaterial(oceanMaterial, 2990);

            previewMaterial = new Material(Shader.Find("Standard"));
            previewMaterial.name = "Runtime White Tile Preview";
            previewMaterial.color = new Color(0.88f, 0.94f, 1f, 0.34f);
            previewMaterial.SetFloat("_Mode", 3f);
            previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            previewMaterial.SetInt("_ZWrite", 0);
            previewMaterial.DisableKeyword("_ALPHATEST_ON");
            previewMaterial.EnableKeyword("_ALPHABLEND_ON");
            previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            previewMaterial.renderQueue = 3000;
        }

        private Shader FindOceanShader()
        {
            var shader = Shader.Find(OceanFoamShaderName);
            return shader != null ? shader : Shader.Find("Standard");
        }

        private Shader FindPillarShader()
        {
            var shader = Shader.Find(PillarShaderName);
            return shader != null ? shader : Shader.Find("Standard");
        }

        private static void ConfigurePillarMaterial(Material material, Color bottomColor, Color middleColor, Color topColor)
        {
            if (material.shader != null && material.shader.name == PillarShaderName)
            {
                material.SetColor("_BottomColor", bottomColor);
                material.SetColor("_MiddleColor", middleColor);
                material.SetColor("_TopColor", topColor);
                material.SetFloat("_GradientBottom", -PillarWaterlineDrop);
                material.SetFloat("_GradientMiddle", 46f);
                material.SetFloat("_GradientTop", GridHeight - PillarWaterlineDrop);
                material.SetFloat("_DrySmoothness", 0.11f);
                material.SetFloat("_WetSmoothness", 0.46f);
                material.SetFloat("_WetLineHeight", 5f);
                material.SetFloat("_WetFadeDistance", 18f);
                material.SetColor("_StoneSpecular", new Color(0.08f, 0.09f, 0.10f, 1f));
                material.SetColor("_WetSpecular", new Color(0.30f, 0.36f, 0.40f, 1f));
                return;
            }

            material.color = middleColor;
            material.SetFloat("_Glossiness", 0.10f);
        }

        private void ConfigureOceanFoamMaterial(Material material, int renderQueue)
        {
            if (material.shader == null || material.shader.name != OceanFoamShaderName)
            {
                return;
            }

            material.SetColor("_ShallowColor", new Color(0.035f, 0.17f, 0.25f, 1f));
            material.SetColor("_MidColor", new Color(0.035f, 0.17f, 0.25f, 1f));
            material.SetColor("_DeepColor", new Color(0.035f, 0.17f, 0.25f, 1f));
            material.SetColor("_HorizonColor", new Color(0.30f, 0.38f, 0.42f, 1f));
            material.SetColor("_FarWaterColor", new Color(0.50f, 0.51f, 0.51f, 1f));
            material.SetColor("_DiffuseTint", new Color(0.17f, 0.22f, 0.25f, 1f));
            material.SetColor("_ShoreRippleColor", new Color(0.36f, 0.45f, 0.47f, 1f));
            material.SetFloat("_Alpha", 0.98f);
            material.SetFloat("_WaveAmplitude", OceanWaveAmplitude);
            material.SetFloat("_WaveSpeed", OceanWaveSpeed);
            material.SetFloat("_PrimaryWaveLength", OceanPrimaryWaveLength * OceanWaveScaleMultiplier);
            material.SetFloat("_SecondaryWaveLength", OceanSecondaryWaveLength * OceanWaveScaleMultiplier);
            material.SetFloat("_GerstnerSteepness", OceanGerstnerSteepness);
            material.SetFloat("_MicroRippleStrength", 0.22f);
            material.SetColor("_FoamColor", new Color(0.64f, 0.67f, 0.66f, 1f));
            material.SetFloat("_ShoreFoamDistance", 11.5f);
            material.SetFloat("_ShoreFoamStrength", 1.05f);
            material.SetFloat("_CrestFoamThreshold", 0.62f);
            material.SetFloat("_CrestFoamWidth", 0.26f);
            material.SetFloat("_CrestFoamStrength", 0.78f);
            material.SetFloat("_FoamScale", 0.18f);
            material.SetFloat("_FoamDrift", 0.46f);
            material.SetFloat("_Smoothness", 72f);
            material.SetFloat("_SunGlitterStrength", 0.42f);
            material.SetFloat("_ReflectionStrength", 0.38f);
            material.SetFloat("_DiffuseStrength", 0.32f);
            material.SetFloat("_FresnelPower", 3.2f);
            material.SetFloat("_DepthColorRange", 28f);
            material.SetFloat("_FarFogStart", 360f);
            material.SetFloat("_FarFogEnd", 1500f);
            material.renderQueue = renderQueue;
        }

        private void CreatePillarObjects()
        {
            var pillar = new GameObject("Voxelized Megalith Pillar");
            pillar.transform.SetParent(pillarRoot);
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
            var ocean = new GameObject("Sea Surface - Low Poly Swell");
            ocean.transform.SetParent(oceanRoot);
            ocean.transform.position = new Vector3(0f, -0.16f, 0f);
            var filter = ocean.AddComponent<MeshFilter>();
            var renderer = ocean.AddComponent<MeshRenderer>();
            seaMesh = CreateSeaMesh(3600f, 384);
            filter.sharedMesh = seaMesh;
            renderer.sharedMaterial = oceanMaterial;
            renderer.receiveShadows = true;

        }

        private Mesh CreateSeaMesh(float size, int divisions)
        {
            var mesh = new Mesh { name = "Runtime Smooth Sea Mesh" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var verts = new List<Vector3>((divisions + 1) * (divisions + 1));
            var tris = new List<int>(divisions * divisions * 6);
            var half = size * 0.5f;
            var step = size / divisions;
            for (var z = 0; z <= divisions; z++)
            {
                for (var x = 0; x <= divisions; x++)
                {
                    verts.Add(new Vector3(-half + x * step, 0f, -half + z * step));
                }
            }

            var stride = divisions + 1;
            for (var z = 0; z < divisions; z++)
            {
                for (var x = 0; x < divisions; x++)
                {
                    var a = z * stride + x;
                    var b = a + 1;
                    var c = (z + 1) * stride + x + 1;
                    var d = (z + 1) * stride + x;
                    tris.Add(a);
                    tris.Add(d);
                    tris.Add(b);
                    tris.Add(b);
                    tris.Add(d);
                    tris.Add(c);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, OceanWaveAmplitude * 4f, size));
            return mesh;
        }

        private void CreateCursor()
        {
            cursor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cursor.name = "Grid Cursor";
            cursor.transform.SetParent(interactionRoot);
            cursor.transform.localScale = Vector3.one * 0.92f;
            cursor.GetComponent<MeshRenderer>().sharedMaterial = previewMaterial;
            Destroy(cursor.GetComponent<BoxCollider>());
            cursor.SetActive(false);
        }

        private void CreateBuildPreviewPool()
        {
            for (var i = 0; i < HouseFootprintCells * HouseFootprintCells * HouseFootprintCells; i++)
            {
                var preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                preview.name = "Build Preview Cell " + i;
                preview.transform.SetParent(interactionRoot);
                preview.transform.localScale = Vector3.one * 0.86f;
                preview.GetComponent<MeshRenderer>().sharedMaterial = previewMaterial;
                Destroy(preview.GetComponent<BoxCollider>());
                preview.SetActive(false);
                buildPreviewCells.Add(preview);
            }
        }

        private void LoadBuildingTileCatalog()
        {
            buildingTileCatalog = Resources.Load<BuildingTileCatalog>("BuildingTileCatalog");
            closedCubeTile = null;
            if (buildingTileCatalog == null)
            {
                Debug.LogWarning("BuildingTileCatalog not found in Resources; runtime will use logical building cells without prefab instances.", this);
                return;
            }

            foreach (var definition in buildingTileCatalog.Definitions(TileLayer.Cube))
            {
                if (definition.CanonicalOpenFaces == TileFace.None)
                {
                    closedCubeTile = definition;
                    break;
                }
            }
        }

        private void SeedPillar()
        {
            var center = new Vector2((GridWidth - 1) * 0.5f, (GridDepth - 1) * 0.5f);
            stoneCellCount = 0;

            for (var x = 0; x < GridWidth; x++)
            {
                for (var z = 0; z < GridDepth; z++)
                {
                    var delta = new Vector2(x, z) - center;
                    var distance = delta.magnitude;
                    var angle = Mathf.Atan2(delta.y, delta.x);

                    for (var y = 0; y < GridHeight; y++)
                    {
                        var baseFlare = Mathf.Lerp(1.35f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 22f, y)));
                        var waterlineBulge = Mathf.Exp(-Mathf.Pow((y - PillarWaterlineDrop) / 13f, 2f)) * 1.20f;
                        var upperTaper = Mathf.Lerp(0f, 1.75f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PillarWaterlineDrop + 28f, GridHeight - 1f, y)));
                        var verticalRibs = (Mathf.Cos(angle * 6f + 0.35f) + 1f) * 0.22f;
                        var terrace = ((y / 9) & 1) == 0 ? 0.24f : -0.08f;
                        var cragNoise = Mathf.Sin(x * 1.17f + z * 0.73f + y * 0.11f) * 0.30f
                            + Mathf.Cos(z * 1.31f - x * 0.41f + y * 0.07f) * 0.22f;
                        var localRadius = PillarRadius + baseFlare + waterlineBulge - upperTaper + verticalRibs + terrace + cragNoise;
                        stone[x, y, z] = distance <= localRadius;
                        if (stone[x, y, z])
                        {
                            stoneCellCount++;
                        }
                    }
                }
            }

            var centerCell = (GridWidth - 1) / 2;
            var waterlineCell = Mathf.RoundToInt(PillarWaterlineDrop);
            var surfaceOffset = Mathf.RoundToInt(PillarRadius);
            CarveCavity(new Vector3Int(centerCell, waterlineCell + 12, centerCell - surfaceOffset), 2);
            CarveCavity(new Vector3Int(centerCell - surfaceOffset, waterlineCell + 34, centerCell), 2);
            CarveCavity(new Vector3Int(centerCell + surfaceOffset, waterlineCell + 58, centerCell), 2);
        }

        private void HandleCameraInput()
        {
            var rightMouseGesture = Input.GetMouseButton(1);
            var cameraGesture = rightMouseGesture || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            SetCameraGestureCursor(cameraGesture);

            if (cameraGesture)
            {
                orbitAngles.x += Input.GetAxis("Mouse X") * CameraOrbitSpeed;
                targetHeight -= Input.GetAxis("Mouse Y") * CameraVerticalDragSpeed;
            }

            orbitDistance = Mathf.Clamp(
                orbitDistance - Input.mouseScrollDelta.y * CameraZoomSpeed,
                CameraMinimumDistance,
                CameraMaximumDistance);

            if (Input.GetKey(KeyCode.Q))
            {
                targetHeight -= Time.deltaTime * CameraKeyVerticalSpeed;
            }
            if (Input.GetKey(KeyCode.E))
            {
                targetHeight += Time.deltaTime * CameraKeyVerticalSpeed;
            }
            targetHeight = Mathf.Clamp(targetHeight, 3f, GridHeight - 3f);

            if (Input.GetKeyDown(KeyCode.Alpha1)) toolMode = ToolMode.Carve;
            if (Input.GetKeyDown(KeyCode.Alpha2)) toolMode = ToolMode.Build;
            if (Input.GetKeyDown(KeyCode.Alpha3)) toolMode = ToolMode.Restore;
        }

        private void SetCameraGestureCursor(bool active)
        {
            if (cameraGestureActive == active)
            {
                return;
            }

            cameraGestureActive = active;
            if (active)
            {
                SaveCursorPosition();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                RestoreCursorPosition();
            }
        }

        private void SaveCursorPosition()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            var eventRef = CGEventCreate(IntPtr.Zero);
            if (eventRef == IntPtr.Zero)
            {
                hasSavedCursorPosition = false;
                return;
            }

            savedCursorPosition = CGEventGetLocation(eventRef);
            hasSavedCursorPosition = true;
            CFRelease(eventRef);
#endif
        }

        private void RestoreCursorPosition()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (!hasSavedCursorPosition)
            {
                return;
            }

            CGWarpMouseCursorPosition(savedCursorPosition);
            hasSavedCursorPosition = false;
#endif
        }

        private void OnDisable()
        {
            SetCameraGestureCursor(false);
        }

        private void UpdateCamera()
        {
            for (var i = 0; i < Camera.allCamerasCount; i++)
            {
                var camera = Camera.allCameras[i];
                if (camera != sceneCamera)
                {
                    camera.enabled = false;
                }
            }

            if (selfTestCameraOverride)
            {
                sceneCamera.transform.position = selfTestCameraPosition;
                sceneCamera.transform.LookAt(selfTestCameraTarget);
                return;
            }

            var target = new Vector3(targetHorizontal.x, targetHeight, targetHorizontal.y);
            var rotation = Quaternion.Euler(orbitAngles.y, orbitAngles.x, 0f);
            sceneCamera.transform.position = target + rotation * new Vector3(0f, 0f, -orbitDistance);
            sceneCamera.transform.LookAt(target);
        }

        private void UpdateHover()
        {
            hoveredCell = new Vector3Int(-1, -1, -1);
            hoveredNormal = Vector3.zero;

            if (cameraGestureActive || GUIUtility.hotControl != 0 || Input.mousePosition.x < 244f && Screen.height - Input.mousePosition.y < 210f)
            {
                return;
            }

            var ray = sceneCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 1200f))
            {
                return;
            }

            hoveredNormal = hit.normal;
            hoveredCell = WorldToCell(hit.point - hit.normal * 0.025f);
        }

        private void HandleToolInput()
        {
            if (cameraGestureActive || !Input.GetMouseButtonDown(0) || !IsInside(hoveredCell))
            {
                return;
            }

            if (CountStoneCells() == 0)
            {
                ResetPillarWorld();
                return;
            }

            var stoneCountBefore = CountStoneCells();
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

            RunLocalWfc();
            if (toolMode == ToolMode.Build &&
                (CountStoneCells() == 0 ||
                 CountStoneCells() > stoneCountBefore ||
                 stoneCountBefore - CountStoneCells() > HouseMountainEmbedMaxCells))
            {
                ResetPillarWorld();
                return;
            }

            RebuildMesh();
        }

        private void HandleRuntimeSelfTestInput()
        {
            if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.F7))
            {
                RunBuildSelfTest();
            }
        }

        private void RunBuildSelfTest()
        {
            ResetPillarWorld();
            var stoneBefore = CountStoneCells();
            if (stoneBefore == 0)
            {
                runtimeSelfTestStatus = "selftest: reset empty";
                return;
            }

            var testOutward = Vector3Int.left;
            var testAnchor = FindSelfTestSurfaceAnchor(testOutward, Mathf.RoundToInt(PillarWaterlineDrop) + 34, (GridDepth - 1) / 2);
            CarveCavity(testAnchor + testOutward * 2 + Vector3Int.up, 3);
            stoneBefore = CountStoneCells();
            var structuresBefore = structures.Count;
            BuildModule(testAnchor, testOutward);
            var stoneAfterBuild = CountStoneCells();
            if (stoneAfterBuild == 0 ||
                stoneAfterBuild > stoneBefore ||
                stoneBefore - stoneAfterBuild > HouseMountainEmbedMaxCells)
            {
                ResetPillarWorld();
                runtimeSelfTestStatus = "selftest: blocked bad build";
                return;
            }

            if (structures.Count == structuresBefore)
            {
                runtimeSelfTestStatus = "selftest: no test build";
                return;
            }

            var focusCell = testAnchor + testOutward * 2 + Vector3Int.up;
            var focusWorld = CellToWorld(focusCell);
            Bounds placedTileBounds;
            if (TryGetBuildingTileInstanceBounds(out placedTileBounds))
            {
                focusWorld = placedTileBounds.center;
            }

            var tangent = Mathf.Abs(testOutward.x) > 0 ? new Vector3Int(0, 0, 1) : new Vector3Int(1, 0, 0);
            targetHorizontal = new Vector2(focusWorld.x, focusWorld.z);
            targetHeight = Mathf.Clamp(focusWorld.y, 3f, GridHeight - 3f);
            orbitDistance = 8f;
            orbitAngles = new Vector2(CameraYawForOutward(testOutward), 22f);
            selfTestCameraOverride = true;
            selfTestCameraTarget = focusWorld;
            selfTestCameraPosition = focusWorld + (Vector3)testOutward * 7f;
            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = 1.9f;
            sceneCamera.transform.position = selfTestCameraPosition;
            sceneCamera.transform.LookAt(selfTestCameraTarget);
            RebuildMesh();
            runtimeSelfTestStatus = "selftest: ok s" + stoneAfterBuild + " b" + (structures.Count - structuresBefore);
        }

        private Vector3Int FindSelfTestSurfaceAnchor(Vector3Int outward, int y, int z)
        {
            if (outward == Vector3Int.left)
            {
                for (var x = 0; x < GridWidth; x++)
                {
                    var cell = new Vector3Int(x, y, z);
                    if (!IsStoneSolid(cell))
                    {
                        continue;
                    }

                    if (!IsStoneSolid(cell + outward))
                    {
                        return cell;
                    }
                }
            }

            return new Vector3Int(5, y, z);
        }

        private bool TryGetBuildingTileInstanceBounds(out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var pair in buildingTileInstances)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                Bounds instanceBounds;
                if (!TryGetRendererBounds(pair.Value, out instanceBounds))
                {
                    continue;
                }

                if (!found)
                {
                    bounds = instanceBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(instanceBounds);
                }
            }

            return found;
        }

        private float CameraYawForOutward(Vector3Int outward)
        {
            if (outward == Vector3Int.right) return -90f;
            if (outward == Vector3Int.left) return 90f;
            if (outward.z > 0) return 180f;
            return 0f;
        }

        private void ResetPillarWorld()
        {
            Array.Clear(stone, 0, stone.Length);
            structures.Clear();
            structureFacing.Clear();
            dirtyStructures.Clear();
            buildingModuleCenters.Clear();
            structuralPillarCenters.Clear();
            roomVolumeCells.Clear();
            roomPillarAssignments.Clear();
            ClearBuildingTileInstances();
            ClearSupportTileInstances();
            shoreContacts.Clear();
            carvedCells = 0;
            buildingCells = 0;
            SeedPillar();
            RebuildMesh();
        }

        private void ResetForValidation()
        {
            Array.Clear(stone, 0, stone.Length);
            stoneCellCount = 0;
            structures.Clear();
            structureFacing.Clear();
            dirtyStructures.Clear();
            buildingModuleCenters.Clear();
            structuralPillarCenters.Clear();
            roomVolumeCells.Clear();
            roomPillarAssignments.Clear();
            ClearBuildingTileInstances();
            ClearSupportTileInstances();
            shoreContacts.Clear();
            carvedCells = 0;
            buildingCells = 0;
        }

        private int CountStructures(StructureKind kind)
        {
            var count = 0;
            foreach (var pair in structures)
            {
                if (pair.Value == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private void ValidatePlacedTileInterfaces()
        {
            foreach (var pair in buildingTileOpenFaces)
            {
                var cell = pair.Key;
                var openFaces = pair.Value;
                for (var i = 0; i < FaceDirections.Length; i++)
                {
                    var face = TileFaceFromDirection(FaceDirections[i]);
                    var open = (openFaces & face) != 0;
                    var neighbor = cell + FaceDirections[i];
                    TileFace neighborOpenFaces;
                    if (buildingTileOpenFaces.TryGetValue(neighbor, out neighborOpenFaces))
                    {
                        var neighborFace = BuildingWfcRules.Opposite(face);
                        var neighborOpen = (neighborOpenFaces & neighborFace) != 0;
                        if (!open || !neighborOpen)
                        {
                            throw new InvalidOperationException("Placed Cube closes an internal shell connection between " + cell + " " + face + " and " + neighbor + " " + neighborFace + ".");
                        }

                        continue;
                    }

                    if (roomVolumeCells.Contains(neighbor) && !structures.ContainsKey(neighbor))
                    {
                        if (!open)
                        {
                            throw new InvalidOperationException("Placed Cube closes toward room interior at " + cell + " " + face + ".");
                        }

                        continue;
                    }

                    if (open)
                    {
                        throw new InvalidOperationException("Placed Cube has an open exterior boundary face at " + cell + " " + face + ".");
                    }
                }
            }
        }

        private void UpdateCursor()
        {
            if (cameraGestureActive || !IsInside(hoveredCell))
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
                            stoneCellCount--;
                        }
                        stone[x, y, z] = false;
                        structures.Remove(cell);
                        structureFacing.Remove(cell);
                        roomVolumeCells.Remove(cell);
                        RemoveBuildingTileInstance(cell);
                        RemoveSupportTileInstance(cell);
                        MarkDirtyArea(cell, 1);
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

                        if (!stone[x, y, z])
                        {
                            stoneCellCount++;
                        }
                        stone[x, y, z] = true;
                        structures.Remove(cell);
                        structureFacing.Remove(cell);
                        roomVolumeCells.Remove(cell);
                        RemoveBuildingTileInstance(cell);
                        RemoveSupportTileInstance(cell);
                        MarkDirtyArea(cell, 1);
                    }
                }
            }
        }

        private void BuildModule(Vector3Int anchor, Vector3 normal)
        {
            var outward = PrincipalHorizontal(normal);
            if (outward == Vector3Int.zero)
            {
                return;
            }

            var cells = GetBuildCells(anchor, outward);
            if (cells.Count != HouseFootprintCells * HouseFootprintCells * HouseFootprintCells - 1)
            {
                return;
            }

            var moduleCenter = anchor + outward * 2;
            var volumeCells = GetRoomVolumeCells(anchor, outward);
            var tileChoices = SolveRoomTiles(cells, volumeCells, moduleCenter);
            if (tileChoices == null)
            {
                return;
            }

            foreach (var cell in volumeCells)
            {
                if (!IsInside(cell) ||
                    stone[cell.x, cell.y, cell.z] && !IsAllowedMountainEmbedCell(cell, anchor, outward))
                {
                    return;
                }
            }

            ClearEmbeddedMountainCells(volumeCells, anchor, outward);

            foreach (var cell in cells)
            {
                var isNewStructure = !structures.ContainsKey(cell);
                var kind = ClassifyInitialBuildCell(cell, anchor, outward);
                if (TrySetStructure(cell, kind, outward, true) && isNewStructure)
                {
                    buildingCells++;
                }

                BuildingTileChoice choice;
                if (tileChoices.TryGetValue(cell, out choice))
                {
                    PlaceBuildingTileInstance(cell, choice, moduleCenter + Vector3Int.up);
                }
            }

            foreach (var cell in volumeCells)
            {
                roomVolumeCells.Add(cell);
            }

            buildingModuleCenters.Add(moduleCenter);
            EnsureStructuralPillar(moduleCenter);
            RunLocalWfc();
        }

        private bool IsAllowedMountainEmbedCell(Vector3Int cell, Vector3Int anchor, Vector3Int outward)
        {
            var baseCell = anchor + outward;
            var offset = cell - baseCell;
            var depth = offset.x * outward.x + offset.y * outward.y + offset.z * outward.z;
            return depth >= 0 && depth < HouseMountainEmbedDepthCells;
        }

        private void ClearEmbeddedMountainCells(List<Vector3Int> volumeCells, Vector3Int anchor, Vector3Int outward)
        {
            foreach (var cell in volumeCells)
            {
                if (!IsStoneSolid(cell) || !IsAllowedMountainEmbedCell(cell, anchor, outward))
                {
                    continue;
                }

                stone[cell.x, cell.y, cell.z] = false;
                stoneCellCount--;
                MarkDirtyArea(cell, 1);
            }
        }

        private List<Vector3Int> GetBuildCells(Vector3Int anchor, Vector3 normal)
        {
            var outward = PrincipalHorizontal(normal);
            if (outward == Vector3Int.zero)
            {
                return new List<Vector3Int>(0);
            }

            return GetBuildCells(anchor, outward);
        }

        private List<Vector3Int> GetBuildCells(Vector3Int anchor, Vector3Int outward)
        {
            var volumeCells = GetRoomVolumeCells(anchor, outward);
            var center = anchor + outward * 2 + Vector3Int.up;
            volumeCells.Remove(center);
            return volumeCells;
        }

        private List<Vector3Int> GetRoomVolumeCells(Vector3Int anchor, Vector3Int outward)
        {
            var tangent = Mathf.Abs(outward.x) > 0 ? new Vector3Int(0, 0, 1) : new Vector3Int(1, 0, 0);
            var baseCell = anchor + outward;
            var cells = new List<Vector3Int>(HouseFootprintCells * HouseFootprintCells * HouseFootprintCells);

            for (var depth = 0; depth < HouseFootprintCells; depth++)
            {
                for (var width = -1; width <= 1; width++)
                {
                    for (var height = 0; height < HouseFootprintCells; height++)
                    {
                        cells.Add(baseCell + outward * depth + tangent * width + Vector3Int.up * height);
                    }
                }
            }

            return cells;
        }

        private StructureKind ClassifyInitialBuildCell(Vector3Int cell, Vector3Int anchor, Vector3Int outward)
        {
            var tangent = Mathf.Abs(outward.x) > 0 ? new Vector3Int(0, 0, 1) : new Vector3Int(1, 0, 0);
            var baseCell = anchor + outward;
            var offset = cell - baseCell;
            var frontOffset = Mathf.Abs(outward.x) > 0 ? offset.x * outward.x : offset.z * outward.z;
            var sideOffset = Mathf.Abs(tangent.x) > 0 ? offset.x * tangent.x : offset.z * tangent.z;

            return offset.y == 0 && frontOffset == 0 && sideOffset == 0 ? StructureKind.HouseDoorA1001 : StructureKind.HouseWallA1002;
        }

        private Dictionary<Vector3Int, BuildingTileChoice> SolveRoomTiles(List<Vector3Int> cells, List<Vector3Int> volumeCells, Vector3Int moduleCenter)
        {
            var result = new Dictionary<Vector3Int, BuildingTileChoice>();
            if (buildingTileCatalog == null || closedCubeTile == null)
            {
                return result;
            }

            var variants = BuildCubeTileVariants();
            var roomCenter = moduleCenter + Vector3Int.up;
            foreach (var cell in cells)
            {
                var exteriorClosedFaces = ExteriorClosedFaces(cell, roomCenter);
                var requiredOpenFaces = AllCubeFaces & ~exteriorClosedFaces;
                var candidates = new List<BuildingTileChoice>();
                foreach (var variant in variants)
                {
                    if (GetLogicalOpenFaces(variant.Definition, variant.Rotation) == requiredOpenFaces)
                    {
                        candidates.Add(variant);
                    }
                }

                if (candidates.Count == 0)
                {
                    Debug.LogWarning("No Cube prefab matches required open faces " + requiredOpenFaces + " for cell " + cell + ".", this);
                    return null;
                }

                result[cell] = PickStableTile(candidates, requiredOpenFaces);
            }

            return result;
        }

        private TileFace RequiredRoomOpenFaces(Vector3Int cell, Vector3Int roomCenter)
        {
            return AllCubeFaces & ~ExteriorClosedFaces(cell, roomCenter);
        }

        private TileFace ExteriorClosedFaces(Vector3Int cell, Vector3Int roomCenter)
        {
            var exteriorClosedFaces = TileFace.None;
            var offset = cell - roomCenter;
            if (offset.x < 0) exteriorClosedFaces |= TileFace.NegativeX;
            else if (offset.x > 0) exteriorClosedFaces |= TileFace.PositiveX;
            if (offset.y < 0) exteriorClosedFaces |= TileFace.NegativeY;
            else if (offset.y > 0) exteriorClosedFaces |= TileFace.PositiveY;
            if (offset.z < 0) exteriorClosedFaces |= TileFace.NegativeZ;
            else if (offset.z > 0) exteriorClosedFaces |= TileFace.PositiveZ;

            return exteriorClosedFaces;
        }

        private List<BuildingTileChoice> BuildCubeTileVariants()
        {
            var result = new List<BuildingTileChoice>();
            foreach (var definition in buildingTileCatalog.Definitions(TileLayer.Cube))
            {
                if (definition.CanonicalOpenFaces == TileFace.None)
                {
                    continue;
                }

                for (var rotation = 0; rotation < definition.RotationCount; rotation++)
                {
                    result.Add(new BuildingTileChoice(definition, rotation));
                }
            }

            return result;
        }

        private BuildingTileChoice PickStableTile(List<BuildingTileChoice> candidates, TileFace requiredOpenFaces)
        {
            candidates.Sort((a, b) =>
            {
                var familyCompare = TileFamilyScore(a.Definition, requiredOpenFaces).CompareTo(TileFamilyScore(b.Definition, requiredOpenFaces));
                if (familyCompare != 0) return familyCompare;
                var weightCompare = b.Definition.Weight.CompareTo(a.Definition.Weight);
                if (weightCompare != 0) return weightCompare;
                var nameCompare = string.CompareOrdinal(a.Definition.name, b.Definition.name);
                return nameCompare != 0 ? nameCompare : a.Rotation.CompareTo(b.Rotation);
            });
            return candidates[0];
        }

        private int TileFamilyScore(BuildingTileDefinition definition, TileFace requiredOpenFaces)
        {
            if (definition == null)
            {
                return 1000;
            }

            return Mathf.Abs(CountFaces(definition.CanonicalOpenFaces) - CountFaces(requiredOpenFaces));
        }

        private int CountFaces(TileFace faces)
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

        private TileFace GetLogicalOpenFaces(BuildingTileDefinition definition, int rotation)
        {
            return definition == null ? TileFace.None : definition.GetOpenFaces(rotation);
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
                    }
                }
            }

            RebuildShoreContacts();

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

        private void RebuildShoreContacts()
        {
            shoreContacts.Clear();
            const float waterlineHalfHeight = 1.15f;
            for (var x = 0; x < GridWidth; x++)
            {
                for (var y = 0; y < GridHeight; y++)
                {
                    for (var z = 0; z < GridDepth; z++)
                    {
                        var cell = new Vector3Int(x, y, z);
                        if (!IsOccupied(cell))
                        {
                            continue;
                        }

                        var center = CellToWorld(cell);
                        if (Mathf.Abs(center.y) > waterlineHalfHeight)
                        {
                            continue;
                        }

                        for (var faceIndex = 0; faceIndex < FaceDirections.Length; faceIndex++)
                        {
                            var direction = FaceDirections[faceIndex];
                            if (direction.y != 0 || IsOccupied(cell + direction))
                            {
                                continue;
                            }

                            var normal = ((Vector3)direction).normalized;
                            var contactCenter = center + normal * (CellSize * 0.56f);
                            contactCenter.y = 0f;
                            var seed = cell.x * 73856093 ^ cell.y * 19349663 ^ cell.z * 83492791 ^ faceIndex * 104729;
                            shoreContacts.Add(new ShoreContactSample(contactCenter, normal, cell, seed));
                        }
                    }
                }
            }
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
            var raised = seed > 0.84f || FaceDirections[faceIndex] == Vector3Int.up;
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

            AddFlatPanel(center, normal, tangentA, tangentB, width, height, seed > 0.94f ? sunStoneTriangles : stoneTriangles);

            if (faceIndex != 2 && seed > 0.90f)
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
            StructureKind kind;
            structures.TryGetValue(cell, out kind);

            if (kind == StructureKind.ColumnA3100)
            {
                AddSupportColumnRelief(cell, faceIndex, normal, tangentA, tangentB);
                return;
            }

            if (kind == StructureKind.PlatformA0100)
            {
                AddFlatPanel(center, normal, tangentA, tangentB, Mathf.Lerp(0.82f, 0.98f, seed), Mathf.Lerp(0.16f, 0.24f, 1f - seed), buildingTriangles);
                return;
            }

            AddFlatPanel(center, normal, tangentA, tangentB, Mathf.Lerp(0.72f, 0.94f, seed), Mathf.Lerp(0.72f, 0.96f, 1f - seed), buildingTriangles);

            if (kind == StructureKind.HouseDoorA1001 && IsDoorFace(cell, FaceDirections[faceIndex]))
            {
                AddFlatPanel(center + normal * 0.045f - tangentB * 0.16f, normal, tangentA, tangentB, 0.34f, 0.58f, stoneTriangles);
                return;
            }

            if (faceIndex != 2 && seed > 0.38f)
            {
                AddFlatPanel(center + normal * 0.035f + tangentB * 0.12f, normal, tangentA, tangentB, 0.22f, 0.28f, stoneTriangles);
            }
        }

        private void AddSupportColumnRelief(Vector3Int cell, int faceIndex, Vector3 normal, Vector3 tangentA, Vector3 tangentB)
        {
            var center = CellToWorld(cell) + normal * (CellSize * 0.58f);
            AddFlatPanel(center, normal, tangentA, tangentB, 0.36f, 0.86f, buildingTriangles);
            if (faceIndex == 2)
            {
                AddFlatPanel(center + normal * 0.035f, normal, tangentA, tangentB, 0.70f, 0.70f, sunStoneTriangles);
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

        private bool TrySetStructure(Vector3Int cell, StructureKind kind, Vector3Int facing, bool overwrite)
        {
            if (!IsInside(cell) || stone[cell.x, cell.y, cell.z])
            {
                return false;
            }

            if (structures.ContainsKey(cell) && !overwrite)
            {
                return false;
            }

            structures[cell] = kind;
            if (facing != Vector3Int.zero)
            {
                structureFacing[cell] = facing;
            }

            MarkDirtyArea(cell, LocalWfcRadius);
            return true;
        }

        private void MarkDirtyArea(Vector3Int center, int radius)
        {
            for (var x = center.x - radius; x <= center.x + radius; x++)
            {
                for (var y = center.y - radius; y <= center.y + radius; y++)
                {
                    for (var z = center.z - radius; z <= center.z + radius; z++)
                    {
                        var cell = new Vector3Int(x, y, z);
                        if (IsInside(cell))
                        {
                            dirtyStructures.Add(cell);
                        }
                    }
                }
            }
        }

        private void RunLocalWfc()
        {
            for (var pass = 0; pass < 6 && dirtyStructures.Count > 0; pass++)
            {
                var dirtySnapshot = new List<Vector3Int>(dirtyStructures);
                dirtyStructures.Clear();

                for (var i = 0; i < dirtySnapshot.Count; i++)
                {
                    PropagateCellRules(dirtySnapshot[i]);
                }
            }
        }

        private void PropagateCellRules(Vector3Int cell)
        {
            StructureKind kind;
            if (!structures.TryGetValue(cell, out kind))
            {
                return;
            }

            var rule = GetStructureRule(kind);
            _ = rule;
        }

        private void EnsureStructuralPillar(Vector3Int moduleCenter)
        {
            Vector3Int assignedPillar;
            if (!NeedsStructuralPillar(moduleCenter, out assignedPillar))
            {
                if (assignedPillar.x > -900)
                {
                    roomPillarAssignments[moduleCenter] = assignedPillar;
                }
                return;
            }

            structuralPillarCenters.Add(moduleCenter);
            roomPillarAssignments[moduleCenter] = moduleCenter;
            for (var y = moduleCenter.y - 1; y >= 0; y--)
            {
                var support = new Vector3Int(moduleCenter.x, y, moduleCenter.z);
                if (!IsInside(support) || IsStoneSolid(support))
                {
                    break;
                }

                StructureKind existing;
                if (structures.TryGetValue(support, out existing))
                {
                    // A column may merge into a previous column. Any other structure is a valid top surface.
                    if (existing != StructureKind.ColumnA3100)
                    {
                        break;
                    }
                    PlaceSupportTileInstance(support);
                    continue;
                }

                if (TrySetStructure(support, StructureKind.ColumnA3100, Vector3Int.zero, false))
                {
                    PlaceSupportTileInstance(support);
                }
            }
        }

        private bool NeedsStructuralPillar(Vector3Int moduleCenter, out Vector3Int assignedPillar)
        {
            assignedPillar = new Vector3Int(-999, -999, -999);
            var unsupportedFloorCells = 0;
            for (var x = -1; x <= 1; x++)
            {
                for (var z = -1; z <= 1; z++)
                {
                    if (!IsOccupied(moduleCenter + new Vector3Int(x, -1, z)))
                    {
                        unsupportedFloorCells++;
                    }
                }
            }

            // Up to four unsupported floor cells are treated as a safe partial overhang.
            if (unsupportedFloorCells < 5)
            {
                return false;
            }

            var nearestPillar = new Vector3Int(-999, -999, -999);
            var nearestDistance = int.MaxValue;
            foreach (var pillar in structuralPillarCenters)
            {
                if (Mathf.Abs(pillar.y - moduleCenter.y) > HouseFootprintCells)
                {
                    continue;
                }

                var distance = Mathf.Max(Mathf.Abs(pillar.x - moduleCenter.x), Mathf.Abs(pillar.z - moduleCenter.z));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestPillar = pillar;
                }
            }

            if (nearestDistance > PillarCoverageCells)
            {
                return true;
            }

            assignedPillar = nearestPillar;
            var roomsOnNearestPillar = 0;
            foreach (var assignment in roomPillarAssignments)
            {
                if (assignment.Value == nearestPillar)
                {
                    roomsOnNearestPillar++;
                }
            }

            return roomsOnNearestPillar >= PillarRoomCapacity;
        }

        private void EnsureDoorConnection(Vector3Int cell)
        {
            Vector3Int facing;
            if (!structureFacing.TryGetValue(cell, out facing) || facing == Vector3Int.zero)
            {
                return;
            }

            var front = cell + facing;
            if (IsInside(front) && !stone[front.x, front.y, front.z] && !IsDoorTarget(front))
            {
                TrySetStructure(front, StructureKind.PlatformA0100, facing, true);
            }
        }

        private bool IsDoorTarget(Vector3Int cell)
        {
            StructureKind kind;
            if (!structures.TryGetValue(cell, out kind))
            {
                return false;
            }

            return kind == StructureKind.PlatformA0100 || kind == StructureKind.HouseDoorA1001;
        }

        private void EnsureWallContinuity(Vector3Int cell)
        {
            if (HasHorizontalArchitectureNeighbor(cell))
            {
                return;
            }

            Vector3Int facing;
            if (!structureFacing.TryGetValue(cell, out facing) || facing == Vector3Int.zero)
            {
                return;
            }

            var landing = cell + facing;
            if (IsInside(landing) && !stone[landing.x, landing.y, landing.z])
            {
                TrySetStructure(landing, StructureKind.PlatformA0100, facing, false);
            }
        }

        private bool HasHorizontalArchitectureNeighbor(Vector3Int cell)
        {
            for (var i = 0; i < FaceDirections.Length; i++)
            {
                var direction = FaceDirections[i];
                if (direction.y != 0)
                {
                    continue;
                }

                var neighbor = cell + direction;
                if (structures.ContainsKey(neighbor) || IsStoneSolid(neighbor))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsDoorFace(Vector3Int cell, Vector3Int faceDirection)
        {
            Vector3Int facing;
            return structureFacing.TryGetValue(cell, out facing) && facing == faceDirection;
        }

        private StructureRule GetStructureRule(StructureKind kind)
        {
            switch (kind)
            {
                case StructureKind.PlatformA0100:
                    return new StructureRule("A0100", ConnectorFlags.GroundConnect | ConnectorFlags.WallConnect | ConnectorFlags.VerticalSupport, 80);
                case StructureKind.HouseDoorA1001:
                    return new StructureRule("A1001", ConnectorFlags.GroundConnect | ConnectorFlags.WallConnect | ConnectorFlags.Door, 100);
                case StructureKind.HouseWallA1002:
                    return new StructureRule("A1002", ConnectorFlags.GroundConnect | ConnectorFlags.WallConnect, 70);
                default:
                    return new StructureRule("A3100", ConnectorFlags.VerticalSupport, 90);
            }
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

        private float SmoothRange(float edge0, float edge1, float value)
        {
            var t = Mathf.Clamp01((value - edge0) / Mathf.Max(Mathf.Abs(edge1 - edge0), 0.0001f) * Mathf.Sign(edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private bool IsStoneSolid(Vector3Int cell)
        {
            return IsInside(cell) && stone[cell.x, cell.y, cell.z];
        }

        private bool IsOccupied(Vector3Int cell)
        {
            return IsStoneSolid(cell) || structures.ContainsKey(cell);
        }

        private void PlaceBuildingTileInstance(Vector3Int cell, BuildingTileChoice choice, Vector3Int roomCenter)
        {
            RemoveBuildingTileInstance(cell);
            buildingTileOpenFaces[cell] = GetLogicalOpenFaces(choice.Definition, choice.Rotation);
            if (suppressTileInstantiation && !Application.isPlaying)
            {
                buildingTileInstances[cell] = null;
                return;
            }

            if (choice.Definition == null || buildingTileRoot == null)
            {
                return;
            }

            var instance = Instantiate(choice.Definition.gameObject, buildingTileRoot);
            instance.SetActive(true);
            instance.name = choice.Definition.name + "_r" + choice.Rotation + "_" + cell.x + "_" + cell.y + "_" + cell.z;
            instance.transform.localPosition = CellToWorld(roomCenter) + (Vector3)(cell - roomCenter) * BuildingTileVisualCellSize;
            instance.transform.localRotation = choice.Definition.GetRotation(choice.Rotation);
            instance.transform.localScale *= BuildingTileVisualScale;
            ApplyBuildingTileMaterial(instance);
            buildingTileInstances[cell] = instance;
        }

        private void PlaceSupportTileInstance(Vector3Int cell)
        {
            RemoveSupportTileInstance(cell);
            supportTileInstances[cell] = null;
        }

        private void NormalizeTileInstanceBounds(GameObject instance, Vector3 cellCenter)
        {
            Bounds bounds;
            if (!TryGetRendererBounds(instance, out bounds))
            {
                return;
            }

            var largestSize = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (largestSize > 0.0001f)
            {
                var scale = (CellSize * 0.98f) / largestSize;
                instance.transform.localScale *= scale;
            }

            if (!TryGetRendererBounds(instance, out bounds))
            {
                return;
            }

            instance.transform.position += cellCenter - bounds.center;
        }

        private bool TryGetRendererBounds(GameObject instance, out Bounds bounds)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            bounds = new Bounds(instance.transform.position, Vector3.zero);
            var found = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return found;
        }

        private void ApplyBuildingTileMaterial(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(false);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].enabled)
                {
                    continue;
                }

                if (buildingTileMaterial != null)
                {
                    var materials = renderers[i].sharedMaterials;
                    if (materials == null || materials.Length == 0)
                    {
                        renderers[i].sharedMaterial = buildingTileMaterial;
                    }
                    else
                    {
                        for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                        {
                            materials[materialIndex] = buildingTileMaterial;
                        }

                        renderers[i].sharedMaterial = buildingTileMaterial;
                        renderers[i].sharedMaterials = materials;
                    }
                }

                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderers[i].receiveShadows = true;
            }
        }

        private void RemoveBuildingTileInstance(Vector3Int cell)
        {
            GameObject instance;
            if (buildingTileInstances.TryGetValue(cell, out instance) && instance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(instance);
                }
                else
                {
                    DestroyImmediate(instance);
                }
            }

            buildingTileInstances.Remove(cell);
            buildingTileOpenFaces.Remove(cell);
        }

        private void RemoveSupportTileInstance(Vector3Int cell)
        {
            GameObject instance;
            if (supportTileInstances.TryGetValue(cell, out instance) && instance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(instance);
                }
                else
                {
                    DestroyImmediate(instance);
                }
            }

            supportTileInstances.Remove(cell);
        }

        private void ClearBuildingTileInstances()
        {
            foreach (var pair in buildingTileInstances)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(pair.Value);
                }
                else
                {
                    DestroyImmediate(pair.Value);
                }
            }

            buildingTileInstances.Clear();
            buildingTileOpenFaces.Clear();
        }

        private void ClearSupportTileInstances()
        {
            foreach (var pair in supportTileInstances)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(pair.Value);
                }
                else
                {
                    DestroyImmediate(pair.Value);
                }
            }

            supportTileInstances.Clear();
        }

        private bool IsInside(Vector3Int cell)
        {
            return cell.x >= 0 && cell.x < GridWidth &&
                   cell.y >= 0 && cell.y < GridHeight &&
                   cell.z >= 0 && cell.z < GridDepth;
        }

        private TileFace TileFaceFromDirection(Vector3Int direction)
        {
            if (direction == Vector3Int.right) return TileFace.PositiveX;
            if (direction == Vector3Int.left) return TileFace.NegativeX;
            if (direction == Vector3Int.up) return TileFace.PositiveY;
            if (direction == Vector3Int.down) return TileFace.NegativeY;
            if (direction.z > 0) return TileFace.PositiveZ;
            return TileFace.NegativeZ;
        }

        private Vector3 CellToWorld(Vector3Int cell)
        {
            return new Vector3(
                (cell.x - (GridWidth - 1) * 0.5f) * CellSize,
                cell.y * CellSize + CellSize * 0.5f - PillarWaterlineDrop,
                (cell.z - (GridDepth - 1) * 0.5f) * CellSize);
        }

        private Vector3Int WorldToCell(Vector3 world)
        {
            return new Vector3Int(
                Mathf.RoundToInt(world.x / CellSize + (GridWidth - 1) * 0.5f),
                Mathf.FloorToInt((world.y + PillarWaterlineDrop) / CellSize),
                Mathf.RoundToInt(world.z / CellSize + (GridDepth - 1) * 0.5f));
        }

        private Vector3Int PrincipalHorizontal(Vector3 normal)
        {
            var horizontal = new Vector2(normal.x, normal.z);
            if (horizontal.magnitude < 0.35f || Mathf.Abs(normal.y) > horizontal.magnitude * 1.35f)
            {
                return Vector3Int.zero;
            }

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
            return stoneCellCount;
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
