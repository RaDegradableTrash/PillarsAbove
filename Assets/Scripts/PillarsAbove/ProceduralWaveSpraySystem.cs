using System.Collections.Generic;
using UnityEngine;

namespace PillarsAbove
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ProceduralWaveSpraySystem : MonoBehaviour
    {
        [Header("Wave Source")]
        [SerializeField] private MeshFilter waveMeshFilter;
        [SerializeField] private Transform waveTransform;
        [SerializeField] private float waterLevel = 0f;
        [SerializeField] private float waveAmplitude = 1.4f;
        [SerializeField] private float waveLength = 18f;
        [SerializeField] private float waveSpeed = 1.6f;
        [SerializeField, Range(0f, 1f)] private float gerstnerSteepness = 0.55f;
        [SerializeField] private Vector2 primaryWaveDirection = new Vector2(0.82f, 0.57f);
        [SerializeField] private Vector2 secondaryWaveDirection = new Vector2(-0.58f, 0.81f);
        [SerializeField] private float contactHeightTolerance = 0.45f;
        [SerializeField] private int vertexStride = 8;
        [SerializeField] private int maxVerticesCheckedPerCollider = 160;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionLayers = ~0;
        [SerializeField] private float contactProbeRadius = 1.15f;
        [SerializeField] private float minImpactForce = 0.18f;
        [SerializeField] private float forceToSpraysPerSecond = 16f;

        [Header("Spray Pool")]
        [SerializeField] private GameObject sprayPrefab;
        [SerializeField] private Material sprayMaterial;
        [SerializeField] private int poolSize = 96;
        [SerializeField] private float particleLifetime = 1.15f;
        [SerializeField] private float particleSpeed = 3.2f;
        [SerializeField] private float particleUpBoost = 1.6f;
        [SerializeField] private float particleGravity = 5.6f;
        [SerializeField] private float particleDrag = 1.25f;
        [SerializeField] private Vector2 particleScaleRange = new Vector2(0.10f, 0.32f);
        [SerializeField] private Color sprayColor = new Color(0.95f, 1f, 0.92f, 0.88f);

        private readonly List<SprayParticle> pool = new List<SprayParticle>(128);
        private readonly Dictionary<Collider, float> emissionDebt = new Dictionary<Collider, float>(32);
        private Mesh sourceMesh;
        private Vector3[] sourceVertices;
        private Collider triggerCollider;
        private Rigidbody triggerBody;
        private int nextPoolIndex;

        private struct WavePoint
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Height;
        }

        private sealed class SprayParticle
        {
            public GameObject GameObject;
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Properties;
            public Vector3 Velocity;
            public float Age;
            public float Lifetime;
            public float BaseScale;
            public Color Color;
            public bool Active;
        }

        private void Awake()
        {
            triggerCollider = GetComponent<Collider>();
            triggerCollider.isTrigger = true;
            triggerBody = GetComponent<Rigidbody>();
            triggerBody.isKinematic = true;
            triggerBody.useGravity = false;
            triggerBody.interpolation = RigidbodyInterpolation.None;
            triggerBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            poolSize = Mathf.Max(1, poolSize);

            if (waveTransform == null && waveMeshFilter != null)
            {
                waveTransform = waveMeshFilter.transform;
            }

            RefreshWaveMesh();
            BuildPool();
        }

        public void ConfigureWaveSource(
            MeshFilter meshFilter,
            Transform sourceTransform,
            float level,
            float amplitude,
            float length,
            float speed,
            int stride = 8)
        {
            waveMeshFilter = meshFilter;
            waveTransform = sourceTransform != null ? sourceTransform : meshFilter != null ? meshFilter.transform : transform;
            waterLevel = level;
            waveAmplitude = Mathf.Max(0.01f, amplitude);
            waveLength = Mathf.Max(0.1f, length);
            waveSpeed = Mathf.Max(0.01f, speed);
            vertexStride = Mathf.Max(1, stride);
            sourceMesh = null;
            sourceVertices = null;
            RefreshWaveMesh();
        }

        private void OnValidate()
        {
            vertexStride = Mathf.Max(1, vertexStride);
            maxVerticesCheckedPerCollider = Mathf.Max(8, maxVerticesCheckedPerCollider);
            poolSize = Mathf.Max(1, poolSize);
            waveLength = Mathf.Max(0.1f, waveLength);
            particleLifetime = Mathf.Max(0.05f, particleLifetime);
            contactProbeRadius = Mathf.Max(0.05f, contactProbeRadius);

            var col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }

            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        private void Update()
        {
            RefreshWaveMesh();
            var dt = Time.deltaTime;
            for (var i = 0; i < pool.Count; i++)
            {
                UpdateParticle(pool[i], dt);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (!IsValidCollisionTarget(other) || sourceVertices == null || sourceVertices.Length == 0)
            {
                return;
            }

            var bestForce = 0f;
            var bestPoint = Vector3.zero;
            var bestNormal = Vector3.up;
            var checkedCount = 0;
            var start = Mathf.Abs(other.GetInstanceID()) % vertexStride;
            var sampleStep = Mathf.Max(vertexStride, sourceVertices.Length / Mathf.Max(1, maxVerticesCheckedPerCollider));
            var time = Time.time;
            var bounds = other.bounds;
            bounds.Expand(contactProbeRadius * 2f);

            for (var sample = 0; sample < maxVerticesCheckedPerCollider && checkedCount < sourceVertices.Length; sample++)
            {
                checkedCount++;
                var vertexIndex = (start + sample * sampleStep) % sourceVertices.Length;
                var wavePoint = SampleWavePoint(sourceVertices[vertexIndex], time);
                if (!bounds.Contains(wavePoint.Position))
                {
                    continue;
                }

                var closest = GetClosestPoint(other, wavePoint.Position);
                var delta = wavePoint.Position - closest;
                var distance = delta.magnitude;
                if (distance > contactProbeRadius || Mathf.Abs(delta.y) > contactHeightTolerance + waveAmplitude * 0.35f)
                {
                    continue;
                }

                var normal = EstimateSurfaceNormal(other, wavePoint.Position, closest, delta);
                var intoSurface = Vector3.Dot(wavePoint.Velocity, -normal);
                var crest = Mathf.InverseLerp(waterLevel + waveAmplitude * 0.10f, waterLevel + waveAmplitude * 0.82f, wavePoint.Height);
                var proximity = 1f - Mathf.Clamp01(distance / contactProbeRadius);
                var force = Mathf.Max(0f, intoSurface) * Mathf.Lerp(0.35f, 1f, crest) * proximity;
                if (force > bestForce)
                {
                    bestForce = force;
                    bestPoint = Vector3.Lerp(wavePoint.Position, closest, 0.35f);
                    bestNormal = normal;
                }
            }

            if (bestForce >= minImpactForce)
            {
                EmitFromImpact(other, bestPoint, bestNormal, bestForce);
            }
        }

        private void RefreshWaveMesh()
        {
            if (waveMeshFilter == null)
            {
                return;
            }

            if (sourceMesh == waveMeshFilter.sharedMesh && sourceVertices != null)
            {
                return;
            }

            sourceMesh = waveMeshFilter.sharedMesh;
            sourceVertices = sourceMesh != null ? sourceMesh.vertices : null;
        }

        private bool IsValidCollisionTarget(Collider other)
        {
            if (other == null || other == triggerCollider || other.transform.IsChildOf(transform))
            {
                return false;
            }

            var layerBit = 1 << other.gameObject.layer;
            if ((collisionLayers.value & layerBit) == 0)
            {
                return false;
            }

            return other.attachedRigidbody == null || other.attachedRigidbody.isKinematic;
        }

        private WavePoint SampleWavePoint(Vector3 localVertex, float time)
        {
            var root = waveTransform != null ? waveTransform : transform;
            var baseWorld = root.TransformPoint(localVertex);
            baseWorld.y = waterLevel;
            var current = ApplyWave(baseWorld, time);
            var previous = ApplyWave(baseWorld, time - Mathf.Max(Time.deltaTime, 0.016f));
            return new WavePoint
            {
                Position = current,
                Velocity = (current - previous) / Mathf.Max(Time.deltaTime, 0.016f),
                Height = current.y
            };
        }

        private Vector3 ApplyWave(Vector3 worldPosition, float time)
        {
            var position = worldPosition;
            AddGerstner(ref position, primaryWaveDirection, waveLength, waveAmplitude * 0.72f, time, 1f);
            AddGerstner(ref position, secondaryWaveDirection, waveLength * 0.47f, waveAmplitude * 0.34f, time, 1.55f);
            return position;
        }

        private void AddGerstner(ref Vector3 position, Vector2 direction, float length, float amplitude, float time, float speedScale)
        {
            var dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            var k = Mathf.PI * 2f / Mathf.Max(length, 0.001f);
            var phase = (position.x * dir.x + position.z * dir.y) * k + time * waveSpeed * speedScale;
            var sin = Mathf.Sin(phase);
            var cos = Mathf.Cos(phase);
            var horizontal = gerstnerSteepness * amplitude * cos;
            position.x += dir.x * horizontal;
            position.z += dir.y * horizontal;
            position.y += amplitude * sin;
        }

        private Vector3 EstimateSurfaceNormal(Collider other, Vector3 wavePoint, Vector3 closest, Vector3 delta)
        {
            if (delta.sqrMagnitude > 0.0001f)
            {
                return delta.normalized;
            }

            var origin = wavePoint + Vector3.up * 2f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 4f, collisionLayers, QueryTriggerInteraction.Ignore)
                && hit.collider == other)
            {
                return hit.normal;
            }

            var fromCenter = wavePoint - other.bounds.center;
            fromCenter.y = 0f;
            return fromCenter.sqrMagnitude > 0.0001f ? fromCenter.normalized : Vector3.up;
        }

        private static Vector3 GetClosestPoint(Collider other, Vector3 point)
        {
            var meshCollider = other as MeshCollider;
            if (meshCollider != null && !meshCollider.convex)
            {
                return other.bounds.ClosestPoint(point);
            }

            return other.ClosestPoint(point);
        }

        private void EmitFromImpact(Collider source, Vector3 point, Vector3 normal, float force)
        {
            if (!emissionDebt.TryGetValue(source, out var debt))
            {
                debt = 0f;
            }

            debt += force * forceToSpraysPerSecond * Time.deltaTime;
            var count = Mathf.Min(8, Mathf.FloorToInt(debt));
            debt -= count;
            emissionDebt[source] = Mathf.Min(debt, 6f);

            for (var i = 0; i < count; i++)
            {
                SpawnSpray(point, normal, force, i);
            }
        }

        private void BuildPool()
        {
            for (var i = pool.Count; i < poolSize; i++)
            {
                var particle = CreatePooledParticle(i);
                particle.GameObject.SetActive(false);
                pool.Add(particle);
            }
        }

        private SprayParticle CreatePooledParticle(int index)
        {
            GameObject go;
            if (sprayPrefab != null)
            {
                go = Instantiate(sprayPrefab, transform);
                go.name = $"Pooled Spray {index:00}";
            }
            else
            {
                go = new GameObject($"Procedural Spray {index:00}");
                go.transform.SetParent(transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = BuildDefaultSprayMesh();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = sprayMaterial != null ? sprayMaterial : CreateDefaultSprayMaterial();
            }

            var meshRenderer = go.GetComponentInChildren<MeshRenderer>();
            if (meshRenderer != null && sprayMaterial != null)
            {
                meshRenderer.sharedMaterial = sprayMaterial;
            }

            return new SprayParticle
            {
                GameObject = go,
                Transform = go.transform,
                Renderer = meshRenderer,
                Properties = new MaterialPropertyBlock()
            };
        }

        private void SpawnSpray(Vector3 point, Vector3 normal, float force, int burstIndex)
        {
            var particle = pool[nextPoolIndex];
            nextPoolIndex = (nextPoolIndex + 1) % pool.Count;

            var tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f)
            {
                tangent = Vector3.right;
            }
            tangent.Normalize();

            var randomSide = tangent * Random.Range(-0.55f, 0.55f) + Vector3.Cross(tangent, normal).normalized * Random.Range(-0.25f, 0.25f);
            particle.Transform.position = point + normal * 0.08f + randomSide * 0.15f;
            particle.Transform.rotation = Quaternion.LookRotation((normal + randomSide * 0.35f).normalized, Vector3.up);
            particle.Velocity = (normal * particleSpeed + randomSide * particleSpeed * 0.55f + Vector3.up * particleUpBoost) * Mathf.Lerp(0.55f, 1.7f, Mathf.Clamp01(force));
            particle.Age = 0f;
            particle.Lifetime = particleLifetime * Random.Range(0.75f, 1.25f);
            particle.BaseScale = Random.Range(particleScaleRange.x, particleScaleRange.y) * Mathf.Lerp(0.8f, 1.8f, Mathf.Clamp01(force));
            particle.Color = sprayColor;
            particle.Active = true;
            particle.GameObject.SetActive(true);
            UpdateParticleVisual(particle, 0f);
        }

        private void UpdateParticle(SprayParticle particle, float dt)
        {
            if (!particle.Active)
            {
                return;
            }

            particle.Age += dt;
            if (particle.Age >= particle.Lifetime)
            {
                particle.Active = false;
                particle.GameObject.SetActive(false);
                return;
            }

            particle.Velocity += Vector3.down * particleGravity * dt;
            particle.Velocity *= Mathf.Exp(-particleDrag * dt);
            particle.Transform.position += particle.Velocity * dt;
            if (particle.Velocity.sqrMagnitude > 0.001f)
            {
                particle.Transform.rotation = Quaternion.LookRotation(particle.Velocity.normalized, Vector3.up);
            }

            UpdateParticleVisual(particle, particle.Age / particle.Lifetime);
        }

        private void UpdateParticleVisual(SprayParticle particle, float life01)
        {
            var grow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.22f, life01));
            var shrink = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1f, life01));
            var scale = particle.BaseScale * Mathf.Max(0.001f, grow * shrink);
            particle.Transform.localScale = new Vector3(scale * 0.70f, scale * 1.45f, scale * 0.70f);

            if (particle.Renderer == null)
            {
                return;
            }

            var alpha = particle.Color.a * shrink;
            particle.Renderer.GetPropertyBlock(particle.Properties);
            particle.Properties.SetColor("_Color", new Color(particle.Color.r, particle.Color.g, particle.Color.b, alpha));
            particle.Renderer.SetPropertyBlock(particle.Properties);
        }

        private Mesh BuildDefaultSprayMesh()
        {
            var mesh = new Mesh { name = "Default Procedural Spray Mesh" };
            var vertices = new[]
            {
                new Vector3(0f, 0.58f, 0f),
                new Vector3(0f, -0.24f, 0f),
                new Vector3(0.38f, 0.02f, 0f),
                new Vector3(-0.38f, 0.02f, 0f),
                new Vector3(0f, 0.02f, 0.30f),
                new Vector3(0f, 0.02f, -0.30f)
            };
            var triangles = new[]
            {
                0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2,
                1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5
            };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Material CreateDefaultSprayMaterial()
        {
            var shader = Shader.Find("Unlit/Transparent");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.name = "Runtime Procedural Spray";
            material.color = sprayColor;
            material.renderQueue = 3050;
            return material;
        }
    }
}
