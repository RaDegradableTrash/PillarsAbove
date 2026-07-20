using System.Collections.Generic;
using UnityEngine;

namespace PillarsAbove
{
    [DisallowMultipleComponent]
    public sealed class WaterGridGenerator : MonoBehaviour
    {
        internal const float InteractiveRippleDisplacement = 0.65f;
        internal const float InteractiveRippleNormalStrength = 1f;
        internal const float PillarWaterEdgeSink = 0.04f;

        [SerializeField] private Color waterColor = new Color(0.08f, 0.42f, 0.62f, 1f);
        [SerializeField] private Color waterGridColor = new Color(0.04f, 0.16f, 0.24f, 1f);
        [SerializeField, Min(0.001f)] private float waterGridLineWidth = 0.016f;
        [SerializeField] private Color hoverGridColor = new Color(1f, 1f, 1f, 0.45f);
        [SerializeField, Min(0.001f)] private float hoverGridLineWidth = 0.012f;
        [SerializeField, Min(0f)] private float hoverGridYOffset = 0.006f;
        [SerializeField, Min(0.05f)] private float hoverRevealRadius = 0.8f;
        [SerializeField, Min(0.01f)] private float hoverFadeWidth = 1.9f;
        [SerializeField, Min(1f)] private float hoverRaycastDistance = 1000f;
        [SerializeField, Min(0f)] private float waveAmplitude = 0.22f;
        [SerializeField, Min(0.01f)] private float waveSpeed = 0.72f;
        [SerializeField, Min(0.1f)] private float primaryWaveLength = 7.5f;
        [SerializeField, Min(0.1f)] private float secondaryWaveLength = 3.2f;
        [SerializeField, Range(0f, 1f)] private float gerstnerSteepness = 0.48f;
        [SerializeField, Range(0f, 1f)] private float realtimeShadowStrength = 0.82f;

        public List<Quad> WaterQuads { get; private set; } = new List<Quad>();

        private PillarGridGenerator pillarGrid;
        private Mesh waterMesh;
        private Material waterMaterial;
        private Material waterGridMaterial;
        private Material hoverGridMaterial;
        private GameObject waterSurface;
        private GameObject waterGridLines;
        private GameObject hoverGridLines;
        private Mesh waterGridMesh;
        private Mesh hoverGridMesh;
        private GameObject waterSprayTrigger;
        private ShallowWaterSimulation shallowWaterSimulation;

        public TownscaperGridData GenerateGlobalTownscaperGrid(
            int nodesPerRing,
            int levelCount,
            float yMin,
            float yMax,
            float dissolutionProbability,
            int randomSeed,
            int relaxationIterations,
            float relaxationStrength)
        {
            return TownscaperGridTopology.Generate(
                nodesPerRing,
                levelCount,
                yMin,
                yMax,
                dissolutionProbability,
                randomSeed,
                relaxationIterations,
                relaxationStrength);
        }

