using System;
using System.Collections.Generic;
using UnityEngine;

namespace PillarsAbove.BuildingTiles
{
    [Serializable]
    public sealed class BuildingTileSeamProfile
    {
        [SerializeField] private TileFace face;
        [SerializeField] private List<Vector3> anchors = new List<Vector3>();

        public TileFace Face => face;
        public List<Vector3> Anchors => anchors;

        public BuildingTileSeamProfile(TileFace face, IEnumerable<Vector3> anchors)
        {
            this.face = face;
            this.anchors.AddRange(anchors);
        }
    }

    [Serializable]
    public sealed class BuildingTileSeamSettings
    {
        [Tooltip("Mesh vertices within this distance of a seam anchor are bound to that anchor.")]
        [SerializeField, Min(0.0001f)] private float vertexBindRadius = 0.035f;
        [Tooltip("Automatically use the mesh bounds as a four-point seam when a face has not been authored yet.")]
        [SerializeField] private bool inferMissingFaces = true;
        [SerializeField] private List<BuildingTileSeamProfile> profiles = new List<BuildingTileSeamProfile>();

        public float VertexBindRadius => Mathf.Max(0.0001f, vertexBindRadius);
        public bool InferMissingFaces => inferMissingFaces;
        public List<BuildingTileSeamProfile> Profiles => profiles;

