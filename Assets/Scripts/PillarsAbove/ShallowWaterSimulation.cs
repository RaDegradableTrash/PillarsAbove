using System.Collections.Generic;
using UnityEngine;

namespace PillarsAbove
{
    /// <summary>
    /// GPU height-field water simulation backed by two ping-pong render textures.
    /// R stores the current height and G stores the height from the previous step.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public sealed class ShallowWaterSimulation : MonoBehaviour
    {
        [Header("Simulation")]
        [SerializeField, Min(32)] private int textureResolution = 256;
        [SerializeField, Range(15f, 240f)] private float simulationFrequency = 60f;
        [SerializeField, Range(0.9f, 1f)] private float damping = 0.995f;
        [SerializeField, Range(1, 16)] private int maximumStepsPerFrame = 8;

        [Header("Surface")]
        [SerializeField] private Renderer waterRenderer;
        [SerializeField] private Material waterSurfaceMaterial;
        [SerializeField, Min(0f)] private float displacement = 1f;
        [SerializeField, Min(0f)] private float normalStrength = 1f;

        [Header("Collision Pulses")]
        [SerializeField, Min(0.001f)] private float defaultPulseRadius = 0.75f;
        [SerializeField] private float defaultPulseHeight = 0.5f;
        [SerializeField, Min(0f)] private float impactHeightMultiplier = 0.08f;
        [SerializeField, Min(0f)] private float maximumImpactHeight = 1.5f;
        [SerializeField] private LayerMask collisionLayers = ~0;

        [Header("Shaders (optional overrides)")]
        [SerializeField] private Shader simulationShader;
        [SerializeField] private Shader inputShader;

        private static readonly int DampingId = Shader.PropertyToID("_Damping");
        private static readonly int PulseUvId = Shader.PropertyToID("_PulseUV");
        private static readonly int PulseRadiusUvId = Shader.PropertyToID("_PulseRadiusUV");
        private static readonly int PulseHeightId = Shader.PropertyToID("_PulseHeight");
        private static readonly int SimulationTexId = Shader.PropertyToID("_SimulationTex");
        private static readonly int DisplacementId = Shader.PropertyToID("_Displacement");
        private static readonly int NormalStrengthId = Shader.PropertyToID("_NormalStrength");

        private readonly List<Pulse> queuedPulses = new List<Pulse>(8);
        private RenderTexture bufferA;
        private RenderTexture bufferB;
        private RenderTexture current;
        private RenderTexture next;
        private Material simulationMaterial;
        private Material inputMaterial;
        private MaterialPropertyBlock surfaceProperties;
        private MeshFilter meshFilter;
        private float accumulatedTime;

        private struct Pulse
        {
            public Vector3 WorldPosition;
            public float Radius;
            public float Height;
        }

        public RenderTexture CurrentSimulationTexture => current;
        public RenderTexture BufferA => bufferA;
        public RenderTexture BufferB => bufferB;

        private void Awake()
        {
            if (waterRenderer == null)
            {
                waterRenderer = GetComponent<Renderer>();
            }

            meshFilter = GetComponent<MeshFilter>();
            CreateResources();
        }

        private void OnEnable()
        {
            if (current == null)
            {
                CreateResources();
            }

            PublishSurfaceTexture();
        }

        private void Update()
        {
            if (current == null || simulationMaterial == null || inputMaterial == null)
            {
                return;
            }

            ApplyQueuedPulses();

            // A fixed GPU step driven by Time.deltaTime makes propagation independent
            // of render frame rate while preserving the discrete wave equation.
            var fixedStep = 1f / Mathf.Max(1f, simulationFrequency);
            accumulatedTime += Mathf.Min(Time.deltaTime, fixedStep * maximumStepsPerFrame);
            var steps = 0;
            while (accumulatedTime >= fixedStep && steps < maximumStepsPerFrame)
            {
                SimulateStep();
                accumulatedTime -= fixedStep;
                steps++;
            }

            PublishSurfaceTexture();
        }

        /// <summary>Add a radial displacement at a world-space point.</summary>
        public void AddPulse(Vector3 worldPosition)
        {
            AddPulse(worldPosition, defaultPulseRadius, defaultPulseHeight);
        }

        /// <summary>Add a radial displacement at a world-space point.</summary>
        public void AddPulse(Vector3 worldPosition, float radius, float height)
        {
            queuedPulses.Add(new Pulse
            {
                WorldPosition = worldPosition,
                Radius = Mathf.Max(0.001f, radius),
                Height = height
            });
        }

        public void ClearSimulation()
        {
            queuedPulses.Clear();
            accumulatedTime = 0f;
            ClearRenderTexture(bufferA);
            ClearRenderTexture(bufferB);
            current = bufferA;
            next = bufferB;
            PublishSurfaceTexture();
        }

        private void SimulateStep()
        {
            simulationMaterial.SetFloat(DampingId, damping);
            Graphics.Blit(current, next, simulationMaterial);
            SwapBuffers();
        }

        private void ApplyQueuedPulses()
        {
            for (var i = 0; i < queuedPulses.Count; i++)
            {
                var pulse = queuedPulses[i];
                if (!TryWorldToSimulationUv(pulse.WorldPosition, out var uv, out var worldSize))
                {
                    continue;
                }

                inputMaterial.SetVector(PulseUvId, new Vector4(uv.x, uv.y, 0f, 0f));
                inputMaterial.SetVector(PulseRadiusUvId, new Vector4(
                    pulse.Radius / Mathf.Max(worldSize.x, 0.001f),
                    pulse.Radius / Mathf.Max(worldSize.y, 0.001f),
                    0f,
                    0f));
                inputMaterial.SetFloat(PulseHeightId, pulse.Height);
                Graphics.Blit(current, next, inputMaterial);
                SwapBuffers();
            }

            queuedPulses.Clear();
        }

        private bool TryWorldToSimulationUv(Vector3 worldPosition, out Vector2 uv, out Vector2 worldSize)
        {
            var localPosition = transform.InverseTransformPoint(worldPosition);
            var bounds = GetLocalBounds();
            if (bounds.size.x <= Mathf.Epsilon || bounds.size.z <= Mathf.Epsilon)
            {
                uv = default;
                worldSize = default;
                return false;
            }

            uv = new Vector2(
                Mathf.InverseLerp(bounds.min.x, bounds.max.x, localPosition.x),
                Mathf.InverseLerp(bounds.min.z, bounds.max.z, localPosition.z));

            var scale = transform.lossyScale;
            worldSize = new Vector2(
                bounds.size.x * Mathf.Abs(scale.x),
                bounds.size.z * Mathf.Abs(scale.z));
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        private Bounds GetLocalBounds()
        {
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh.bounds;
            }

            return new Bounds(Vector3.zero, new Vector3(1f, 0f, 1f));
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsCollisionLayerEnabled(collision.gameObject.layer) || collision.contactCount == 0)
            {
                return;
            }

            var height = Mathf.Clamp(
                collision.relativeVelocity.magnitude * impactHeightMultiplier,
                0f,
                maximumImpactHeight);
            AddPulse(collision.GetContact(0).point, defaultPulseRadius, height);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsCollisionLayerEnabled(other.gameObject.layer))
            {
                return;
            }