        public void GenerateFrom(PillarGridGenerator pillarGrid, List<Quad> waterQuads)
        {
            this.pillarGrid = pillarGrid;
            WaterQuads.Clear();
            WaterQuads.AddRange(waterQuads);
            if (waterSurface != null)
            {
                PillarGridGenerator.DestroyRuntimeObject(waterSurface);
            }
            if (waterGridLines != null)
            {
                PillarGridGenerator.DestroyRuntimeObject(waterGridLines);
            }
            if (hoverGridLines != null)
            {
                PillarGridGenerator.DestroyRuntimeObject(hoverGridLines);
            }
            if (waterSprayTrigger != null)
            {
                PillarGridGenerator.DestroyRuntimeObject(waterSprayTrigger);
            }
            PillarGridGenerator.DestroyRuntimeObject(waterMesh);
            PillarGridGenerator.DestroyRuntimeObject(waterGridMesh);
            PillarGridGenerator.DestroyRuntimeObject(hoverGridMesh);

            waterSurface = new GameObject("Planar Organic Water Quads", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(GeneratedGridClickSurface));
            waterSurface.transform.SetParent(pillarGrid.GetGeneratedRoot(), false);
            waterMesh = pillarGrid.BuildWaterQuadMesh(WaterQuads, "Planar Townscaper Water Grid");
            waterSurface.GetComponent<MeshFilter>().sharedMesh = waterMesh;
            waterSurface.GetComponent<MeshCollider>().sharedMesh = waterMesh;
            waterSurface.GetComponent<GeneratedGridClickSurface>().Initialize(pillarGrid, false);
            if (waterMaterial == null)
            {
                waterMaterial = CreateWaterEffectsMaterial("Generated Organic Water Effects");
            }
            var waterRenderer = waterSurface.GetComponent<MeshRenderer>();
            waterRenderer.sharedMaterial = waterMaterial;
            waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            shallowWaterSimulation = waterSurface.AddComponent<ShallowWaterSimulation>();
            ConfigureWaterSurfaceMaterial(waterMaterial);

            waterGridLines = new GameObject("Organic Water Grid Lines", typeof(MeshFilter), typeof(MeshRenderer));
            waterGridLines.transform.SetParent(pillarGrid.GetGeneratedRoot(), false);
            waterGridMesh = pillarGrid.BuildWaterGridLineMesh(
                WaterQuads,
                waterGridLineWidth,
                0.012f,
                "Organic Water Grid Lines");
            pillarGrid.ValidateWaterGridLineCoverage(waterGridMesh);
            waterGridLines.GetComponent<MeshFilter>().sharedMesh = waterGridMesh;
            if (waterGridMaterial == null)
            {
                waterGridMaterial = PillarGridGenerator.CreateHighlightGridMaterial("Water Grid Lines", waterGridColor, true);
            }
            ConfigureWaterLineMaterial(waterGridMaterial);
            var waterGridRenderer = waterGridLines.GetComponent<MeshRenderer>();
            waterGridRenderer.sharedMaterial = waterGridMaterial;
            waterGridRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterGridRenderer.receiveShadows = false;
            waterGridLines.SetActive(false);

            if (hoverGridMaterial == null)
            {
                hoverGridMaterial = PillarGridGenerator.CreateHighlightGridMaterial("Water Hover Glow Grid Lines", hoverGridColor, true);
            }
            ConfigureWaterHighlightMaterial();

            hoverGridLines = new GameObject("Water Hover Glow Grid Lines", typeof(MeshFilter), typeof(MeshRenderer));
            hoverGridLines.transform.SetParent(pillarGrid.GetGeneratedRoot(), false);
            hoverGridMesh = pillarGrid.BuildWaterGridLineMesh(
                WaterQuads,
                hoverGridLineWidth,
                hoverGridYOffset,
                "Water Hover Glow Grid Lines");
            hoverGridLines.GetComponent<MeshFilter>().sharedMesh = hoverGridMesh;
            var hoverGridRenderer = hoverGridLines.GetComponent<MeshRenderer>();
            hoverGridRenderer.sharedMaterial = hoverGridMaterial;
            hoverGridRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hoverGridRenderer.receiveShadows = false;
            hoverGridLines.SetActive(false);

            CreateSprayTrigger(pillarGrid);
        }

        private void Update()
        {
            RefreshHoverGridMaterial();
        }

        public void RefreshHoverGridMaterial()
        {
            ConfigureWaterSurfaceMaterial(waterMaterial);
            ConfigureWaterLineMaterial(waterGridMaterial);
            ConfigureWaterHighlightMaterial();
        }

        public void RefreshDynamicWaterBindings(Material pillarSurfaceMaterial, Material pillarHighlightMaterial, float waterLevel)
        {
            ConfigureWaterSurfaceMaterial(waterMaterial);
            ConfigureWaterLineMaterial(waterGridMaterial);
            ConfigureWaterHighlightMaterial();
            ConfigurePillarSurfaceWater(pillarSurfaceMaterial, waterLevel);
            ConfigurePillarHighlightClip(pillarHighlightMaterial, waterLevel);
        }