        public BuildingTileSeamProfile Find(TileFace face)
        {
            for (var i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] != null && profiles[i].Face == face) return profiles[i];
            }
            return null;
        }
    }

    /// <summary>
    /// Owns per-instance mesh copies and moves authored seam vertices without changing source assets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingTileMeshDeformer : MonoBehaviour
    {
        private sealed class MeshState
        {
            public MeshFilter Filter;
            public Mesh Source;
            public Mesh Runtime;
            public Vector3[] SourceVertices;
            public Vector3[] WorkingVertices;
            public Matrix4x4 MeshToRoot;
            public Matrix4x4 RootToMesh;
        }

        private BuildingTileDefinition definition;
        private readonly List<MeshState> meshes = new List<MeshState>();
        private readonly Dictionary<TileFace, List<Vector3>> inferredProfiles = new Dictionary<TileFace, List<Vector3>>();
        private Bounds rootBounds;
        private bool prepared;

        public BuildingTileDefinition Definition => definition;

        public void Prepare()
        {
            if (prepared) return;
            definition = GetComponent<BuildingTileDefinition>();
            if (definition == null) return;

            var filters = GetComponentsInChildren<MeshFilter>(true);
            var hasBounds = false;
            for (var i = 0; i < filters.Length; i++)
            {
                var source = filters[i].sharedMesh;
                if (source == null || !source.isReadable) continue;
                var runtime = Instantiate(source);
                runtime.name = source.name + " (Seam Instance)";
                filters[i].sharedMesh = runtime;
                var meshToRoot = transform.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
                var vertices = source.vertices;
                for (var v = 0; v < vertices.Length; v++)
                {
                    var point = meshToRoot.MultiplyPoint3x4(vertices[v]);
                    if (!hasBounds)
                    {
                        rootBounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else rootBounds.Encapsulate(point);
                }
                meshes.Add(new MeshState
                {
                    Filter = filters[i], Source = source, Runtime = runtime,
                    SourceVertices = vertices, WorkingVertices = (Vector3[])vertices.Clone(),
                    MeshToRoot = meshToRoot, RootToMesh = meshToRoot.inverse
                });
            }

            if (hasBounds) BuildInferredProfiles();
            prepared = true;
        }

        public void ResetShape()
        {
            Prepare();
            for (var i = 0; i < meshes.Count; i++)
                Array.Copy(meshes[i].SourceVertices, meshes[i].WorkingVertices, meshes[i].SourceVertices.Length);
        }

        public bool TryGetWorldAnchors(Vector3 worldNormal, out TileFace localFace, out List<Vector3> worldAnchors)
        {
            Prepare();
            localFace = BestLocalFace(worldNormal);
            var localAnchors = GetLocalAnchors(localFace);
            worldAnchors = new List<Vector3>(localAnchors.Count);
            for (var i = 0; i < localAnchors.Count; i++) worldAnchors.Add(transform.TransformPoint(localAnchors[i]));
            return worldAnchors.Count > 0;
        }

        public void MoveBoundVertices(TileFace face, Vector3 sourceAnchor, Vector3 targetWorld)
        {
            var settings = definition.MeshSeams;
            var radius = settings.VertexBindRadius;
            var radiusSqr = radius * radius;
            var targetRoot = transform.InverseTransformPoint(targetWorld);
            var deltaRoot = targetRoot - sourceAnchor;
            for (var m = 0; m < meshes.Count; m++)
            {
                var state = meshes[m];
                for (var v = 0; v < state.SourceVertices.Length; v++)
                {
                    var rootPoint = state.MeshToRoot.MultiplyPoint3x4(state.SourceVertices[v]);
                    if ((rootPoint - sourceAnchor).sqrMagnitude > radiusSqr) continue;
                    // A corner can belong to two or three neighboring faces. Apply each face's
                    // displacement to the current working point so X/Y/Z seam corrections compose.
                    var workingRootPoint = state.MeshToRoot.MultiplyPoint3x4(state.WorkingVertices[v]);
                    state.WorkingVertices[v] = state.RootToMesh.MultiplyPoint3x4(workingRootPoint + deltaRoot);
                }
            }
        }

        public void ApplyShape()
        {
            for (var i = 0; i < meshes.Count; i++)
            {
                var state = meshes[i];
                state.Runtime.vertices = state.WorkingVertices;
                state.Runtime.RecalculateNormals();
                state.Runtime.RecalculateTangents();
                state.Runtime.RecalculateBounds();
                var colliders = state.Filter.GetComponents<Collider>();
                for (var c = 0; c < colliders.Length; c++)
                {
                    var meshCollider = colliders[c] as MeshCollider;
                    if (meshCollider == null) continue;
                    meshCollider.sharedMesh = null;
                    meshCollider.sharedMesh = state.Runtime;
                }
            }
        }

        private void OnDestroy()
        {
            for (var i = 0; i < meshes.Count; i++)
            {
                if (meshes[i].Runtime == null) continue;
                if (Application.isPlaying) Destroy(meshes[i].Runtime);
                else DestroyImmediate(meshes[i].Runtime);
            }
            meshes.Clear();
        }

        private List<Vector3> GetLocalAnchors(TileFace face)
        {
            var profile = definition.MeshSeams.Find(face);
            if (profile != null && profile.Anchors.Count > 0) return profile.Anchors;
            if (definition.MeshSeams.InferMissingFaces && inferredProfiles.TryGetValue(face, out var inferred)) return inferred;
            return EmptyAnchors;
        }

        private TileFace BestLocalFace(Vector3 worldNormal)
        {
            var bestFace = TileFace.PositiveX;
            var bestDot = float.NegativeInfinity;
            for (var i = 0; i < Faces.Length; i++)
            {
                var dot = Vector3.Dot(transform.TransformDirection(FaceNormal(Faces[i])).normalized, worldNormal.normalized);
                if (dot <= bestDot) continue;
                bestDot = dot;
                bestFace = Faces[i];
            }
            return bestFace;
        }

        private void BuildInferredProfiles()
        {
            inferredProfiles.Clear();
            for (var i = 0; i < Faces.Length; i++)
                inferredProfiles[Faces[i]] = BoundsFaceCorners(rootBounds, Faces[i]);
        }

        public static List<Vector3> BoundsFaceCorners(Bounds bounds, TileFace face)
        {
            var min = bounds.min; var max = bounds.max;
            switch (face)
            {
                case TileFace.PositiveX: return new List<Vector3> { new Vector3(max.x,min.y,min.z), new Vector3(max.x,max.y,min.z), new Vector3(max.x,max.y,max.z), new Vector3(max.x,min.y,max.z) };
                case TileFace.NegativeX: return new List<Vector3> { new Vector3(min.x,min.y,max.z), new Vector3(min.x,max.y,max.z), new Vector3(min.x,max.y,min.z), new Vector3(min.x,min.y,min.z) };
                case TileFace.PositiveY: return new List<Vector3> { new Vector3(min.x,max.y,min.z), new Vector3(min.x,max.y,max.z), new Vector3(max.x,max.y,max.z), new Vector3(max.x,max.y,min.z) };
                case TileFace.NegativeY: return new List<Vector3> { new Vector3(min.x,min.y,max.z), new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z), new Vector3(max.x,min.y,max.z) };
                case TileFace.PositiveZ: return new List<Vector3> { new Vector3(max.x,min.y,max.z), new Vector3(max.x,max.y,max.z), new Vector3(min.x,max.y,max.z), new Vector3(min.x,min.y,max.z) };
                default: return new List<Vector3> { new Vector3(min.x,min.y,min.z), new Vector3(min.x,max.y,min.z), new Vector3(max.x,max.y,min.z), new Vector3(max.x,min.y,min.z) };
            }
        }

        public static Vector3 FaceNormal(TileFace face)
        {
            if (face == TileFace.PositiveX) return Vector3.right;
            if (face == TileFace.NegativeX) return Vector3.left;
            if (face == TileFace.PositiveY) return Vector3.up;
            if (face == TileFace.NegativeY) return Vector3.down;
            if (face == TileFace.PositiveZ) return Vector3.forward;
            return Vector3.back;
        }

        private static readonly List<Vector3> EmptyAnchors = new List<Vector3>();
        private static readonly TileFace[] Faces = { TileFace.PositiveX, TileFace.NegativeX, TileFace.PositiveY, TileFace.NegativeY, TileFace.PositiveZ, TileFace.NegativeZ };
    }

    public static class BuildingTileMeshStitcher
    {
        private static readonly Vector3Int[] PositiveDirections = { Vector3Int.right, Vector3Int.up, new Vector3Int(0, 0, 1) };

        public static void StitchGrid(IReadOnlyDictionary<Vector3Int, GameObject> instances)
        {
            var deformers = new Dictionary<Vector3Int, BuildingTileMeshDeformer>();
            foreach (var pair in instances)
            {
                if (pair.Value == null) continue;
                var deformer = pair.Value.GetComponent<BuildingTileMeshDeformer>();
                if (deformer == null) deformer = pair.Value.AddComponent<BuildingTileMeshDeformer>();
                deformer.ResetShape();
                deformers[pair.Key] = deformer;
            }

            foreach (var pair in deformers)
            {
                for (var i = 0; i < PositiveDirections.Length; i++)
                {
                    var direction = PositiveDirections[i];
                    if (deformers.TryGetValue(pair.Key + direction, out var neighbor))
                        StitchPair(pair.Value, neighbor, direction);
                }
            }
            foreach (var pair in deformers) pair.Value.ApplyShape();
        }

        private static void StitchPair(BuildingTileMeshDeformer a, BuildingTileMeshDeformer b, Vector3Int direction)
        {
            var normal = ((Vector3)direction).normalized;
            if (!a.TryGetWorldAnchors(normal, out var faceA, out var anchorsA)) return;
            if (!b.TryGetWorldAnchors(-normal, out var faceB, out var anchorsB)) return;
            if (anchorsA.Count != anchorsB.Count)
            {
                Debug.LogWarning("Cannot stitch " + a.name + " to " + b.name +
                                 ": facing seam handle counts differ (" + anchorsA.Count + " vs " + anchorsB.Count + ").", a);
                return;
            }

            var unused = new HashSet<int>();
            for (var i = 0; i < anchorsB.Count; i++) unused.Add(i);
            var localA = a.Definition.MeshSeams.Find(faceA)?.Anchors;
            var localB = b.Definition.MeshSeams.Find(faceB)?.Anchors;
            for (var i = 0; i < anchorsA.Count; i++)
            {
                var best = -1; var bestDistance = float.PositiveInfinity;
                foreach (var candidate in unused)
                {
                    var distance = (anchorsA[i] - anchorsB[candidate]).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance; best = candidate;
                }
                if (best < 0) continue;
                unused.Remove(best);
                var target = (anchorsA[i] + anchorsB[best]) * 0.5f;
                var sourceA = localA != null && localA.Count == anchorsA.Count ? localA[i] : a.transform.InverseTransformPoint(anchorsA[i]);
                var sourceB = localB != null && localB.Count == anchorsB.Count ? localB[best] : b.transform.InverseTransformPoint(anchorsB[best]);
                a.MoveBoundVertices(faceA, sourceA, target);
                b.MoveBoundVertices(faceB, sourceB, target);
            }
        }
    }
}