            var point = other.ClosestPoint(transform.position);
            AddPulse(point, defaultPulseRadius, defaultPulseHeight);
        }

        private bool IsCollisionLayerEnabled(int layer)
        {
            return (collisionLayers.value & (1 << layer)) != 0;
        }

        private void CreateResources()
        {
            ReleaseResources();

            simulationShader = simulationShader != null
                ? simulationShader
                : Shader.Find("PillarsAbove/ShallowWaterSimulation");
            inputShader = inputShader != null
                ? inputShader
                : Shader.Find("PillarsAbove/ShallowWaterInput");

            if (simulationShader == null || inputShader == null)
            {
                Debug.LogError("Shallow-water simulation shaders could not be found.", this);
                enabled = false;
                return;
            }

            simulationMaterial = new Material(simulationShader) { hideFlags = HideFlags.HideAndDontSave };
            inputMaterial = new Material(inputShader) { hideFlags = HideFlags.HideAndDontSave };

            var resolution = Mathf.Max(32, textureResolution);
            bufferA = CreateBuffer("Buffer_A", resolution);
            bufferB = CreateBuffer("Buffer_B", resolution);
            current = bufferA;
            next = bufferB;
            surfaceProperties = new MaterialPropertyBlock();
            ClearSimulation();

            if (waterRenderer != null && waterSurfaceMaterial != null)
            {
                waterRenderer.material = waterSurfaceMaterial;
            }
        }

        private static RenderTexture CreateBuffer(string bufferName, int resolution)
        {
            var texture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
            {
                name = bufferName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
            return texture;
        }

        private void PublishSurfaceTexture()
        {
            if (waterRenderer == null || current == null)
            {
                return;
            }

            if (surfaceProperties == null)
            {
                surfaceProperties = new MaterialPropertyBlock();
            }

            waterRenderer.GetPropertyBlock(surfaceProperties);
            surfaceProperties.SetTexture(SimulationTexId, current);
            surfaceProperties.SetFloat(DisplacementId, displacement);
            surfaceProperties.SetFloat(NormalStrengthId, normalStrength);
            waterRenderer.SetPropertyBlock(surfaceProperties);
        }

        private void SwapBuffers()
        {
            var temporary = current;
            current = next;
            next = temporary;
        }

        private static void ClearRenderTexture(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void ReleaseResources()
        {
            if (bufferA != null)
            {
                bufferA.Release();
                Destroy(bufferA);
            }

            if (bufferB != null)
            {
                bufferB.Release();
                Destroy(bufferB);
            }

            if (simulationMaterial != null)
            {
                Destroy(simulationMaterial);
            }

            if (inputMaterial != null)
            {
                Destroy(inputMaterial);
            }

            bufferA = null;
            bufferB = null;
            current = null;
            next = null;
            simulationMaterial = null;
            inputMaterial = null;
        }

        private void OnValidate()
        {
            textureResolution = Mathf.Max(32, textureResolution);
            simulationFrequency = Mathf.Max(1f, simulationFrequency);
            maximumStepsPerFrame = Mathf.Max(1, maximumStepsPerFrame);
            defaultPulseRadius = Mathf.Max(0.001f, defaultPulseRadius);
            maximumImpactHeight = Mathf.Max(0f, maximumImpactHeight);
        }
    }
}