        public void ConfigurePillarHighlightClip(Material material, float waterLevel)
        {
            if (material == null)
            {
                return;
            }

            ApplyWaveParameters(material);
            material.SetFloat("_WaterLevel", waterLevel);
            material.SetFloat("_ClipBelowDynamicWater", 1f);
            material.SetFloat("_WaterClipSoftness", 0.006f);
            material.SetFloat("_UseClipRipple", 1f);
            material.SetFloat("_AnchorBottomToDynamicWater", 1f);
            material.SetFloat("_DynamicWaterAnchorRange", 0.08f);
            material.SetFloat("_DynamicWaterAnchorOffset", hoverGridYOffset);
            material.SetFloat("_Displacement", InteractiveRippleDisplacement);
            material.SetFloat("_NormalStrength", InteractiveRippleNormalStrength);
            material.SetFloat("_ShadowStrength", realtimeShadowStrength);
            ApplyWaterUvBounds(material);
            if (shallowWaterSimulation != null && shallowWaterSimulation.CurrentSimulationTexture != null)
            {
                material.SetTexture("_SimulationTex", shallowWaterSimulation.CurrentSimulationTexture);
            }
        }

        public void ConfigurePillarSurfaceWater(Material material, float waterLevel)
        {
            if (material == null)
            {
                return;
            }

            ApplyWaveParameters(material);
            material.SetFloat("_DynamicWaterClip", 1f);
            material.SetFloat("_WaterLevel", waterLevel);
            material.SetFloat("_WaterEdgeSink", PillarWaterEdgeSink);
            material.SetFloat("_Displacement", InteractiveRippleDisplacement);
            material.SetFloat("_NormalStrength", InteractiveRippleNormalStrength);
            ApplyWaterUvBounds(material);
            if (shallowWaterSimulation != null && shallowWaterSimulation.CurrentSimulationTexture != null)
            {
                material.SetTexture("_SimulationTex", shallowWaterSimulation.CurrentSimulationTexture);
            }
        }

        public bool TryRaycast(Ray ray, out RaycastHit hit)
        {
            hit = default(RaycastHit);
            if (waterSurface == null)
            {
                return false;
            }

            var collider = waterSurface.GetComponent<MeshCollider>();
            return collider != null && collider.Raycast(ray, out hit, hoverRaycastDistance);
        }

        public void ShowGlobalHoverGrid()
        {
            if (hoverGridLines == null || hoverGridMaterial == null)
            {
                return;
            }

            PillarGridGenerator.SetHighlightMaterial(hoverGridMaterial, Vector3.zero, 10000f, 1000f, 1f);
            hoverGridLines.SetActive(true);
        }

        public void ShowHoverGrid(Vector3 center)
        {
            if (pillarGrid == null || waterSurface == null || hoverGridLines == null || hoverGridMaterial == null)
            {
                return;
            }

            ConfigureWaterHighlightMaterial();
            PillarGridGenerator.SetHighlightMaterial(
                hoverGridMaterial,
                center,
                hoverRevealRadius,
                hoverFadeWidth,
                1f);
            hoverGridLines.SetActive(true);
        }

        public void HideHoverGrid()
        {
            if (hoverGridLines != null)
            {
                hoverGridLines.SetActive(false);
            }
        }

        private Material CreateWaterEffectsMaterial(string materialName)
        {
            var shader = Shader.Find("PillarsAbove/OceanFoam") ?? Shader.Find("PillarsAbove/ShallowWaterSurface") ?? Shader.Find("Standard");
            var opaqueWaterColor = waterColor;
            opaqueWaterColor.a = 1f;
            var material = new Material(shader) { name = materialName, color = opaqueWaterColor };
            material.SetColor("_Color", opaqueWaterColor);
            material.SetColor("_BaseColor", opaqueWaterColor);

            if (shader != null && shader.name == "PillarsAbove/OceanFoam")
            {
                material.SetColor("_ShallowColor", new Color(0.035f, 0.17f, 0.25f, 1f));
                material.SetColor("_MidColor", new Color(0.04f, 0.34f, 0.48f, 1f));
                material.SetColor("_DeepColor", new Color(0.02f, 0.12f, 0.22f, 1f));
                material.SetColor("_HorizonColor", new Color(0.42f, 0.58f, 0.64f, 1f));
                material.SetColor("_FarWaterColor", new Color(0.50f, 0.56f, 0.58f, 1f));
                material.SetColor("_DiffuseTint", new Color(0.14f, 0.22f, 0.25f, 1f));
                material.SetColor("_ShoreRippleColor", new Color(0.54f, 0.68f, 0.70f, 1f));
                material.SetColor("_FoamColor", new Color(0.76f, 0.83f, 0.82f, 1f));
                material.SetFloat("_Alpha", 1f);
                material.SetFloat("_WaveAmplitude", waveAmplitude);
                material.SetFloat("_WaveSpeed", waveSpeed);
                material.SetFloat("_PrimaryWaveLength", primaryWaveLength);
                material.SetFloat("_SecondaryWaveLength", secondaryWaveLength);
                material.SetFloat("_GerstnerSteepness", gerstnerSteepness);
                material.SetFloat("_MicroRippleStrength", 0.24f);
                material.SetFloat("_ShoreFoamDistance", 1.6f);
                material.SetFloat("_ShoreFoamStrength", 0.95f);
                material.SetFloat("_CrestFoamThreshold", 0.64f);
                material.SetFloat("_CrestFoamWidth", 0.24f);
                material.SetFloat("_CrestFoamStrength", 0.62f);
                material.SetFloat("_FoamScale", 0.62f);
                material.SetFloat("_FoamDrift", 0.5f);
                material.SetFloat("_Smoothness", 96f);
                material.SetFloat("_SunGlitterStrength", 0.5f);
                material.SetFloat("_ReflectionStrength", 0.42f);
                material.SetFloat("_DiffuseStrength", 0.34f);
                material.SetFloat("_FresnelPower", 3.2f);
                material.SetFloat("_DepthColorRange", 7f);
                material.SetFloat("_FarFogStart", 60f);
                material.SetFloat("_FarFogEnd", 220f);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 20;
            }

            return material;
        }

        private void ConfigureWaterSurfaceMaterial(Material material)
        {
            if (material == null) return;
            ApplyWaveParameters(material);
            material.SetFloat("_Displacement", InteractiveRippleDisplacement);
            material.SetFloat("_NormalStrength", InteractiveRippleNormalStrength);
            material.SetFloat("_ShadowStrength", realtimeShadowStrength);
            ApplyWaterUvBounds(material);
            if (shallowWaterSimulation != null && shallowWaterSimulation.CurrentSimulationTexture != null)
            {
                material.SetTexture("_SimulationTex", shallowWaterSimulation.CurrentSimulationTexture);
            }
        }

        private void ConfigureWaterHighlightMaterial()
        {
            if (hoverGridMaterial == null) return;
            ConfigureWaterLineMaterial(hoverGridMaterial);
        }

        private void ConfigureWaterLineMaterial(Material material)
        {
            if (material == null) return;
            ApplyWaveParameters(material);
            material.SetFloat("_WaterMode", 1f);
            material.SetFloat("_WaterLevel", GetWorldWaterLevel());
            material.SetFloat("_ClipBelowDynamicWater", 0f);
            material.SetFloat("_UseClipRipple", 0f);
            material.SetFloat("_AnchorBottomToDynamicWater", 0f);
            material.SetFloat("_Displacement", InteractiveRippleDisplacement);
            material.SetFloat("_NormalStrength", InteractiveRippleNormalStrength);
            ApplyWaterUvBounds(material);
            if (shallowWaterSimulation != null && shallowWaterSimulation.CurrentSimulationTexture != null)
            {
                material.SetTexture("_SimulationTex", shallowWaterSimulation.CurrentSimulationTexture);
            }
        }

        private void ApplyWaveParameters(Material material)
        {
            material.SetFloat("_WaveAmplitude", waveAmplitude);
            material.SetFloat("_WaveSpeed", waveSpeed);
            material.SetFloat("_PrimaryWaveLength", primaryWaveLength);
            material.SetFloat("_SecondaryWaveLength", secondaryWaveLength);
            material.SetFloat("_GerstnerSteepness", gerstnerSteepness);
        }

        private float GetWorldWaterLevel()
        {
            return pillarGrid != null
                ? pillarGrid.transform.TransformPoint(new Vector3(0f, pillarGrid.WaterHeight, 0f)).y
                : 0f;
        }

        private void ApplyWaterUvBounds(Material material)
        {
            if (waterSurface == null || waterMesh == null)
            {
                return;
            }

            var bounds = waterMesh.bounds;
            var corners = new[]
            {
                waterSurface.transform.TransformPoint(new Vector3(bounds.min.x, bounds.center.y, bounds.min.z)),
                waterSurface.transform.TransformPoint(new Vector3(bounds.min.x, bounds.center.y, bounds.max.z)),
                waterSurface.transform.TransformPoint(new Vector3(bounds.max.x, bounds.center.y, bounds.min.z)),
                waterSurface.transform.TransformPoint(new Vector3(bounds.max.x, bounds.center.y, bounds.max.z))
            };
            var minX = corners[0].x;
            var maxX = corners[0].x;
            var minZ = corners[0].z;
            var maxZ = corners[0].z;
            for (var corner = 1; corner < corners.Length; corner++)
            {
                minX = Mathf.Min(minX, corners[corner].x);
                maxX = Mathf.Max(maxX, corners[corner].x);
                minZ = Mathf.Min(minZ, corners[corner].z);
                maxZ = Mathf.Max(maxZ, corners[corner].z);
            }

            material.SetVector("_WaterUvMin", new Vector4(minX, minZ, 0f, 0f));
            material.SetVector("_WaterUvSize", new Vector4(Mathf.Max(0.001f, maxX - minX), Mathf.Max(0.001f, maxZ - minZ), 0f, 0f));
        }

        private void CreateSprayTrigger(PillarGridGenerator pillarGrid)
        {
            if (waterMesh == null) return;

            waterSprayTrigger = new GameObject("Water Contact Spray Trigger", typeof(BoxCollider), typeof(Rigidbody), typeof(ProceduralWaveSpraySystem));
            waterSprayTrigger.transform.SetParent(pillarGrid.GetGeneratedRoot(), false);
            var box = waterSprayTrigger.GetComponent<BoxCollider>();
            var bounds = waterMesh.bounds;
            box.center = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
            box.size = new Vector3(Mathf.Max(0.1f, bounds.size.x), Mathf.Max(1.2f, waveAmplitude * 8f), Mathf.Max(0.1f, bounds.size.z));
            box.isTrigger = true;

            var body = waterSprayTrigger.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var spray = waterSprayTrigger.GetComponent<ProceduralWaveSpraySystem>();
            spray.ConfigureWaveSource(
                waterSurface.GetComponent<MeshFilter>(),
                waterSurface.transform,
                pillarGrid.WaterHeight,
                Mathf.Max(0.05f, waveAmplitude),
                primaryWaveLength,
                waveSpeed,
                4);
        }

        private void OnDestroy()
        {
            if (waterSprayTrigger != null)
            {
                PillarGridGenerator.DestroyRuntimeObject(waterSprayTrigger);
            }
            PillarGridGenerator.DestroyRuntimeObject(waterMesh);
            PillarGridGenerator.DestroyRuntimeObject(waterGridMesh);
            PillarGridGenerator.DestroyRuntimeObject(hoverGridMesh);
            PillarGridGenerator.DestroyRuntimeObject(waterMaterial);
            PillarGridGenerator.DestroyRuntimeObject(waterGridMaterial);
            PillarGridGenerator.DestroyRuntimeObject(hoverGridMaterial);
        }
    }
}
