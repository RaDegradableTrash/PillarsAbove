using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PillarsAbove
{
    public sealed class PillarGridGenerator : MonoBehaviour
    {
        [Header("Global Townscaper lattice")]
        [SerializeField, Min(4)] private int nodesPerRing = 18;
        [SerializeField, Min(3)] private int yLevels = 11;
        [SerializeField] private float yMin = -3.1f;
        [SerializeField] private float yMax = 3.1f;
        [SerializeField, Range(0f, 1f)] private float edgeDissolution = 1f;
        [SerializeField, Min(0)] private int relaxationIterations = 8;
        [SerializeField, Range(0f, 1f)] private float relaxationStrength = 0.35f;
        [SerializeField] private int randomSeed = 18731;

        [Header("Boolean pillar")]
        [SerializeField] private Vector2 pillarCenter = new Vector2(Mathf.PI, 0f);
        [Tooltip("Circle radius in the relaxed 2D lattice used to select every touched tile.")]
        [SerializeField, Min(0.1f)] private float pillarRadius = 1.62f;
        [Tooltip("Approximate world-space radius after the selected tile footprint is scaled.")]
        [SerializeField, Min(0.1f)] private float pillarWorldRadius = 9f;
        [SerializeField, Min(0.1f)] private float pillarHeight = 6f;
        [SerializeField, Min(1f)] private float pillarHeightMultiplier = 10f;
        [SerializeField] private float waterHeight = -3f;
        [SerializeField, Min(1)] private int pillarVerticalBands = 36;
        [SerializeField, Min(1)] private int waterTileRepeat = 11;
        [SerializeField, Min(0)] private int waterEdgeLengthRelaxationIterations = 24;
        [SerializeField, Range(0f, 1f)] private float waterEdgeLengthRelaxationStrength = 0.42f;
        [SerializeField, Min(0)] private int waterAngleRelaxationIterations = 30;
        [SerializeField, Range(0f, 1f)] private float waterAngleRelaxationStrength = 0.45f;
        [SerializeField, Range(1f, 89f)] private float waterMinimumCornerAngle = 70f;
        [SerializeField, Range(91f, 179f)] private float waterMaximumCornerAngle = 110f;
        [SerializeField, Range(0f, 1f)] private float waterMinimumEdgeRatio = 0.75f;
        [Tooltip("Only relaxes grid corners sharper than this angle; all other cells keep their generated shape.")]
        [SerializeField, Range(30f, 89f)] private float acuteCornerMinimumAngle = 55f;
        [SerializeField, Min(0)] private int acuteCornerRelaxationIterations = 24;
        [SerializeField, Range(0f, 1f)] private float acuteCornerRelaxationStrength = 0.7f;
        [SerializeField, Range(0.01f, 0.35f)] private float acuteCornerMaximumStepRatio = 0.16f;
        [SerializeField, Range(60f, 75f)] private float placementMinimumAngle = 65f;

        [Header("Generated rendering")]
        [SerializeField] private Color pillarColor = new Color(0.42f, 0.39f, 0.34f, 1f);
        [SerializeField] private Color pillarGridColor = new Color(0.08f, 0.065f, 0.05f, 1f);
        [SerializeField, Min(0.001f)] private float pillarGridLineWidth = 0.018f;
        [SerializeField] private Color pillarHighlightGridColor = new Color(1f, 1f, 1f, 0.45f);
        [SerializeField, Min(0.001f)] private float pillarHighlightGridLineWidth = 0.012f;
        [SerializeField] private Color terrainPlacementColor = new Color(0.42f, 0.39f, 0.34f, 1f);
        [SerializeField] private Color buildingPlacementColor = new Color(0.82f, 0.82f, 0.78f, 1f);
        [SerializeField, Min(0.001f)] private float placementSurfaceOffset = 0.025f;
        [SerializeField, Range(0.2f, 2f)] private float placementDepthScale = 1f;
        [SerializeField, Min(0.001f)] private float activeCellLineWidth = 0.055f;
        [SerializeField, Min(0.001f)] private float activeCellSurfaceOffset = 0.09f;
        [SerializeField] private Color activeCellColor = new Color(1f, 0.84f, 0.16f, 1f);
        [SerializeField, Min(1)] private int activeCellCornerSegments = 5;

        public List<Quad> Quads { get; private set; } = new List<Quad>();
        public List<Quad> PillarQuads { get; private set; } = new List<Quad>();
        public List<Vector2> Vertices { get; private set; } = new List<Vector2>();
        public IReadOnlyCollection<int> SeamVertexIds => seamVertexIds;
        public int NodesPerRing => nodesPerRing;
        public float PillarRadius => pillarWorldRadius;
        public float WaterHeight => waterHeight;

        private readonly List<Quad> waterQuads = new List<Quad>();
        private readonly HashSet<int> seamVertexIds = new HashSet<int>();
        private readonly List<Vector2Int> seamEdges = new List<Vector2Int>();
        private readonly List<Vector3> seamOutwardNormals = new List<Vector3>();
        private TownscaperGridData gridData;
        private Transform generatedRoot;
        private Material pillarMaterial;
        private Material pillarGridMaterial;
        private Material pillarHighlightGridMaterial;
        private Mesh pillarMesh;
        private Mesh pillarGridMesh;
        private Mesh pillarHighlightGridMesh;
        private GameObject pillarSurfaceObject;
        private GameObject pillarGridLinesObject;
        private GameObject pillarHighlightGridLinesObject;
        private WaterGridGenerator waterGenerator;
        private Light realtimeSunLight;
        private float globalHighlightUntil;
        private Bounds cachedBaseWaterTileBounds;
        private bool hasCachedBaseWaterTileBounds;
        private PlacementMode placementMode = PlacementMode.Terrain;
        private GameObject activeCellOutlineObject;
        private Mesh activeCellOutlineMesh;
        private Material activeCellOutlineMaterial;
        private Transform placementRoot;
        private Material terrainPlacementMaterial;
        private Material buildingPlacementMaterial;
        private readonly Dictionary<string, GameObject> placedCells = new Dictionary<string, GameObject>();
        private Vector2 leftMouseDownPosition;
        private Vector2 rightMouseDownPosition;
        private bool leftMouseCandidate;
        private bool rightMouseCandidate;
        private const float ClickDragThresholdPixels = 8f;
        private static readonly int[,] PlacedCellFaceVertexIndices =
        {
            { 0, 1, 2, 3 },
            { 4, 5, 6, 7 },
            { 8, 9, 10, 11 },
            { 12, 13, 14, 15 },
            { 16, 17, 18, 19 },
            { 20, 21, 22, 23 }
        };

        private enum PlacementMode
        {
            Terrain,
            Building
        }

        private struct AcuteCornerCandidate
        {
            public Quad Quad;
            public int Corner;
            public float Angle;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapV2()
        {
            if (!string.Equals(SceneManager.GetActiveScene().name, "V2", StringComparison.Ordinal) ||
                FindObjectOfType<PillarGridGenerator>() != null)
            {
                return;
            }

            var host = new GameObject("Boolean Townscaper Grid");
            host.AddComponent<PillarGridGenerator>();
        }

        private void Awake()
        {
            ConfigureRealtimeLighting();
            Generate();
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(pillarMaterial);
            DestroyRuntimeObject(pillarGridMaterial);
            DestroyRuntimeObject(pillarHighlightGridMaterial);
            DestroyRuntimeObject(activeCellOutlineMaterial);
            DestroyRuntimeObject(terrainPlacementMaterial);
            DestroyRuntimeObject(buildingPlacementMaterial);
            DestroyRuntimeObject(pillarMesh);
            DestroyRuntimeObject(pillarGridMesh);
            DestroyRuntimeObject(pillarHighlightGridMesh);
            DestroyRuntimeObject(activeCellOutlineMesh);
        }

        [ContextMenu("Generate Boolean Townscaper Grid")]
        public void Generate()
        {
            nodesPerRing = Mathf.Max(4, nodesPerRing);
            yLevels = Mathf.Max(3, yLevels);
            if (yMax <= yMin)
            {
                yMax = yMin + 0.1f;
            }

            // The lattice maps θ (0..2π) and y (yMin..yMax) onto the water/pillar plane with a single
            // uniform world scale, so a cell reads square only when its y-step equals its θ-step.
            // Balance the vertical level count to the ring count so tiles stay ~1:1 (well within the
            // ±25% target) no matter how nodesPerRing or the y-extent are later tuned.
            var thetaStep = Mathf.PI * 2f / nodesPerRing;
            yLevels = Mathf.Max(3, Mathf.RoundToInt((yMax - yMin) / thetaStep) + 1);

            if (waterGenerator == null)
            {
                waterGenerator = GetComponent<WaterGridGenerator>();
                if (waterGenerator == null)
                {
                    waterGenerator = gameObject.AddComponent<WaterGridGenerator>();
                }
            }

            gridData = waterGenerator.GenerateGlobalTownscaperGrid(
                nodesPerRing,
                yLevels,
                yMin,
                yMax,
                edgeDissolution,
                randomSeed,
                relaxationIterations,
                relaxationStrength);
            ClearRuntimePlacements();
            hasCachedBaseWaterTileBounds = false;
            Vertices = gridData.Vertices;
            Quads = gridData.Quads;
            PartitionAfterRelaxation();
            ExtractSharedBoundaryEdges();
            RelaxAcuteWaterCorners();
            RelaxWaterEdgeLengths();
            RelaxWaterQuadAngles();
            TownscaperGridTopology.RecalculateCentroids(gridData);
            PartitionAfterRelaxation();
            ExtractSharedBoundaryEdges();
            ValidateTopologyAndSeam();
            BuildPillarMesh();
            waterGenerator.GenerateFrom(this, waterQuads);
        }

        public bool IsInPillar(Vector2 quadCentroid)
        {
            var delta = ParameterDeltaFromPillarCenter(quadCentroid);
            return delta.sqrMagnitude <= pillarRadius * pillarRadius;
        }

        /// <summary>Maps the global lattice to the horizontal water/top plane.</summary>
        public Vector3 MapWaterVertex(Vector2 parameter)
        {
            var delta = ParameterDeltaFromPillarCenter(parameter);
            var scale = pillarWorldRadius / Mathf.Max(0.0001f, pillarRadius);
            return transform.TransformPoint(new Vector3(delta.x * scale, waterHeight, delta.y * scale));
        }

        internal Vector3 MapWaterTileVertex(int vertexId, float anchorTheta, int tileX, int tileZ)
        {
            var basePoint = MapPlanarVertex(Vertices[vertexId], anchorTheta, waterHeight);
            return basePoint + GetWaterTileOffset(tileX, tileZ);
        }

        public Vector3 MapPillarTopVertex(Vector2 parameter)
        {
            var point = MapWaterVertex(parameter);
            return point + transform.up * (PillarTopHeight - waterHeight);
        }

        internal Vector3 MapPlanarVertex(Vector2 parameter, float anchorTheta, float worldY)
        {
            var scale = pillarWorldRadius / Mathf.Max(0.0001f, pillarRadius);
            var anchorFromCenter = TownscaperGridTopology.ShortestThetaDelta(pillarCenter.x, anchorTheta);
            var unwrappedX = anchorFromCenter + TownscaperGridTopology.ShortestThetaDelta(anchorTheta, parameter.x);
            return transform.TransformPoint(new Vector3(
                unwrappedX * scale,
                worldY,
                (parameter.y - pillarCenter.y) * scale));
        }

        private float EffectivePillarHeight => pillarHeight * pillarHeightMultiplier;
        private float PillarOriginalCenterHeight => waterHeight + pillarHeight * 0.5f;
        private float PillarBottomHeight => PillarOriginalCenterHeight - EffectivePillarHeight * 0.5f;
        private float PillarTopHeight => PillarOriginalCenterHeight + EffectivePillarHeight * 0.5f;

        public bool TryGetQuadAtTriangle(int triangleIndex, bool pillarSurface, out Quad quad)
        {
            if (pillarSurface)
            {
                quad = null;
                return false;
            }
            var source = waterQuads;
            var quadIndex = triangleIndex / 2;
            if (quadIndex >= 0 && quadIndex < source.Count)
            {
                quad = source[quadIndex];
                return true;
            }
            quad = null;
            return false;
        }

        private void PartitionAfterRelaxation()
        {
            PillarQuads.Clear();
            waterQuads.Clear();
            for (var quadIndex = 0; quadIndex < Quads.Count; quadIndex++)
            {
                var quad = Quads[quadIndex];
                if (IsQuadCoveredByPillar(quad)) PillarQuads.Add(quad);
                else waterQuads.Add(quad);
            }
        }

        public bool IsQuadCoveredByPillar(Quad quad)
        {
            var points = new Vector2[4];
            var centerInQuadSpace = new Vector2(
                TownscaperGridTopology.ShortestThetaDelta(quad.Centroid.x, pillarCenter.x),
                pillarCenter.y - quad.Centroid.y);
            for (var corner = 0; corner < 4; corner++)
            {
                var vertex = Vertices[quad.GetVertex(corner)];
                points[corner] = new Vector2(
                    TownscaperGridTopology.ShortestThetaDelta(quad.Centroid.x, vertex.x),
                    vertex.y - quad.Centroid.y);
                if ((points[corner] - centerInQuadSpace).sqrMagnitude <= pillarRadius * pillarRadius) return true;
            }

            if (PointInsidePolygon(centerInQuadSpace, points)) return true;
            for (var edge = 0; edge < 4; edge++)
            {
                if (DistanceToSegment(centerInQuadSpace, points[edge], points[(edge + 1) % 4]) <= pillarRadius)
                {
                    return true;
                }
            }
            return false;
        }

        private void ExtractSharedBoundaryEdges()
        {
            seamVertexIds.Clear();
            seamEdges.Clear();
            seamOutwardNormals.Clear();
            var edgeFlags = new Dictionary<ulong, int>();
            var edgeVertices = new Dictionary<ulong, Vector2Int>();
            var pillarOwners = new Dictionary<ulong, Quad>();
            AccumulateEdgeFlags(PillarQuads, 1, edgeFlags, edgeVertices, pillarOwners);
            AccumulateEdgeFlags(waterQuads, 2, edgeFlags, edgeVertices, null);

            foreach (var pair in edgeFlags)
            {
                if (pair.Value != 3) continue;
                var edge = edgeVertices[pair.Key];
                seamEdges.Add(edge);
                seamOutwardNormals.Add(CalculateBoundaryOutwardNormal(edge, pillarOwners[pair.Key]));
                seamVertexIds.Add(edge.x);
                seamVertexIds.Add(edge.y);
            }
        }

        private void RelaxWaterEdgeLengths()
        {
            if (waterEdgeLengthRelaxationIterations <= 0 || waterQuads.Count == 0 || Vertices.Count == 0)
            {
                return;
            }

            waterMinimumEdgeRatio = Mathf.Clamp01(waterMinimumEdgeRatio);
            if (waterMinimumEdgeRatio <= 0f)
            {
                return;
            }

            bool[] lockY;
            var movable = BuildWaterMovableVertexMask(out lockY);
            var strength = Mathf.Clamp01(waterEdgeLengthRelaxationStrength);
            var previousPenalty = CalculateWaterEdgeLengthPenalty();

            for (var iteration = 0; iteration < waterEdgeLengthRelaxationIterations; iteration++)
            {
                TownscaperGridTopology.RecalculateCentroids(gridData);
                var deltas = new Vector2[Vertices.Count];
                var counts = new int[Vertices.Count];

                for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
                {
                    var quad = waterQuads[quadIndex];
                    Vector2[] points;
                    if (!TryGetQuadParameterPoints(quad, out points))
                    {
                        continue;
                    }

                    var longest = 0f;
                    var lengths = new float[4];
                    for (var edge = 0; edge < 4; edge++)
                    {
                        lengths[edge] = Vector2.Distance(points[edge], points[(edge + 1) % 4]);
                        longest = Mathf.Max(longest, lengths[edge]);
                    }
                    if (longest <= 0.000001f)
                    {
                        continue;
                    }

                    var minimumAllowed = longest * waterMinimumEdgeRatio;
                    for (var edge = 0; edge < 4; edge++)
                    {
                        var length = lengths[edge];
                        if (length >= minimumAllowed || length <= 0.000001f)
                        {
                            continue;
                        }

                        var startCorner = edge;
                        var endCorner = (edge + 1) % 4;
                        var startVertex = quad.GetVertex(startCorner);
                        var endVertex = quad.GetVertex(endCorner);
                        var direction = (points[endCorner] - points[startCorner]) / length;
                        var correction = direction * ((minimumAllowed - length) * 0.5f);
                        if (movable[startVertex])
                        {
                            deltas[startVertex] -= correction;
                            counts[startVertex]++;
                        }
                        if (movable[endVertex])
                        {
                            deltas[endVertex] += correction;
                            counts[endVertex]++;
                        }
                    }
                }

                var originalVertices = Vertices.ToArray();
                var accepted = false;
                var candidateStrength = strength;
                while (candidateStrength >= 0.01f)
                {
                    var changed = false;
                    for (var vertex = 0; vertex < Vertices.Count; vertex++)
                    {
                        if (counts[vertex] == 0)
                        {
                            continue;
                        }

                        var current = originalVertices[vertex];
                        var delta = deltas[vertex] / counts[vertex] * candidateStrength;
                        var next = new Vector2(
                            TownscaperGridTopology.WrapTheta(current.x + delta.x),
                            Mathf.Clamp(current.y + delta.y, yMin, yMax));
                        if (lockY[vertex])
                        {
                            next.y = current.y;
                        }
                        if ((next - current).sqrMagnitude > 0.0000001f)
                        {
                            changed = true;
                        }
                        Vertices[vertex] = next;
                    }

                    StitchWaterVerticalBoundary();
                    TownscaperGridTopology.RecalculateCentroids(gridData);
                    var candidatePenalty = CalculateWaterEdgeLengthPenalty();
                    if (changed && candidatePenalty <= previousPenalty)
                    {
                        previousPenalty = candidatePenalty;
                        accepted = true;
                        break;
                    }

                    for (var vertex = 0; vertex < Vertices.Count; vertex++)
                    {
                        Vertices[vertex] = originalVertices[vertex];
                    }
                    TownscaperGridTopology.RecalculateCentroids(gridData);
                    candidateStrength *= 0.5f;
                }

                if (!accepted)
                {
                    break;
                }
            }
        }

        private float CalculateWaterEdgeLengthPenalty()
        {
            var penalty = 0f;
            for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
            {
                Vector2[] points;
                if (!TryGetQuadParameterPoints(waterQuads[quadIndex], out points))
                {
                    penalty += 100000f;
                    continue;
                }

                var shortest = float.MaxValue;
                var longest = 0f;
                for (var edge = 0; edge < 4; edge++)
                {
                    var length = Vector2.Distance(points[edge], points[(edge + 1) % 4]);
                    shortest = Mathf.Min(shortest, length);
                    longest = Mathf.Max(longest, length);
                }

                if (longest <= 0.000001f)
                {
                    penalty += 100000f;
                    continue;
                }

                var ratio = shortest / longest;
                if (ratio < waterMinimumEdgeRatio)
                {
                    var deficit = waterMinimumEdgeRatio - ratio;
                    penalty += deficit * deficit;
                }
            }
            return penalty;
        }

        private void RelaxAcuteWaterCorners()
        {
            RelaxAcuteCorners(Quads, false);
        }

        private void RelaxAcuteCorners(List<Quad> candidateQuads, bool prioritizeCandidateQuads)
        {
            if (acuteCornerRelaxationIterations <= 0 || candidateQuads.Count == 0 || Vertices.Count == 0)
            {
                return;
            }

            var minimumAngle = Mathf.Clamp(acuteCornerMinimumAngle, 30f, 89f);
            var strength = Mathf.Clamp01(acuteCornerRelaxationStrength);
            var maximumStepRatio = Mathf.Clamp(acuteCornerMaximumStepRatio, 0.01f, 0.35f);
            bool[] lockY;
            BuildWaterMovableVertexMask(out lockY);
            var incidentQuads = BuildIncidentQuadMap();
            var boundaryPartners = BuildVerticalBoundaryPartnerMap();
            var candidateSet = prioritizeCandidateQuads ? new HashSet<Quad>(candidateQuads) : null;
            for (var iteration = 0; iteration < acuteCornerRelaxationIterations; iteration++)
            {
                TownscaperGridTopology.RecalculateCentroids(gridData);
                var candidates = new List<AcuteCornerCandidate>();
                for (var quadIndex = 0; quadIndex < candidateQuads.Count; quadIndex++)
                {
                    var quad = candidateQuads[quadIndex];
                    Vector2[] points;
                    if (!TryGetQuadParameterPoints(quad, out points))
                    {
                        continue;
                    }
                    for (var corner = 0; corner < 4; corner++)
                    {
                        var angle = CalculateCornerAngle(points, corner);
                        if (angle < minimumAngle)
                        {
                            candidates.Add(new AcuteCornerCandidate { Quad = quad, Corner = corner, Angle = angle });
                        }
                    }
                }
                candidates.Sort((left, right) => left.Angle.CompareTo(right.Angle));

                var movedVertices = new HashSet<int>();
                var acceptedMoves = 0;
                for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    var candidate = candidates[candidateIndex];
                    Vector2[] points;
                    if (!TryGetQuadParameterPoints(candidate.Quad, out points))
                    {
                        continue;
                    }
                    var currentAngle = CalculateCornerAngle(points, candidate.Corner);
                    if (currentAngle >= minimumAngle)
                    {
                        continue;
                    }

                    Vector2[] targets;
                    if (!TryGetRectifiedQuadTargets(points, out targets))
                    {
                        continue;
                    }
                    for (var relaxationOrder = 0; relaxationOrder < 4; relaxationOrder++)
                    {
                        var relaxationCorner = relaxationOrder == 0
                            ? candidate.Corner
                            : relaxationOrder == 1
                                ? (candidate.Corner + 3) % 4
                                : relaxationOrder == 2
                                    ? (candidate.Corner + 1) % 4
                                    : (candidate.Corner + 2) % 4;
                        var vertex = candidate.Quad.GetVertex(relaxationCorner);
                        if (movedVertices.Contains(vertex))
                        {
                            continue;
                        }

                        var currentPoint = points[relaxationCorner];
                        var previousPoint = points[(relaxationCorner + 3) % 4];
                        var nextPoint = points[(relaxationCorner + 1) % 4];
                        var correction = targets[relaxationCorner] - currentPoint;
                        var shortestAdjacentEdge = Mathf.Min(
                            Vector2.Distance(currentPoint, previousPoint),
                            Vector2.Distance(currentPoint, nextPoint));
                        var maximumStep = shortestAdjacentEdge * maximumStepRatio;
                        if (correction.magnitude > maximumStep)
                        {
                            correction = correction.normalized * maximumStep;
                        }
                        correction *= Mathf.Clamp01((minimumAngle - currentAngle) / minimumAngle + 0.35f);
                        if (correction.sqrMagnitude < 0.0000001f)
                        {
                            continue;
                        }

                        var boundaryPartner = lockY[vertex] ? boundaryPartners[vertex] : -1;
                        if (lockY[vertex]) correction.y = 0f;
                        var affectedIncident = boundaryPartner >= 0
                            ? CombineIncidentQuads(incidentQuads[vertex], incidentQuads[boundaryPartner])
                            : incidentQuads[vertex];
                        var qualityIncident = prioritizeCandidateQuads
                            ? FilterIncidentQuads(affectedIncident, candidateSet)
                            : affectedIncident;
                        if (TryAcceptIncidentVertexMove(
                                vertex,
                                boundaryPartner,
                                correction,
                                strength,
                                minimumAngle,
                                qualityIncident,
                                affectedIncident,
                                prioritizeCandidateQuads))
                        {
                            movedVertices.Add(vertex);
                            if (boundaryPartner >= 0) movedVertices.Add(boundaryPartner);
                            acceptedMoves++;
                            break;
                        }
                    }
                }

                if (acceptedMoves == 0)
                {
                    break;
                }
            }
            StitchWaterVerticalBoundary();
            TownscaperGridTopology.RecalculateCentroids(gridData);
        }

        private bool TryAcceptIncidentVertexMove(
            int vertex,
            int boundaryPartner,
            Vector2 correction,
            float strength,
            float minimumAngle,
            List<Quad> qualityIncident,
            List<Quad> affectedIncident,
            bool focusedPass)
        {
            int beforeCount;
            float beforePenalty;
            float beforeMinimum;
            float beforeMinimumEdgeRatio;
            float beforeMaximumAngle;
            bool beforeValid;
            EvaluateIncidentCornerQuality(
                qualityIncident, minimumAngle,
                out beforeCount, out beforePenalty, out beforeMinimum,
                out beforeMinimumEdgeRatio, out beforeMaximumAngle, out beforeValid);
            if (!beforeValid)
            {
                return false;
            }

            var original = Vertices[vertex];
            var originalPartner = boundaryPartner >= 0 ? Vertices[boundaryPartner] : Vector2.zero;
            var candidateStrength = strength;
            while (candidateStrength >= 0.02f)
            {
                Vertices[vertex] = new Vector2(
                    TownscaperGridTopology.WrapTheta(original.x + correction.x * candidateStrength),
                    Mathf.Clamp(original.y + correction.y * candidateStrength, yMin, yMax));
                if (boundaryPartner >= 0)
                {
                    Vertices[boundaryPartner] = new Vector2(
                        TownscaperGridTopology.WrapTheta(originalPartner.x + correction.x * candidateStrength),
                        originalPartner.y);
                }

                int afterCount;
                float afterPenalty;
                float afterMinimum;
                float afterMinimumEdgeRatio;
                float afterMaximumAngle;
                bool afterValid;
                EvaluateIncidentCornerQuality(
                    qualityIncident, minimumAngle,
                    out afterCount, out afterPenalty, out afterMinimum,
                    out afterMinimumEdgeRatio, out afterMaximumAngle, out afterValid);
                int ignoredCount;
                float ignoredPenalty;
                float affectedMinimum;
                float affectedMinimumEdgeRatio;
                float affectedMaximumAngle;
                bool affectedValid;
                EvaluateIncidentCornerQuality(
                    affectedIncident, minimumAngle,
                    out ignoredCount, out ignoredPenalty, out affectedMinimum,
                    out affectedMinimumEdgeRatio, out affectedMaximumAngle, out affectedValid);
                var improvesQuality = afterCount < beforeCount ||
                                      afterCount == beforeCount && afterPenalty < beforePenalty - 0.0001f;
                var preservesEdgeQuality = affectedMinimumEdgeRatio >= 0.29f;
                var preservesMaximumAngle = affectedMaximumAngle <= Mathf.Max(beforeMaximumAngle, 145f) + 0.1f;
                var preservesMinimumAngle = focusedPass
                    ? affectedMinimum >= 40f
                    : afterMinimum >= beforeMinimum - 0.05f;
                if (afterValid && affectedValid && improvesQuality && preservesMinimumAngle &&
                    preservesEdgeQuality && preservesMaximumAngle)
                {
                    return true;
                }

                Vertices[vertex] = original;
                if (boundaryPartner >= 0) Vertices[boundaryPartner] = originalPartner;
                candidateStrength *= 0.5f;
            }
            Vertices[vertex] = original;
            if (boundaryPartner >= 0) Vertices[boundaryPartner] = originalPartner;
            return false;
        }

        private int[] BuildVerticalBoundaryPartnerMap()
        {
            var partners = new int[Vertices.Count];
            for (var vertex = 0; vertex < partners.Length; vertex++) partners[vertex] = -1;
            var bottom = new List<int>();
            var top = new List<int>();
            var tolerance = Mathf.Max(0.0001f, (yMax - yMin) * 0.0005f);
            for (var vertex = 0; vertex < Vertices.Count; vertex++)
            {
                if (Mathf.Abs(Vertices[vertex].y - yMin) <= tolerance) bottom.Add(vertex);
                else if (Mathf.Abs(Vertices[vertex].y - yMax) <= tolerance) top.Add(vertex);
            }
            bottom.Sort((left, right) => Vertices[left].x.CompareTo(Vertices[right].x));
            top.Sort((left, right) => Vertices[left].x.CompareTo(Vertices[right].x));
            var count = Mathf.Min(bottom.Count, top.Count);
            for (var index = 0; index < count; index++)
            {
                partners[bottom[index]] = top[index];
                partners[top[index]] = bottom[index];
            }
            return partners;
        }

        private static List<Quad> CombineIncidentQuads(List<Quad> first, List<Quad> second)
        {
            var combined = new List<Quad>(first.Count + second.Count);
            for (var index = 0; index < first.Count; index++) combined.Add(first[index]);
            for (var index = 0; index < second.Count; index++)
            {
                if (!combined.Contains(second[index])) combined.Add(second[index]);
            }
            return combined;
        }

        private static List<Quad> FilterIncidentQuads(List<Quad> incident, HashSet<Quad> allowed)
        {
            var filtered = new List<Quad>(incident.Count);
            for (var index = 0; index < incident.Count; index++)
            {
                if (allowed.Contains(incident[index])) filtered.Add(incident[index]);
            }
            return filtered;
        }

        private List<Quad>[] BuildIncidentQuadMap()
        {
            var incident = new List<Quad>[Vertices.Count];
            for (var vertex = 0; vertex < incident.Length; vertex++) incident[vertex] = new List<Quad>(6);
            for (var quadIndex = 0; quadIndex < Quads.Count; quadIndex++)
            {
                var quad = Quads[quadIndex];
                for (var corner = 0; corner < 4; corner++)
                {
                    var vertex = quad.GetVertex(corner);
                    if (!incident[vertex].Contains(quad)) incident[vertex].Add(quad);
                }
            }
            return incident;
        }

        private void EvaluateIncidentCornerQuality(
            List<Quad> incident,
            float minimumAngle,
            out int acuteCount,
            out float acutePenalty,
            out float minimumIncidentAngle,
            out float minimumIncidentEdgeRatio,
            out float maximumIncidentAngle,
            out bool valid)
        {
            acuteCount = 0;
            acutePenalty = 0f;
            minimumIncidentAngle = 180f;
            minimumIncidentEdgeRatio = 1f;
            maximumIncidentAngle = 0f;
            valid = true;
            for (var quadIndex = 0; quadIndex < incident.Count; quadIndex++)
            {
                Vector2[] points;
                if (!TryGetQuadParameterPoints(incident[quadIndex], out points) || Mathf.Abs(SignedPolygonArea(points)) < 0.0001f)
                {
                    valid = false;
                    return;
                }
                for (var corner = 0; corner < 4; corner++)
                {
                    var angle = CalculateCornerAngle(points, corner);
                    minimumIncidentAngle = Mathf.Min(minimumIncidentAngle, angle);
                    maximumIncidentAngle = Mathf.Max(maximumIncidentAngle, angle);
                    if (angle < minimumAngle)
                    {
                        var deficit = minimumAngle - angle;
                        acuteCount++;
                        acutePenalty += deficit * deficit;
                    }
                }

                var shortestEdge = float.MaxValue;
                var longestEdge = 0f;
                for (var edge = 0; edge < 4; edge++)
                {
                    var length = Vector2.Distance(points[edge], points[(edge + 1) % 4]);
                    shortestEdge = Mathf.Min(shortestEdge, length);
                    longestEdge = Mathf.Max(longestEdge, length);
                }
                if (longestEdge <= 0.000001f)
                {
                    valid = false;
                    return;
                }
                minimumIncidentEdgeRatio = Mathf.Min(minimumIncidentEdgeRatio, shortestEdge / longestEdge);
            }
        }

        private Dictionary<int, List<int>> BuildSeamNeighborMap()
        {
            var neighbors = new Dictionary<int, List<int>>();
            for (var edgeIndex = 0; edgeIndex < seamEdges.Count; edgeIndex++)
            {
                var edge = seamEdges[edgeIndex];
                AddSeamNeighbor(neighbors, edge.x, edge.y);
                AddSeamNeighbor(neighbors, edge.y, edge.x);
            }
            return neighbors;
        }

        private static void AddSeamNeighbor(Dictionary<int, List<int>> neighbors, int vertex, int neighbor)
        {
            List<int> values;
            if (!neighbors.TryGetValue(vertex, out values))
            {
                values = new List<int>(2);
                neighbors.Add(vertex, values);
            }
            if (!values.Contains(neighbor)) values.Add(neighbor);
        }

        private float CalculateAcuteSeamBoundaryPenalty(Dictionary<int, List<int>> seamNeighbors, float minimumAngle)
        {
            var penalty = 0f;
            foreach (var pair in seamNeighbors)
            {
                float angle;
                if (!TryCalculateSeamBoundaryAngle(pair.Key, pair.Value, out angle))
                {
                    continue;
                }
                if (angle < minimumAngle)
                {
                    var deficit = minimumAngle - angle;
                    penalty += deficit * deficit;
                }
            }
            return penalty;
        }

        private bool TryCalculateSeamBoundaryAngle(int vertex, List<int> neighbors, out float angle)
        {
            angle = 180f;
            if (neighbors == null || neighbors.Count != 2)
            {
                return false;
            }
            var current = Vertices[vertex];
            var first = Vertices[neighbors[0]];
            var second = Vertices[neighbors[1]];
            var firstDirection = new Vector2(
                TownscaperGridTopology.ShortestThetaDelta(current.x, first.x),
                first.y - current.y);
            var secondDirection = new Vector2(
                TownscaperGridTopology.ShortestThetaDelta(current.x, second.x),
                second.y - current.y);
            if (firstDirection.sqrMagnitude < 0.000001f || secondDirection.sqrMagnitude < 0.000001f)
            {
                return false;
            }
            angle = Vector2.Angle(firstDirection, secondDirection);
            return true;
        }

        private void RelaxWaterQuadAngles()
        {
            if (waterAngleRelaxationIterations <= 0 || waterQuads.Count == 0 || Vertices.Count == 0)
            {
                return;
            }

            waterMinimumCornerAngle = Mathf.Clamp(waterMinimumCornerAngle, 1f, 89f);
            waterMaximumCornerAngle = Mathf.Clamp(waterMaximumCornerAngle, 91f, 179f);
            if (waterMinimumCornerAngle >= waterMaximumCornerAngle)
            {
                waterMinimumCornerAngle = 75f;
                waterMaximumCornerAngle = 105f;
            }

            bool[] lockY;
            var movable = BuildWaterMovableVertexMask(out lockY);
            var strength = Mathf.Clamp01(waterAngleRelaxationStrength);
            var previousPenalty = CalculateWaterAnglePenalty();
            for (var iteration = 0; iteration < waterAngleRelaxationIterations; iteration++)
            {
                TownscaperGridTopology.RecalculateCentroids(gridData);
                var sums = new Vector2[Vertices.Count];
                var counts = new int[Vertices.Count];
                for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
                {
                    var quad = waterQuads[quadIndex];
                    Vector2[] points;
                    if (!TryGetQuadParameterPoints(quad, out points) || QuadAnglesAreWithinWaterRange(points))
                    {
                        continue;
                    }

                    Vector2[] targets;
                    if (!TryGetRectifiedQuadTargets(points, out targets))
                    {
                        continue;
                    }

                    for (var corner = 0; corner < 4; corner++)
                    {
                        var vertex = quad.GetVertex(corner);
                        if (!movable[vertex])
                        {
                            continue;
                        }

                        var current = Vertices[vertex];
                        var target = new Vector2(
                            current.x + TownscaperGridTopology.ShortestThetaDelta(current.x, targets[corner].x),
                            targets[corner].y);
                        sums[vertex] += target;
                        counts[vertex]++;
                    }
                }

                var originalVertices = Vertices.ToArray();
                var accepted = false;
                var candidateStrength = strength;
                while (candidateStrength >= 0.015f)
                {
                    var changed = false;
                    for (var vertex = 0; vertex < Vertices.Count; vertex++)
                    {
                        if (counts[vertex] == 0)
                        {
                            continue;
                        }

                        var current = originalVertices[vertex];
                        var target = sums[vertex] / counts[vertex];
                        var next = new Vector2(
                            TownscaperGridTopology.WrapTheta(current.x + TownscaperGridTopology.ShortestThetaDelta(current.x, target.x) * candidateStrength),
                            Mathf.Lerp(current.y, Mathf.Clamp(target.y, yMin, yMax), candidateStrength));
                        if (lockY[vertex])
                        {
                            next.y = current.y;
                        }
                        if ((next - current).sqrMagnitude > 0.0000001f)
                        {
                            changed = true;
                        }
                        Vertices[vertex] = next;
                    }

                    StitchWaterVerticalBoundary();
                    TownscaperGridTopology.RecalculateCentroids(gridData);
                    var candidatePenalty = CalculateWaterAnglePenalty();
                    if (changed && candidatePenalty <= previousPenalty)
                    {
                        previousPenalty = candidatePenalty;
                        accepted = true;
                        break;
                    }

                    for (var vertex = 0; vertex < Vertices.Count; vertex++)
                    {
                        Vertices[vertex] = originalVertices[vertex];
                    }
                    TownscaperGridTopology.RecalculateCentroids(gridData);
                    candidateStrength *= 0.5f;
                }

                if (!accepted)
                {
                    break;
                }
            }
        }

        private bool[] BuildWaterMovableVertexMask(out bool[] lockY)
        {
            var movable = new bool[Vertices.Count];
            lockY = new bool[Vertices.Count];
            for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
            {
                var quad = waterQuads[quadIndex];
                for (var corner = 0; corner < 4; corner++)
                {
                    movable[quad.GetVertex(corner)] = true;
                }
            }

            for (var quadIndex = 0; quadIndex < PillarQuads.Count; quadIndex++)
            {
                var quad = PillarQuads[quadIndex];
                for (var corner = 0; corner < 4; corner++)
                {
                    var vertex = quad.GetVertex(corner);
                    if (!movable[vertex])
                    {
                        movable[vertex] = false;
                    }
                }
            }

            var boundaryTolerance = Mathf.Max(0.0001f, (yMax - yMin) * 0.0005f);
            for (var vertex = 0; vertex < Vertices.Count; vertex++)
            {
                var point = Vertices[vertex];
                if (Mathf.Abs(point.y - yMin) <= boundaryTolerance ||
                    Mathf.Abs(point.y - yMax) <= boundaryTolerance)
                {
                    lockY[vertex] = true;
                }
            }

            return movable;
        }

        private void StitchWaterVerticalBoundary()
        {
            var bottom = new List<int>();
            var top = new List<int>();
            var tolerance = Mathf.Max(0.0001f, (yMax - yMin) * 0.0005f);
            for (var vertex = 0; vertex < Vertices.Count; vertex++)
            {
                var point = Vertices[vertex];
                if (Mathf.Abs(point.y - yMin) <= tolerance)
                {
                    bottom.Add(vertex);
                }
                else if (Mathf.Abs(point.y - yMax) <= tolerance)
                {
                    top.Add(vertex);
                }
            }

            bottom.Sort((left, right) => Vertices[left].x.CompareTo(Vertices[right].x));
            top.Sort((left, right) => Vertices[left].x.CompareTo(Vertices[right].x));
            var count = Mathf.Min(bottom.Count, top.Count);
            for (var index = 0; index < count; index++)
            {
                var bottomVertex = bottom[index];
                var topVertex = top[index];
                var bottomPoint = Vertices[bottomVertex];
                var topPoint = Vertices[topVertex];
                var theta = TownscaperGridTopology.WrapTheta(
                    bottomPoint.x + TownscaperGridTopology.ShortestThetaDelta(bottomPoint.x, topPoint.x) * 0.5f);
                Vertices[bottomVertex] = new Vector2(theta, yMin);
                Vertices[topVertex] = new Vector2(theta, yMax);
            }
        }

        private bool TryGetQuadParameterPoints(Quad quad, out Vector2[] points)
        {
            points = new Vector2[4];
            var center = CalculateQuadParameterCenter(quad);
            for (var corner = 0; corner < 4; corner++)
            {
                var vertex = Vertices[quad.GetVertex(corner)];
                points[corner] = new Vector2(
                    center.x + TownscaperGridTopology.ShortestThetaDelta(center.x, vertex.x),
                    vertex.y);
            }
            return SignedPolygonArea(points) > 0.000001f || SignedPolygonArea(points) < -0.000001f;
        }

        private Vector2 CalculateQuadParameterCenter(Quad quad)
        {
            var anchor = Vertices[quad.A].x;
            var theta = 0f;
            var y = 0f;
            for (var corner = 0; corner < 4; corner++)
            {
                var point = Vertices[quad.GetVertex(corner)];
                theta += anchor + TownscaperGridTopology.ShortestThetaDelta(anchor, point.x);
                y += point.y;
            }
            return new Vector2(TownscaperGridTopology.WrapTheta(theta * 0.25f), y * 0.25f);
        }

        private float CalculateWaterAnglePenalty()
        {
            var penalty = 0f;
            for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
            {
                Vector2[] points;
                if (!TryGetQuadParameterPoints(waterQuads[quadIndex], out points))
                {
                    penalty += 100000f;
                    continue;
                }

                var area = Mathf.Abs(SignedPolygonArea(points));
                if (area < 0.0001f)
                {
                    penalty += (0.0001f - area) * 100000f;
                }

                for (var corner = 0; corner < 4; corner++)
                {
                    var angle = CalculateCornerAngle(points, corner);
                    if (angle < waterMinimumCornerAngle)
                    {
                        var delta = waterMinimumCornerAngle - angle;
                        penalty += delta * delta;
                    }
                    else if (angle > waterMaximumCornerAngle)
                    {
                        var delta = angle - waterMaximumCornerAngle;
                        penalty += delta * delta;
                    }
                }
            }
            return penalty;
        }

        private bool QuadAnglesAreWithinWaterRange(Vector2[] points)
        {
            for (var corner = 0; corner < 4; corner++)
            {
                var angle = CalculateCornerAngle(points, corner);
                if (angle < waterMinimumCornerAngle || angle > waterMaximumCornerAngle)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryGetRectifiedQuadTargets(Vector2[] points, out Vector2[] targets)
        {
            targets = new Vector2[4];
            var center = (points[0] + points[1] + points[2] + points[3]) * 0.25f;
            var axisU = (points[1] - points[0]) + (points[2] - points[3]);
            var axisV = (points[3] - points[0]) + (points[2] - points[1]);
            if (axisU.sqrMagnitude < 0.000001f)
            {
                axisU = points[2] - points[0];
            }
            if (axisV.sqrMagnitude < 0.000001f)
            {
                axisV = points[3] - points[1];
            }
            if (axisU.sqrMagnitude < 0.000001f || axisV.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            axisU.Normalize();
            axisV -= axisU * Vector2.Dot(axisV, axisU);
            if (axisV.sqrMagnitude < 0.000001f)
            {
                axisV = new Vector2(-axisU.y, axisU.x);
            }
            else
            {
                axisV.Normalize();
            }

            var width = (Mathf.Abs(Vector2.Dot(points[1] - points[0], axisU)) +
                         Mathf.Abs(Vector2.Dot(points[2] - points[3], axisU))) * 0.5f;
            var height = (Mathf.Abs(Vector2.Dot(points[3] - points[0], axisV)) +
                          Mathf.Abs(Vector2.Dot(points[2] - points[1], axisV))) * 0.5f;
            width = Mathf.Max(width, 0.0001f);
            height = Mathf.Max(height, 0.0001f);

            var halfU = axisU * (width * 0.5f);
            var halfV = axisV * (height * 0.5f);
            targets[0] = center - halfU - halfV;
            targets[1] = center + halfU - halfV;
            targets[2] = center + halfU + halfV;
            targets[3] = center - halfU + halfV;
            return true;
        }

        private static float CalculateCornerAngle(Vector2[] points, int corner)
        {
            var previous = points[(corner + 3) % 4] - points[corner];
            var next = points[(corner + 1) % 4] - points[corner];
            if (previous.sqrMagnitude < 0.000001f || next.sqrMagnitude < 0.000001f)
            {
                return 0f;
            }
            return Vector2.Angle(previous, next);
        }

        private static float SignedPolygonArea(Vector2[] points)
        {
            var area = 0f;
            for (var index = 0; index < points.Length; index++)
            {
                var next = (index + 1) % points.Length;
                area += points[index].x * points[next].y - points[next].x * points[index].y;
            }
            return area * 0.5f;
        }

        private static void AccumulateEdgeFlags(
            List<Quad> quads,
            int flag,
            Dictionary<ulong, int> edgeFlags,
            Dictionary<ulong, Vector2Int> edgeVertices,
            Dictionary<ulong, Quad> owners)
        {
            for (var quadIndex = 0; quadIndex < quads.Count; quadIndex++)
            {
                var quad = quads[quadIndex];
                for (var edgeIndex = 0; edgeIndex < 4; edgeIndex++)
                {
                    var a = quad.GetVertex(edgeIndex);
                    var b = quad.GetVertex((edgeIndex + 1) % 4);
                    var min = Mathf.Min(a, b);
                    var max = Mathf.Max(a, b);
                    var key = ((ulong)(uint)min << 32) | (uint)max;
                    int current;
                    edgeFlags.TryGetValue(key, out current);
                    edgeFlags[key] = current | flag;
                    if (!edgeVertices.ContainsKey(key)) edgeVertices.Add(key, new Vector2Int(a, b));
                    if (owners != null && !owners.ContainsKey(key)) owners.Add(key, quad);
                }
            }
        }

        private Vector3 CalculateBoundaryOutwardNormal(Vector2Int edge, Quad pillarOwner)
        {
            var a = MapPlanarVertex(Vertices[edge.x], pillarOwner.Centroid.x, waterHeight);
            var b = MapPlanarVertex(Vertices[edge.y], pillarOwner.Centroid.x, waterHeight);
            var ownerCenter = Vector3.zero;
            for (var corner = 0; corner < 4; corner++)
            {
                ownerCenter += MapPlanarVertex(
                    Vertices[pillarOwner.GetVertex(corner)],
                    pillarOwner.Centroid.x,
                    waterHeight);
            }
            ownerCenter *= 0.25f;

            var edgeDirection = b - a;
            edgeDirection -= transform.up * Vector3.Dot(edgeDirection, transform.up);
            var candidate = Vector3.Cross(transform.up, edgeDirection).normalized;
            var fromOwnerToEdge = (a + b) * 0.5f - ownerCenter;
            if (Vector3.Dot(candidate, fromOwnerToEdge) < 0f) candidate = -candidate;
            return candidate;
        }

        private static bool PointInsidePolygon(Vector2 point, Vector2[] polygon)
        {
            var inside = false;
            for (var i = 0; i < polygon.Length; i++)
            {
                var j = (i + polygon.Length - 1) % polygon.Length;
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                    (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var segment = b - a;
            if (segment.sqrMagnitude < 0.000001f) return Vector2.Distance(point, a);
            var t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude);
            return Vector2.Distance(point, a + segment * t);
        }

        private void BuildPillarMesh()
        {
            EnsureGeneratedRoot();
            var existing = generatedRoot.Find("Covered-Tile Pillar");
            if (existing != null) DestroyRuntimeObject(existing.gameObject);
            var existingLines = generatedRoot.Find("Covered-Tile Pillar Grid Lines");
            if (existingLines != null) DestroyRuntimeObject(existingLines.gameObject);
            var existingHighlightLines = generatedRoot.Find("Covered-Tile Pillar Highlight Grid Lines");
            if (existingHighlightLines != null) DestroyRuntimeObject(existingHighlightLines.gameObject);
            DestroyRuntimeObject(pillarMesh);
            DestroyRuntimeObject(pillarGridMesh);
            DestroyRuntimeObject(pillarHighlightGridMesh);

            var surface = new GameObject("Covered-Tile Pillar", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(GeneratedGridClickSurface));
            surface.transform.SetParent(generatedRoot, false);
            pillarSurfaceObject = surface;
            pillarMesh = BuildExtrudedPillarMesh();
            surface.GetComponent<MeshFilter>().sharedMesh = pillarMesh;
            surface.GetComponent<MeshCollider>().sharedMesh = pillarMesh;
            surface.GetComponent<GeneratedGridClickSurface>().Initialize(this, true);
            if (pillarMaterial == null)
            {
                pillarMaterial = CreatePillarMaterial("Generated Pillar Grid", pillarColor);
            }
            var pillarRenderer = surface.GetComponent<MeshRenderer>();
            pillarRenderer.sharedMaterial = pillarMaterial;
            pillarRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            pillarRenderer.receiveShadows = true;

            var gridLines = new GameObject("Covered-Tile Pillar Grid Lines", typeof(MeshFilter), typeof(MeshRenderer));
            gridLines.transform.SetParent(generatedRoot, false);
            pillarGridLinesObject = gridLines;
            pillarGridMesh = BuildExtrudedPillarGridLines(pillarGridLineWidth, "Covered-Tile Pillar Grid Lines");
            gridLines.GetComponent<MeshFilter>().sharedMesh = pillarGridMesh;
            if (pillarGridMaterial == null)
            {
                pillarGridMaterial = CreateGridMaterial("Pillar Grid Lines", pillarGridColor);
            }
            var gridRenderer = gridLines.GetComponent<MeshRenderer>();
            gridRenderer.sharedMaterial = pillarGridMaterial;
            gridRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gridRenderer.receiveShadows = false;
            gridLines.SetActive(false);

            var highlightLines = new GameObject("Covered-Tile Pillar Highlight Grid Lines", typeof(MeshFilter), typeof(MeshRenderer));
            highlightLines.transform.SetParent(generatedRoot, false);
            pillarHighlightGridLinesObject = highlightLines;
            pillarHighlightGridMesh = BuildExtrudedPillarGridLines(
                pillarHighlightGridLineWidth,
                "Covered-Tile Pillar Highlight Grid Lines",
                true);
            highlightLines.GetComponent<MeshFilter>().sharedMesh = pillarHighlightGridMesh;
            if (pillarHighlightGridMaterial == null)
            {
                pillarHighlightGridMaterial = CreateHighlightGridMaterial("Pillar Highlight Grid Lines", pillarHighlightGridColor, false);
            }
            var highlightRenderer = highlightLines.GetComponent<MeshRenderer>();
            highlightRenderer.sharedMaterial = pillarHighlightGridMaterial;
            highlightRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            highlightRenderer.receiveShadows = false;
            highlightLines.SetActive(false);
        }

        private Mesh BuildExtrudedPillarMesh()
        {
            var sideQuadCount = seamEdges.Count * pillarVerticalBands;
            var quadCount = sideQuadCount + PillarQuads.Count;
            var mesh = new Mesh { name = "Covered-Tile Extruded Pillar" };
            var meshVertices = new Vector3[quadCount * 4];
            var triangles = new int[quadCount * 6];
            var outputQuad = 0;

            for (var edgeIndex = 0; edgeIndex < seamEdges.Count; edgeIndex++)
            {
                var edge = seamEdges[edgeIndex];
                var waterA = MapWaterVertex(Vertices[edge.x]);
                var waterB = MapWaterVertex(Vertices[edge.y]);
                for (var band = 0; band < pillarVerticalBands; band++)
                {
                    var lower = (float)band / pillarVerticalBands;
                    var upper = (float)(band + 1) / pillarVerticalBands;
                    var lowerHeight = Mathf.Lerp(PillarBottomHeight, PillarTopHeight, lower);
                    var upperHeight = Mathf.Lerp(PillarBottomHeight, PillarTopHeight, upper);
                    WriteSurfaceQuad(
                        meshVertices,
                        triangles,
                        outputQuad++,
                        waterA + transform.up * (lowerHeight - waterHeight),
                        waterB + transform.up * (lowerHeight - waterHeight),
                        waterB + transform.up * (upperHeight - waterHeight),
                        waterA + transform.up * (upperHeight - waterHeight),
                        seamOutwardNormals[edgeIndex]);
                }
            }

            for (var quadIndex = 0; quadIndex < PillarQuads.Count; quadIndex++)
            {
                var quad = PillarQuads[quadIndex];
                WriteSurfaceQuad(
                    meshVertices,
                    triangles,
                    outputQuad++,
                    MapPillarTopVertex(Vertices[quad.A]),
                    MapPillarTopVertex(Vertices[quad.B]),
                    MapPillarTopVertex(Vertices[quad.C]),
                    MapPillarTopVertex(Vertices[quad.D]),
                    transform.up);
            }

            mesh.vertices = meshVertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void WriteSurfaceQuad(
            Vector3[] outputVertices,
            int[] outputTriangles,
            int quadIndex,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector3 desiredNormal)
        {
            var vertexBase = quadIndex * 4;
            outputVertices[vertexBase] = generatedRoot.InverseTransformPoint(a);
            outputVertices[vertexBase + 1] = generatedRoot.InverseTransformPoint(b);
            outputVertices[vertexBase + 2] = generatedRoot.InverseTransformPoint(c);
            outputVertices[vertexBase + 3] = generatedRoot.InverseTransformPoint(d);
            var forward = Vector3.Dot(Vector3.Cross(b - a, d - a), desiredNormal) >= 0f;
            var triangleBase = quadIndex * 6;
            outputTriangles[triangleBase] = vertexBase;
            outputTriangles[triangleBase + 1] = forward ? vertexBase + 1 : vertexBase + 2;
            outputTriangles[triangleBase + 2] = forward ? vertexBase + 2 : vertexBase + 1;
            outputTriangles[triangleBase + 3] = vertexBase;
            outputTriangles[triangleBase + 4] = forward ? vertexBase + 2 : vertexBase + 3;
            outputTriangles[triangleBase + 5] = forward ? vertexBase + 3 : vertexBase + 2;
        }

        private void WriteWorldQuad(
            Vector3[] outputVertices,
            int[] outputTriangles,
            int quadIndex,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d)
        {
            var vertexBase = quadIndex * 4;
            outputVertices[vertexBase] = generatedRoot.InverseTransformPoint(a);
            outputVertices[vertexBase + 1] = generatedRoot.InverseTransformPoint(b);
            outputVertices[vertexBase + 2] = generatedRoot.InverseTransformPoint(c);
            outputVertices[vertexBase + 3] = generatedRoot.InverseTransformPoint(d);
            var triangleBase = quadIndex * 12;
            outputTriangles[triangleBase] = vertexBase;
            outputTriangles[triangleBase + 1] = vertexBase + 1;
            outputTriangles[triangleBase + 2] = vertexBase + 2;
            outputTriangles[triangleBase + 3] = vertexBase;
            outputTriangles[triangleBase + 4] = vertexBase + 2;
            outputTriangles[triangleBase + 5] = vertexBase + 3;
            outputTriangles[triangleBase + 6] = vertexBase;
            outputTriangles[triangleBase + 7] = vertexBase + 2;
            outputTriangles[triangleBase + 8] = vertexBase + 1;
            outputTriangles[triangleBase + 9] = vertexBase;
            outputTriangles[triangleBase + 10] = vertexBase + 3;
            outputTriangles[triangleBase + 11] = vertexBase + 2;
        }

        private Mesh BuildExtrudedPillarGridLines(float lineWidth, string meshName, bool onlyAboveWater = false)
        {
            var starts = new List<Vector3>();
            var ends = new List<Vector3>();
            var normals = new List<Vector3>();

            var vertexNormals = new Dictionary<int, Vector3>();
            for (var edgeIndex = 0; edgeIndex < seamEdges.Count; edgeIndex++)
            {
                AccumulateVertexNormal(vertexNormals, seamEdges[edgeIndex].x, seamOutwardNormals[edgeIndex]);
                AccumulateVertexNormal(vertexNormals, seamEdges[edgeIndex].y, seamOutwardNormals[edgeIndex]);
            }

            foreach (var vertexId in seamVertexIds)
            {
                var water = MapWaterVertex(Vertices[vertexId]);
                var bottomHeight = onlyAboveWater ? Mathf.Max(PillarBottomHeight, waterHeight + 0.025f) : PillarBottomHeight;
                var bottom = water + transform.up * (bottomHeight - waterHeight);
                var top = water + transform.up * (PillarTopHeight - waterHeight);
                AddLineSegment(starts, ends, normals, bottom, top, vertexNormals[vertexId].normalized);
            }

            for (var edgeIndex = 0; edgeIndex < seamEdges.Count; edgeIndex++)
            {
                var edge = seamEdges[edgeIndex];
                var waterA = MapWaterVertex(Vertices[edge.x]);
                var waterB = MapWaterVertex(Vertices[edge.y]);
                if (onlyAboveWater)
                {
                    var anchoredA = waterA + transform.up * 0.025f;
                    var anchoredB = waterB + transform.up * 0.025f;
                    AddLineSegment(starts, ends, normals, anchoredA, anchoredB, seamOutwardNormals[edgeIndex]);
                }

                for (var level = 0; level <= pillarVerticalBands; level++)
                {
                    var height = Mathf.Lerp(PillarBottomHeight, PillarTopHeight, (float)level / pillarVerticalBands);
                    if (onlyAboveWater && height < waterHeight + 0.025f)
                    {
                        continue;
                    }
                    var a = waterA + transform.up * (height - waterHeight);
                    var b = waterB + transform.up * (height - waterHeight);
                    AddLineSegment(starts, ends, normals, a, b, seamOutwardNormals[edgeIndex]);
                }
            }

            var topEdges = new HashSet<ulong>();
            for (var quadIndex = 0; quadIndex < PillarQuads.Count; quadIndex++)
            {
                var quad = PillarQuads[quadIndex];
                for (var edgeIndex = 0; edgeIndex < 4; edgeIndex++)
                {
                    var aId = quad.GetVertex(edgeIndex);
                    var bId = quad.GetVertex((edgeIndex + 1) % 4);
                    var min = Mathf.Min(aId, bId);
                    var max = Mathf.Max(aId, bId);
                    var key = ((ulong)(uint)min << 32) | (uint)max;
                    if (topEdges.Add(key))
                    {
                        AddLineSegment(
                            starts,
                            ends,
                            normals,
                            MapPillarTopVertex(Vertices[aId]),
                            MapPillarTopVertex(Vertices[bId]),
                            transform.up);
                    }
                }
            }

            return BuildWorldLineMesh(starts, ends, normals, lineWidth, 0.018f, meshName);
        }

        private static void AccumulateVertexNormal(Dictionary<int, Vector3> normals, int vertexId, Vector3 normal)
        {
            Vector3 current;
            normals.TryGetValue(vertexId, out current);
            normals[vertexId] = current + normal;
        }

        private Vector3 FootprintNormal(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            local.y = 0f;
            return transform.TransformDirection(local.sqrMagnitude > 0.000001f ? local.normalized : Vector3.forward);
        }

        private static void AddLineSegment(
            List<Vector3> starts,
            List<Vector3> ends,
            List<Vector3> normals,
            Vector3 start,
            Vector3 end,
            Vector3 normal)
        {
            starts.Add(start);
            ends.Add(end);
            normals.Add(normal);
        }

        private Mesh BuildWorldLineMesh(
            List<Vector3> starts,
            List<Vector3> ends,
            List<Vector3> normals,
            float lineWidth,
            float surfaceOffset,
            string meshName)
        {
            var mesh = new Mesh { name = meshName };
            var maximumQuadCount = starts.Count * 3;
            var lineVertices = new Vector3[maximumQuadCount * 4];
            var waterSampleCoordinates = new Vector2[maximumQuadCount * 4];
            var triangles = new int[maximumQuadCount * 12];
            var halfWidth = Mathf.Max(0.0005f, lineWidth * 0.5f);
            var outputQuad = 0;
            var jointCenters = new List<Vector3>();
            var jointNormals = new List<Vector3>();
            var jointKeys = new HashSet<Vector3Int>();
            for (var lineIndex = 0; lineIndex < starts.Count; lineIndex++)
            {
                var segment = ends[lineIndex] - starts[lineIndex];
                if (segment.sqrMagnitude < 0.000001f)
                {
                    continue;
                }
                var normal = normals[lineIndex].normalized;
                var direction = segment.normalized;
                var side = Vector3.Cross(normal, direction).normalized * halfWidth;
                var sampleStart = starts[lineIndex];
                var sampleEnd = ends[lineIndex];
                var start = starts[lineIndex] + normal * surfaceOffset;
                var end = ends[lineIndex] + normal * surfaceOffset;
                WriteWorldQuad(
                    lineVertices,
                    triangles,
                    outputQuad++,
                    start - side,
                    start + side,
                    end + side,
                    end - side);
                var vertexBase = (outputQuad - 1) * 4;
                waterSampleCoordinates[vertexBase] = new Vector2(sampleStart.x, sampleStart.z);
                waterSampleCoordinates[vertexBase + 1] = new Vector2(sampleStart.x, sampleStart.z);
                waterSampleCoordinates[vertexBase + 2] = new Vector2(sampleEnd.x, sampleEnd.z);
                waterSampleCoordinates[vertexBase + 3] = new Vector2(sampleEnd.x, sampleEnd.z);
                RegisterWorldLineJoint(jointCenters, jointNormals, jointKeys, sampleStart, normal);
                RegisterWorldLineJoint(jointCenters, jointNormals, jointKeys, sampleEnd, normal);
            }
            for (var jointIndex = 0; jointIndex < jointCenters.Count; jointIndex++)
            {
                WriteWorldJointVertices(
                    lineVertices,
                    waterSampleCoordinates,
                    triangles,
                    outputQuad++,
                    jointCenters[jointIndex],
                    jointNormals[jointIndex],
                    halfWidth,
                    surfaceOffset);
            }

            Array.Resize(ref lineVertices, outputQuad * 4);
            Array.Resize(ref waterSampleCoordinates, outputQuad * 4);
            Array.Resize(ref triangles, outputQuad * 12);

            mesh.vertices = lineVertices;
            mesh.uv2 = waterSampleCoordinates;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ValidateLineMeshWaterSamples(mesh);
            return mesh;
        }

        private void WriteWorldJointVertices(
            Vector3[] lineVertices,
            Vector2[] waterSampleCoordinates,
            int[] triangles,
            int outputQuad,
            Vector3 center,
            Vector3 normal,
            float halfWidth,
            float surfaceOffset)
        {
            normal = normal.normalized;
            if (normal.sqrMagnitude < 0.000001f)
            {
                normal = transform.up;
            }

            var tangentA = Vector3.Cross(normal, transform.up);
            if (tangentA.sqrMagnitude < 0.000001f)
            {
                tangentA = transform.right;
            }
            tangentA = tangentA.normalized * halfWidth;
            var tangentB = Vector3.Cross(normal, tangentA).normalized * halfWidth;
            var displayedCenter = center + normal * surfaceOffset;
            var vertexBase = outputQuad * 4;
            lineVertices[vertexBase] = generatedRoot.InverseTransformPoint(displayedCenter - tangentA - tangentB);
            lineVertices[vertexBase + 1] = generatedRoot.InverseTransformPoint(displayedCenter + tangentA - tangentB);
            lineVertices[vertexBase + 2] = generatedRoot.InverseTransformPoint(displayedCenter + tangentA + tangentB);
            lineVertices[vertexBase + 3] = generatedRoot.InverseTransformPoint(displayedCenter - tangentA + tangentB);
            waterSampleCoordinates[vertexBase] = new Vector2(center.x, center.z);
            waterSampleCoordinates[vertexBase + 1] = new Vector2(center.x, center.z);
            waterSampleCoordinates[vertexBase + 2] = new Vector2(center.x, center.z);
            waterSampleCoordinates[vertexBase + 3] = new Vector2(center.x, center.z);
            var triangleBase = outputQuad * 12;
            triangles[triangleBase] = vertexBase;
            triangles[triangleBase + 1] = vertexBase + 1;
            triangles[triangleBase + 2] = vertexBase + 2;
            triangles[triangleBase + 3] = vertexBase;
            triangles[triangleBase + 4] = vertexBase + 2;
            triangles[triangleBase + 5] = vertexBase + 3;
            triangles[triangleBase + 6] = vertexBase;
            triangles[triangleBase + 7] = vertexBase + 2;
            triangles[triangleBase + 8] = vertexBase + 1;
            triangles[triangleBase + 9] = vertexBase;
            triangles[triangleBase + 10] = vertexBase + 3;
            triangles[triangleBase + 11] = vertexBase + 2;
        }

        private static void RegisterWorldLineJoint(
            List<Vector3> centers,
            List<Vector3> normals,
            HashSet<Vector3Int> keys,
            Vector3 center,
            Vector3 normal)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(center.x * 1000f),
                Mathf.RoundToInt(center.y * 1000f),
                Mathf.RoundToInt(center.z * 1000f));
            if (keys.Add(key))
            {
                centers.Add(center);
                normals.Add(normal);
            }
            else
            {
                var index = centers.FindIndex(existing => Vector3.SqrMagnitude(existing - center) < 0.000001f);
                if (index >= 0)
                {
                    normals[index] = (normals[index] + normal).normalized;
                }
            }
        }

        private void Update()
        {
            UpdatePillarHighlightGrid();
            HandlePlacementInput();
        }

        private void ConfigureRealtimeLighting()
        {
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowDistance = 260f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowNearPlaneOffset = 1.5f;

            realtimeSunLight = RenderSettings.sun;
            if (realtimeSunLight == null)
            {
                var lights = FindObjectsOfType<Light>();
                for (var index = 0; index < lights.Length; index++)
                {
                    if (lights[index] != null && lights[index].type == LightType.Directional)
                    {
                        realtimeSunLight = lights[index];
                        break;
                    }
                }
            }
            if (realtimeSunLight == null)
            {
                var sunObject = new GameObject("V2 Realtime Shadow Sun");
                realtimeSunLight = sunObject.AddComponent<Light>();
                realtimeSunLight.type = LightType.Directional;
            }

            realtimeSunLight.name = "V2 Realtime Shadow Sun";
            realtimeSunLight.enabled = true;
            realtimeSunLight.type = LightType.Directional;
            realtimeSunLight.transform.rotation = Quaternion.Euler(38f, -42f, 0f);
            realtimeSunLight.color = new Color(1f, 0.94f, 0.82f, 1f);
            realtimeSunLight.intensity = 1.08f;
            realtimeSunLight.shadows = LightShadows.Soft;
            realtimeSunLight.shadowStrength = 0.82f;
            realtimeSunLight.shadowBias = 0.035f;
            realtimeSunLight.shadowNormalBias = 0.35f;
            realtimeSunLight.shadowNearPlane = 0.08f;
            realtimeSunLight.shadowCustomResolution = 4096;
            RenderSettings.sun = realtimeSunLight;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.42f, 0.48f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.21f, 0.25f, 0.28f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.09f, 0.10f, 1f);
            RenderSettings.ambientIntensity = 0.72f;

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.depthTextureMode |= DepthTextureMode.Depth;
            }
        }

        private void UpdatePillarHighlightGrid()
        {
            if (pillarSurfaceObject == null || pillarHighlightGridLinesObject == null || pillarHighlightGridMaterial == null)
            {
                return;
            }

            if (waterGenerator != null)
            {
                waterGenerator.RefreshDynamicWaterBindings(
                    pillarMaterial,
                    pillarHighlightGridMaterial,
                    transform.TransformPoint(new Vector3(0f, waterHeight, 0f)).y);
            }
            else
            {
                pillarHighlightGridMaterial.SetFloat("_WaterLevel", transform.TransformPoint(new Vector3(0f, waterHeight, 0f)).y);
                pillarHighlightGridMaterial.SetFloat("_ClipBelowDynamicWater", 1f);
            }

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                globalHighlightUntil = Time.unscaledTime + 0.4f;
            }

            if (Input.GetKey(KeyCode.Backspace) || Time.unscaledTime < globalHighlightUntil)
            {
                SetHighlightMaterial(pillarHighlightGridMaterial, Vector3.zero, 10000f, 1000f, 1f);
                pillarHighlightGridLinesObject.SetActive(true);
                HideActiveCellOutline();
                if (waterGenerator != null)
                {
                    waterGenerator.ShowGlobalHoverGrid();
                }
                return;
            }

            pillarHighlightGridLinesObject.SetActive(false);
            if (waterGenerator != null) waterGenerator.HideHoverGrid();

            GridSurfaceHit gridHit;
            if (TryGetCurrentGridHit(out gridHit))
            {
                ShowActiveCellOutline(gridHit);
            }
            else
            {
                HideActiveCellOutline();
            }
        }

        private void HandlePlacementInput()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                placementMode = PlacementMode.Terrain;
            }
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                placementMode = PlacementMode.Building;
            }

            if (Input.GetMouseButtonDown(0))
            {
                leftMouseDownPosition = Input.mousePosition;
                leftMouseCandidate = true;
            }
            if (Input.GetMouseButtonDown(1))
            {
                rightMouseDownPosition = Input.mousePosition;
                rightMouseCandidate = true;
            }

            if (leftMouseCandidate && Input.GetMouseButton(0) &&
                Vector2.Distance(leftMouseDownPosition, Input.mousePosition) > ClickDragThresholdPixels)
            {
                leftMouseCandidate = false;
            }
            if (rightMouseCandidate && Input.GetMouseButton(1) &&
                Vector2.Distance(rightMouseDownPosition, Input.mousePosition) > ClickDragThresholdPixels)
            {
                rightMouseCandidate = false;
            }

            if (leftMouseCandidate && Input.GetMouseButtonUp(0))
            {
                leftMouseCandidate = false;
                if (!IsMouseOverModeBar())
                {
                    GridSurfaceHit hit;
                    if (TryGetCurrentGridHit(out hit))
                    {
                        PlaceCell(hit);
                    }
                }
            }

            if (rightMouseCandidate && Input.GetMouseButtonUp(1))
            {
                rightMouseCandidate = false;
                if (!IsMouseOverModeBar())
                {
                    GridSurfaceHit hit;
                    if (TryGetCurrentGridHit(out hit))
                    {
                        RemoveCell(hit);
                    }
                }
            }
        }

        private void OnGUI()
        {
            const float x = 18f;
            var y = Screen.height - 88f;
            var terrainRect = new Rect(x, y, 118f, 54f);
            var buildingRect = new Rect(x + 126f, y, 118f, 54f);
            DrawModeSlot(terrainRect, "~ 地形", placementMode == PlacementMode.Terrain);
            DrawModeSlot(buildingRect, "1 建筑", placementMode == PlacementMode.Building);
        }

        private void DrawModeSlot(Rect rect, string label, bool selected)
        {
            var oldColor = GUI.color;
            GUI.color = selected ? new Color(1f, 0.88f, 0.36f, 0.92f) : new Color(0.08f, 0.08f, 0.08f, 0.72f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = selected ? Color.black : Color.white;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 9f, rect.width - 24f, 22f), label);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 29f, rect.width - 24f, 18f), selected ? "当前" : "切换");
            GUI.color = oldColor;
        }

        private static bool IsMouseOverModeBar()
        {
            var mouse = Input.mousePosition;
            return mouse.x >= 12f && mouse.x <= 270f && mouse.y <= 96f;
        }

        private bool TryGetCurrentGridHit(out GridSurfaceHit gridHit)
        {
            gridHit = default(GridSurfaceHit);
            var camera = Camera.main;
            if (camera == null || IsMouseOverModeBar())
            {
                return false;
            }

            var ray = camera.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 2000f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            var selectedIndex = -1;
            var selectedPlacedSurface = default(GeneratedPlacedCellSurface);
            var selectedGridSurface = default(GeneratedGridClickSurface);
            var selectedDistance = float.MaxValue;
            for (var index = 0; index < hits.Length; index++)
            {
                var placedSurfaceCandidate = hits[index].collider.GetComponent<GeneratedPlacedCellSurface>();
                if (placedSurfaceCandidate != null &&
                    placedSurfaceCandidate.Generator == this &&
                    hits[index].distance < selectedDistance)
                {
                    selectedIndex = index;
                    selectedDistance = hits[index].distance;
                    selectedPlacedSurface = placedSurfaceCandidate;
                    selectedGridSurface = null;
                    continue;
                }

                var gridSurfaceCandidate = hits[index].collider.GetComponent<GeneratedGridClickSurface>();
                if (gridSurfaceCandidate != null &&
                    gridSurfaceCandidate.Generator == this &&
                    hits[index].distance < selectedDistance)
                {
                    selectedIndex = index;
                    selectedDistance = hits[index].distance;
                    selectedPlacedSurface = null;
                    selectedGridSurface = gridSurfaceCandidate;
                }
            }
            if (selectedIndex < 0)
            {
                return false;
            }

            var hit = hits[selectedIndex];
            var meshFilter = hit.collider.GetComponent<MeshFilter>();
            var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null)
            {
                return false;
            }

            var meshVertices = mesh.vertices;
            var corners = new Vector3[4];
            var meshQuadIndex = hit.triangleIndex / 2;
            if (selectedPlacedSurface != null)
            {
                var face = meshQuadIndex;
                if (face < 0 || face >= PlacedCellFaceVertexIndices.GetLength(0))
                {
                    return false;
                }
                for (var corner = 0; corner < 4; corner++)
                {
                    corners[corner] = hit.collider.transform.TransformPoint(meshVertices[PlacedCellFaceVertexIndices[face, corner]]);
                }
            }
            else
            {
                var vertexBase = meshQuadIndex * 4;
                if (vertexBase < 0 || vertexBase + 3 >= meshVertices.Length)
                {
                    return false;
                }
                for (var corner = 0; corner < 4; corner++)
                {
                    corners[corner] = hit.collider.transform.TransformPoint(meshVertices[vertexBase + corner]);
                }
            }

            var normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
            if (normal.sqrMagnitude < 0.000001f)
            {
                normal = hit.normal;
            }
            else
            {
                normal.Normalize();
                if (Vector3.Dot(normal, hit.normal) < 0f)
                {
                    normal = -normal;
                    Array.Reverse(corners);
                }
            }

            gridHit = new GridSurfaceHit
            {
                Collider = hit.collider,
                MeshQuadIndex = meshQuadIndex,
                IsPillar = selectedGridSurface != null && selectedGridSurface.IsPillarSurface,
                PlacedCell = selectedPlacedSurface,
                Corners = corners,
                Normal = normal,
                Key = selectedPlacedSurface != null
                    ? selectedPlacedSurface.PlacementKey + ":face:" + meshQuadIndex
                    : hit.collider.GetInstanceID() + ":" + meshQuadIndex
            };
            ResolveUnifiedPlacementCell(ref gridHit);
            return true;
        }

        private void ShowActiveCellOutline(GridSurfaceHit hit)
        {
            EnsureGeneratedRoot();
            if (activeCellOutlineObject == null)
            {
                activeCellOutlineObject = new GameObject("Active Grid Cell Rounded Outline", typeof(MeshFilter), typeof(MeshRenderer));
                activeCellOutlineObject.transform.SetParent(generatedRoot, false);
            }

            if (activeCellOutlineMaterial == null)
            {
                activeCellOutlineMaterial = CreateOverlayLineMaterial("Active Grid Cell Outline", activeCellColor);
            }

            DestroyRuntimeObject(activeCellOutlineMesh);
            activeCellOutlineMesh = BuildRoundedCellOutlineMesh(
                hit.Corners,
                hit.Normal,
                activeCellLineWidth,
                activeCellSurfaceOffset,
                activeCellCornerSegments);
            activeCellOutlineObject.GetComponent<MeshFilter>().sharedMesh = activeCellOutlineMesh;
            activeCellOutlineObject.GetComponent<MeshRenderer>().sharedMaterial = activeCellOutlineMaterial;
            activeCellOutlineObject.SetActive(true);
        }

        private void HideActiveCellOutline()
        {
            if (activeCellOutlineObject != null)
            {
                activeCellOutlineObject.SetActive(false);
            }
        }

        private Mesh BuildRoundedCellOutlineMesh(
            Vector3[] corners,
            Vector3 normal,
            float lineWidth,
            float surfaceOffset,
            int cornerSegments)
        {
            var outline = new List<Vector3>();
            var segments = Mathf.Max(1, cornerSegments);
            for (var cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                var previous = corners[(cornerIndex + 3) % 4];
                var current = corners[cornerIndex];
                var next = corners[(cornerIndex + 1) % 4];
                var previousLength = Vector3.Distance(current, previous);
                var nextLength = Vector3.Distance(current, next);
                var radius = Mathf.Min(previousLength, nextLength) * 0.22f;
                var start = current + (previous - current).normalized * radius;
                var end = current + (next - current).normalized * radius;
                if (outline.Count == 0)
                {
                    outline.Add(start);
                }
                else
                {
                    outline.Add(start);
                }
                for (var step = 1; step <= segments; step++)
                {
                    var t = (float)step / segments;
                    outline.Add(QuadraticBezier(start, current, end, t));
                }
            }

            return BuildPolylineMesh(outline, true, normal, lineWidth, surfaceOffset, "Rounded Active Cell Outline");
        }

        private Mesh BuildPolylineMesh(
            List<Vector3> points,
            bool closed,
            Vector3 normal,
            float lineWidth,
            float surfaceOffset,
            string meshName)
        {
            var segmentCount = closed ? points.Count : Mathf.Max(0, points.Count - 1);
            var mesh = new Mesh { name = meshName };
            var vertices = new Vector3[segmentCount * 4];
            var triangles = new int[segmentCount * 6];
            var halfWidth = Mathf.Max(0.0005f, lineWidth * 0.5f);
            var output = 0;
            for (var index = 0; index < segmentCount; index++)
            {
                var start = points[index];
                var end = points[(index + 1) % points.Count];
                var segment = end - start;
                if (segment.sqrMagnitude < 0.000001f)
                {
                    continue;
                }

                var side = Vector3.Cross(normal, segment.normalized).normalized * halfWidth;
                start += normal * surfaceOffset;
                end += normal * surfaceOffset;
                var vertexBase = output * 4;
                vertices[vertexBase] = generatedRoot.InverseTransformPoint(start - side);
                vertices[vertexBase + 1] = generatedRoot.InverseTransformPoint(start + side);
                vertices[vertexBase + 2] = generatedRoot.InverseTransformPoint(end + side);
                vertices[vertexBase + 3] = generatedRoot.InverseTransformPoint(end - side);
                var triangleBase = output * 6;
                triangles[triangleBase] = vertexBase;
                triangles[triangleBase + 1] = vertexBase + 1;
                triangles[triangleBase + 2] = vertexBase + 2;
                triangles[triangleBase + 3] = vertexBase;
                triangles[triangleBase + 4] = vertexBase + 2;
                triangles[triangleBase + 5] = vertexBase + 3;
                output++;
            }

            Array.Resize(ref vertices, output * 4);
            Array.Resize(ref triangles, output * 6);
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            var oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * a + 2f * oneMinusT * t * b + t * t * c;
        }

        private void ResolveUnifiedPlacementCell(ref GridSurfaceHit hit)
        {
            if (hit.PlacedCell != null)
            {
                ResolvePlacedPlacementCell(ref hit);
                return;
            }

            if (hit.IsPillar)
            {
                ResolvePillarPlacementCell(ref hit);
                return;
            }

            ResolveWaterPlacementCell(ref hit);
        }

        private void ResolvePlacedPlacementCell(ref GridSurfaceHit hit)
        {
            int quadId;
            int tileX;
            int tileZ;
            int band;
            if (!TryParseUnifiedCellKey(hit.PlacedCell.PlacementKey, out quadId, out tileX, out tileZ, out band))
            {
                return;
            }

            Quad sourceQuad;
            if (!TryFindQuadById(quadId, out sourceQuad))
            {
                return;
            }

            var targetQuad = sourceQuad;
            var targetTileX = tileX;
            var targetTileZ = tileZ;
            var targetBand = band;

            if (hit.MeshQuadIndex == 0)
            {
                targetBand = band - 1;
            }
            else if (hit.MeshQuadIndex == 1)
            {
                targetBand = band + 1;
            }
            else if (hit.MeshQuadIndex >= 2 && hit.MeshQuadIndex <= 5)
            {
                var edgeIndex = hit.MeshQuadIndex - 2;
                if (!TryFindAdjacentQuadAcrossEdge(sourceQuad, edgeIndex, out targetQuad, ref targetTileX, ref targetTileZ))
                {
                    return;
                }
            }
            else
            {
                return;
            }

            if (targetBand < 0 || targetBand >= pillarVerticalBands)
            {
                return;
            }

            hit.PlacementCellKey = BuildUnifiedCellKey(targetQuad.Id, targetTileX, targetTileZ, targetBand);
            hit.HasPlacementCell = TryBuildUnifiedCellCorners(
                targetQuad,
                targetTileX,
                targetTileZ,
                targetBand,
                BandOverlapsWaterSurface(targetBand),
                out hit.PlacementCellCorners);
        }

        private void ResolveWaterPlacementCell(ref GridSurfaceHit hit)
        {
            Quad sourceQuad;
            int tileX;
            int tileZ;
            if (!TryResolveWaterOutputQuad(hit.MeshQuadIndex, out sourceQuad, out tileX, out tileZ))
            {
                return;
            }

            var band = GetWaterAdjacentBandIndex();
            hit.PlacementCellKey = BuildUnifiedCellKey(sourceQuad.Id, tileX, tileZ, band);
            hit.HasPlacementCell = TryBuildUnifiedCellCorners(sourceQuad, tileX, tileZ, band, true, out hit.PlacementCellCorners);
        }

        private void ResolvePillarPlacementCell(ref GridSurfaceHit hit)
        {
            var sideQuadCount = seamEdges.Count * pillarVerticalBands;
            if (hit.MeshQuadIndex < 0 || hit.MeshQuadIndex >= sideQuadCount)
            {
                return;
            }

            var edgeIndex = hit.MeshQuadIndex / pillarVerticalBands;
            var band = hit.MeshQuadIndex % pillarVerticalBands;
            if (BandOverlapsWaterSurface(band))
            {
                band = GetWaterAdjacentBandIndex();
            }

            Quad adjacentWaterQuad;
            if (!TryFindWaterQuadForSeamEdge(seamEdges[edgeIndex], out adjacentWaterQuad))
            {
                return;
            }

            hit.PlacementCellKey = BuildUnifiedCellKey(adjacentWaterQuad.Id, 0, 0, band);
            hit.HasPlacementCell = TryBuildUnifiedCellCorners(adjacentWaterQuad, 0, 0, band, BandOverlapsWaterSurface(band), out hit.PlacementCellCorners);
        }

        private bool TryResolveWaterOutputQuad(int outputQuadIndex, out Quad sourceQuad, out int tileX, out int tileZ)
        {
            sourceQuad = null;
            tileX = 0;
            tileZ = 0;
            var repeat = Mathf.Max(1, waterTileRepeat);
            var startTile = -repeat / 2;
            var endTile = startTile + repeat - 1;
            var cursor = 0;
            for (var z = startTile; z <= endTile; z++)
            {
                for (var x = startTile; x <= endTile; x++)
                {
                    var source = x == 0 && z == 0 ? waterQuads : Quads;
                    if (outputQuadIndex >= cursor && outputQuadIndex < cursor + source.Count)
                    {
                        sourceQuad = source[outputQuadIndex - cursor];
                        tileX = x;
                        tileZ = z;
                        return true;
                    }
                    cursor += source.Count;
                }
            }
            return false;
        }

        private bool TryFindWaterQuadForSeamEdge(Vector2Int seamEdge, out Quad waterQuad)
        {
            var seamKey = EdgeKey(seamEdge.x, seamEdge.y);
            for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
            {
                var quad = waterQuads[quadIndex];
                for (var edgeIndex = 0; edgeIndex < 4; edgeIndex++)
                {
                    if (EdgeKey(quad.GetVertex(edgeIndex), quad.GetVertex((edgeIndex + 1) % 4)) == seamKey)
                    {
                        waterQuad = quad;
                        return true;
                    }
                }
            }

            waterQuad = null;
            return false;
        }

        private bool TryFindQuadById(int quadId, out Quad quad)
        {
            for (var index = 0; index < Quads.Count; index++)
            {
                if (Quads[index].Id == quadId)
                {
                    quad = Quads[index];
                    return true;
                }
            }

            quad = null;
            return false;
        }

        private bool TryFindAdjacentQuadAcrossEdge(Quad sourceQuad, int edgeIndex, out Quad adjacentQuad, ref int tileX, ref int tileZ)
        {
            var a = sourceQuad.GetVertex(edgeIndex);
            var b = sourceQuad.GetVertex((edgeIndex + 1) % 4);
            var edgeKey = EdgeKey(a, b);
            for (var quadIndex = 0; quadIndex < Quads.Count; quadIndex++)
            {
                var candidate = Quads[quadIndex];
                if (candidate.Id == sourceQuad.Id)
                {
                    continue;
                }

                for (var candidateEdge = 0; candidateEdge < 4; candidateEdge++)
                {
                    if (EdgeKey(candidate.GetVertex(candidateEdge), candidate.GetVertex((candidateEdge + 1) % 4)) == edgeKey)
                    {
                        adjacentQuad = candidate;
                        return true;
                    }
                }
            }

            adjacentQuad = null;
            return false;
        }

        private static bool TryParseUnifiedCellKey(string key, out int quadId, out int tileX, out int tileZ, out int band)
        {
            quadId = 0;
            tileX = 0;
            tileZ = 0;
            band = 0;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            var parts = key.Split(':');
            return parts.Length == 5 &&
                   string.Equals(parts[0], "grid-cell", StringComparison.Ordinal) &&
                   int.TryParse(parts[1], out quadId) &&
                   int.TryParse(parts[2], out tileX) &&
                   int.TryParse(parts[3], out tileZ) &&
                   int.TryParse(parts[4], out band);
        }

        private int GetWaterAdjacentBandIndex()
        {
            var t = Mathf.InverseLerp(PillarBottomHeight, PillarTopHeight, waterHeight);
            return Mathf.Clamp(Mathf.FloorToInt(t * pillarVerticalBands), 0, pillarVerticalBands - 1);
        }

        private bool BandOverlapsWaterSurface(int band)
        {
            var lower = GetBandLowerHeight(band);
            var upper = GetBandUpperHeight(band);
            return waterHeight >= lower - 0.001f && waterHeight <= upper + 0.001f;
        }

        private float GetBandLowerHeight(int band)
        {
            return Mathf.Lerp(PillarBottomHeight, PillarTopHeight, Mathf.Clamp01((float)band / pillarVerticalBands));
        }

        private float GetBandUpperHeight(int band)
        {
            return Mathf.Lerp(PillarBottomHeight, PillarTopHeight, Mathf.Clamp01((float)(band + 1) / pillarVerticalBands));
        }

        private static string BuildUnifiedCellKey(int quadId, int tileX, int tileZ, int band)
        {
            return "grid-cell:" + quadId + ":" + tileX + ":" + tileZ + ":" + band;
        }

        private bool TryBuildUnifiedCellCorners(
            Quad footprintQuad,
            int tileX,
            int tileZ,
            int band,
            bool clampLowerToWater,
            out Vector3[] cellCorners)
        {
            cellCorners = null;
            if (footprintQuad == null)
            {
                return false;
            }

            var lowerHeight = clampLowerToWater ? Mathf.Max(waterHeight, GetBandLowerHeight(band)) : GetBandLowerHeight(band);
            var upperHeight = Mathf.Max(lowerHeight + 0.05f, GetBandUpperHeight(band));
            var lowerSurfaceOffset = clampLowerToWater ? placementSurfaceOffset : 0f;
            cellCorners = new Vector3[8];
            for (var corner = 0; corner < 4; corner++)
            {
                var vertexId = footprintQuad.GetVertex(corner);
                cellCorners[corner] = MapWaterTileVertex(vertexId, footprintQuad.Centroid.x, tileX, tileZ) +
                                      transform.up * (lowerHeight - waterHeight + lowerSurfaceOffset);
                cellCorners[corner + 4] = MapWaterTileVertex(vertexId, footprintQuad.Centroid.x, tileX, tileZ) +
                                          transform.up * (upperHeight - waterHeight);
            }
            return true;
        }

        private void EnforcePlacementFootprintMinimumAngle(Vector3[] cellCorners, float minimumAngle)
        {
            if (cellCorners == null || cellCorners.Length < 8)
            {
                return;
            }

            var original = new Vector2[4];
            var lowerLocal = new Vector3[4];
            var upperLocal = new Vector3[4];
            for (var corner = 0; corner < 4; corner++)
            {
                lowerLocal[corner] = transform.InverseTransformPoint(cellCorners[corner]);
                upperLocal[corner] = transform.InverseTransformPoint(cellCorners[corner + 4]);
                original[corner] = new Vector2(lowerLocal[corner].x, lowerLocal[corner].z);
            }

            var originalMinimum = 180f;
            for (var corner = 0; corner < 4; corner++)
            {
                originalMinimum = Mathf.Min(originalMinimum, CalculateCornerAngle(original, corner));
            }
            if (originalMinimum >= minimumAngle)
            {
                return;
            }

            Vector2[] rectified;
            if (!TryGetRectifiedQuadTargets(original, out rectified))
            {
                return;
            }

            var adjusted = new Vector2[4];
            var accepted = false;
            for (var step = 1; step <= 20 && !accepted; step++)
            {
                var blend = step / 20f;
                var adjustedMinimum = 180f;
                for (var corner = 0; corner < 4; corner++)
                {
                    adjusted[corner] = Vector2.Lerp(original[corner], rectified[corner], blend);
                }
                if (Mathf.Sign(SignedPolygonArea(adjusted)) != Mathf.Sign(SignedPolygonArea(original)))
                {
                    continue;
                }
                for (var corner = 0; corner < 4; corner++)
                {
                    adjustedMinimum = Mathf.Min(adjustedMinimum, CalculateCornerAngle(adjusted, corner));
                }
                accepted = adjustedMinimum >= minimumAngle;
            }
            if (!accepted)
            {
                return;
            }

            for (var corner = 0; corner < 4; corner++)
            {
                lowerLocal[corner].x = adjusted[corner].x;
                lowerLocal[corner].z = adjusted[corner].y;
                upperLocal[corner].x = adjusted[corner].x;
                upperLocal[corner].z = adjusted[corner].y;
                cellCorners[corner] = transform.TransformPoint(lowerLocal[corner]);
                cellCorners[corner + 4] = transform.TransformPoint(upperLocal[corner]);
            }
        }

        private void PlaceCell(GridSurfaceHit hit)
        {
            EnsurePlacementRoot();
            if (hit.PlacedCell != null && !hit.HasPlacementCell)
            {
                return;
            }

            var isBuilding = placementMode == PlacementMode.Building;
            var material = isBuilding ? GetBuildingPlacementMaterial() : GetTerrainPlacementMaterial();
            var placementKey = hit.HasPlacementCell ? hit.PlacementCellKey : hit.Key;
            RemoveCellByKey(placementKey);

            var mesh = hit.HasPlacementCell
                ? BuildPlacedCellMesh(hit.PlacementCellCorners, isBuilding ? "Placed Building Grid Cell" : "Placed Terrain Grid Cell")
                : BuildPlacedCellMesh(
                    hit.Corners,
                    hit.Normal,
                    placementSurfaceOffset,
                    CalculateAdjacentCellDepth(hit.Corners) * placementDepthScale,
                    isBuilding ? "Placed Building Cell Fill" : "Placed Terrain Cell Fill");

            var placed = new GameObject(
                isBuilding ? "Placed Building Cell Fill" : "Placed Terrain Cell Fill",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(MeshCollider),
                typeof(GeneratedPlacedCellSurface));
            placed.transform.SetParent(placementRoot, false);
            placed.GetComponent<MeshFilter>().sharedMesh = mesh;
            var placedRenderer = placed.GetComponent<MeshRenderer>();
            placedRenderer.sharedMaterial = material;
            placedRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            placedRenderer.receiveShadows = true;
            placed.GetComponent<MeshCollider>().sharedMesh = mesh;
            placed.GetComponent<GeneratedPlacedCellSurface>().Initialize(this, placementKey);
            placedCells[placementKey] = placed;
        }

        private void RemoveCell(GridSurfaceHit hit)
        {
            var key = hit.PlacedCell != null ? hit.PlacedCell.PlacementKey : hit.HasPlacementCell ? hit.PlacementCellKey : hit.Key;
            RemoveCellByKey(key);
        }

        private void RemoveCellByKey(string key)
        {
            GameObject existing;
            if (!placedCells.TryGetValue(key, out existing))
            {
                return;
            }

            placedCells.Remove(key);
            var meshFilter = existing.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                DestroyRuntimeObject(meshFilter.sharedMesh);
            }
            DestroyRuntimeObject(existing);
        }

        private void ClearRuntimePlacements()
        {
            foreach (var entry in placedCells)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                var meshFilter = entry.Value.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    DestroyRuntimeObject(meshFilter.sharedMesh);
                }
                DestroyRuntimeObject(entry.Value);
            }
            placedCells.Clear();
            if (placementRoot != null)
            {
                DestroyRuntimeObject(placementRoot.gameObject);
                placementRoot = null;
            }
        }

        private void EnsurePlacementRoot()
        {
            EnsureGeneratedRoot();
            if (placementRoot != null)
            {
                return;
            }

            var existing = generatedRoot.Find("Runtime Placed Cells");
            placementRoot = existing != null ? existing : new GameObject("Runtime Placed Cells").transform;
            placementRoot.SetParent(generatedRoot, false);
        }

        private Material GetTerrainPlacementMaterial()
        {
            if (terrainPlacementMaterial == null)
            {
                terrainPlacementMaterial = CreateMaterial("Runtime Terrain Placement", terrainPlacementColor, 0f, 0.12f);
                ConfigureOpaquePlacementMaterial(terrainPlacementMaterial, terrainPlacementColor);
            }
            return terrainPlacementMaterial;
        }

        private Material GetBuildingPlacementMaterial()
        {
            if (buildingPlacementMaterial == null)
            {
                buildingPlacementMaterial = CreateMaterial("Runtime Building Placement", buildingPlacementColor, 0f, 0.24f);
                ConfigureOpaquePlacementMaterial(buildingPlacementMaterial, buildingPlacementColor);
            }
            return buildingPlacementMaterial;
        }

        private static void ConfigureOpaquePlacementMaterial(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            color.a = 1f;
            material.color = color;
            material.SetColor("_Color", color);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        private static float CalculateAdjacentCellDepth(Vector3[] corners)
        {
            var total = 0f;
            var count = 0;
            for (var index = 0; index < corners.Length; index++)
            {
                var length = Vector3.Distance(corners[index], corners[(index + 1) % corners.Length]);
                if (length > 0.0001f)
                {
                    total += length;
                    count++;
                }
            }
            return count > 0 ? Mathf.Max(0.05f, total / count) : 0.5f;
        }

        private Mesh BuildPlacedCellMesh(Vector3[] corners, Vector3 normal, float surfaceOffset, float depth, string meshName)
        {
            var mesh = new Mesh { name = meshName };
            var backOffset = normal * Mathf.Max(0.001f, surfaceOffset);
            var frontOffset = normal * (Mathf.Max(0.001f, surfaceOffset) + Mathf.Max(0.01f, depth));
            var back = new Vector3[4];
            var front = new Vector3[4];
            for (var index = 0; index < 4; index++)
            {
                back[index] = corners[index] + backOffset;
                front[index] = corners[index] + frontOffset;
            }

            var vertices = new Vector3[24];
            var triangles = new int[36];
            WritePlacedFace(vertices, triangles, 0, back[0], back[1], back[2], back[3]);
            WritePlacedFace(vertices, triangles, 1, front[0], front[3], front[2], front[1]);
            WritePlacedFace(vertices, triangles, 2, back[0], front[0], front[1], back[1]);
            WritePlacedFace(vertices, triangles, 3, back[1], front[1], front[2], back[2]);
            WritePlacedFace(vertices, triangles, 4, back[2], front[2], front[3], back[3]);
            WritePlacedFace(vertices, triangles, 5, back[3], front[3], front[0], back[0]);
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildPlacedCellMesh(Vector3[] cellCorners, string meshName)
        {
            var mesh = new Mesh { name = meshName };
            var vertices = new Vector3[24];
            var triangles = new int[36];
            WritePlacedFace(vertices, triangles, 0, cellCorners[0], cellCorners[1], cellCorners[2], cellCorners[3]);
            WritePlacedFace(vertices, triangles, 1, cellCorners[4], cellCorners[7], cellCorners[6], cellCorners[5]);
            WritePlacedFace(vertices, triangles, 2, cellCorners[0], cellCorners[4], cellCorners[5], cellCorners[1]);
            WritePlacedFace(vertices, triangles, 3, cellCorners[1], cellCorners[5], cellCorners[6], cellCorners[2]);
            WritePlacedFace(vertices, triangles, 4, cellCorners[2], cellCorners[6], cellCorners[7], cellCorners[3]);
            WritePlacedFace(vertices, triangles, 5, cellCorners[3], cellCorners[7], cellCorners[4], cellCorners[0]);
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void WritePlacedFace(Vector3[] vertices, int[] triangles, int faceIndex, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var vertexBase = faceIndex * 4;
            vertices[vertexBase] = generatedRoot.InverseTransformPoint(a);
            vertices[vertexBase + 1] = generatedRoot.InverseTransformPoint(b);
            vertices[vertexBase + 2] = generatedRoot.InverseTransformPoint(c);
            vertices[vertexBase + 3] = generatedRoot.InverseTransformPoint(d);
            var triangleBase = faceIndex * 6;
            triangles[triangleBase] = vertexBase;
            triangles[triangleBase + 1] = vertexBase + 1;
            triangles[triangleBase + 2] = vertexBase + 2;
            triangles[triangleBase + 3] = vertexBase;
            triangles[triangleBase + 4] = vertexBase + 2;
            triangles[triangleBase + 5] = vertexBase + 3;
        }

        private struct GridSurfaceHit
        {
            public Collider Collider;
            public int MeshQuadIndex;
            public bool IsPillar;
            public GeneratedPlacedCellSurface PlacedCell;
            public Vector3[] Corners;
            public Vector3 Normal;
            public string Key;
            public bool HasPlacementCell;
            public string PlacementCellKey;
            public Vector3[] PlacementCellCorners;
        }

        internal Mesh BuildWaterQuadMesh(List<Quad> quads, string meshName)
        {
            var repeat = Mathf.Max(1, waterTileRepeat);
            var startTile = -repeat / 2;
            var endTile = startTile + repeat - 1;
            var centralQuadCount = quads.Count;
            var outerTileCount = repeat * repeat - 1;
            var totalQuadCount = centralQuadCount + outerTileCount * Quads.Count;
            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var meshVertices = new Vector3[totalQuadCount * 4];
            var uvs = new Vector2[totalQuadCount * 4];
            var triangles = new int[totalQuadCount * 6];
            var outputQuad = 0;

            for (var tileZ = startTile; tileZ <= endTile; tileZ++)
            {
                for (var tileX = startTile; tileX <= endTile; tileX++)
                {
                    var source = tileX == 0 && tileZ == 0 ? quads : Quads;
                    for (var quadIndex = 0; quadIndex < source.Count; quadIndex++)
                    {
                        WriteWaterQuadVertices(meshVertices, triangles, outputQuad++, source[quadIndex], tileX, tileZ);
                    }
                }
            }

            var bounds = new Bounds(meshVertices.Length > 0 ? meshVertices[0] : Vector3.zero, Vector3.zero);
            for (var vertexIndex = 1; vertexIndex < meshVertices.Length; vertexIndex++)
            {
                bounds.Encapsulate(meshVertices[vertexIndex]);
            }
            for (var vertexIndex = 0; vertexIndex < meshVertices.Length; vertexIndex++)
            {
                var vertex = meshVertices[vertexIndex];
                uvs[vertexIndex] = new Vector2(
                    Mathf.InverseLerp(bounds.min.x, bounds.max.x, vertex.x),
                    Mathf.InverseLerp(bounds.min.z, bounds.max.z, vertex.z));
            }
            mesh.vertices = meshVertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void WriteWaterQuadVertices(
            Vector3[] meshVertices,
            int[] triangles,
            int outputQuad,
            Quad quad,
            int tileX,
            int tileZ)
        {
            var vertexBase = outputQuad * 4;
            for (var corner = 0; corner < 4; corner++)
            {
                meshVertices[vertexBase + corner] = generatedRoot.InverseTransformPoint(
                    MapWaterTileVertex(quad.GetVertex(corner), quad.Centroid.x, tileX, tileZ));
            }

            var triangleBase = outputQuad * 6;
            triangles[triangleBase] = vertexBase;
            triangles[triangleBase + 1] = vertexBase + 2;
            triangles[triangleBase + 2] = vertexBase + 1;
            triangles[triangleBase + 3] = vertexBase;
            triangles[triangleBase + 4] = vertexBase + 3;
            triangles[triangleBase + 5] = vertexBase + 2;
        }

        internal Mesh BuildWaterGridLineMesh(List<Quad> quads, float lineWidth, float surfaceOffset, string meshName)
        {
            var repeat = Mathf.Max(1, waterTileRepeat);
            var startTile = -repeat / 2;
            var endTile = startTile + repeat - 1;
            var tileEdgePairs = new List<Vector2Int>();
            var tileEdgeAnchors = new List<float>();
            CollectWaterEdges(Quads, tileEdgePairs, tileEdgeAnchors);
            var centralEdgePairs = new List<Vector2Int>();
            var centralEdgeAnchors = new List<float>();
            CollectWaterEdges(quads, centralEdgePairs, centralEdgeAnchors);
            var wrapEdgePairs = new List<Vector2Int>();
            CollectPeriodicWrapEdges(Quads, wrapEdgePairs);
            var zStitchPairs = new List<Vector2Int>();
            CollectVerticalTileStitchPairs(zStitchPairs);

            var totalEdgeCount = centralEdgePairs.Count +
                                 (repeat * repeat - 1) * tileEdgePairs.Count +
                                 wrapEdgePairs.Count * repeat * Mathf.Max(0, repeat - 1) +
                                 zStitchPairs.Count * repeat * Mathf.Max(0, repeat - 1);
            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var maximumJointCount = totalEdgeCount * 2;
            var maximumQuadCount = totalEdgeCount + maximumJointCount;
            var lineVertices = new Vector3[maximumQuadCount * 4];
            var waterSampleCoordinates = new Vector2[maximumQuadCount * 4];
            var triangles = new int[maximumQuadCount * 12];
            var halfWidth = Mathf.Max(0.0005f, lineWidth * 0.5f);
            var outputEdge = 0;
            var waterJointCenters = new List<Vector3>();
            var waterJointKeys = new HashSet<Vector2Int>();

            for (var tileZ = startTile; tileZ <= endTile; tileZ++)
            {
                for (var tileX = startTile; tileX <= endTile; tileX++)
                {
                    var edgePairs = tileX == 0 && tileZ == 0 ? centralEdgePairs : tileEdgePairs;
                    var edgeAnchors = tileX == 0 && tileZ == 0 ? centralEdgeAnchors : tileEdgeAnchors;
                    for (var edgeIndex = 0; edgeIndex < edgePairs.Count; edgeIndex++)
                    {
                        WriteWaterLineVertices(
                            lineVertices,
                            waterSampleCoordinates,
                            triangles,
                            outputEdge++,
                            edgePairs[edgeIndex],
                            edgeAnchors[edgeIndex],
                            tileX,
                            tileZ,
                            halfWidth,
                            surfaceOffset,
                            waterJointCenters,
                            waterJointKeys);
                    }
                }
            }
            for (var tileZ = startTile; tileZ < endTile; tileZ++)
            {
                for (var tileX = startTile; tileX <= endTile; tileX++)
                {
                    for (var edgeIndex = 0; edgeIndex < zStitchPairs.Count; edgeIndex++)
                    {
                        var pair = zStitchPairs[edgeIndex];
                        WriteWaterLineVertices(
                            lineVertices,
                            waterSampleCoordinates,
                            triangles,
                            outputEdge++,
                            pair.x,
                            Vertices[pair.x].x,
                            tileX,
                            tileZ,
                            pair.y,
                            Vertices[pair.y].x,
                            tileX,
                            tileZ + 1,
                            halfWidth,
                            surfaceOffset,
                            waterJointCenters,
                            waterJointKeys);
                    }
                }
            }
            for (var tileZ = startTile; tileZ <= endTile; tileZ++)
            {
                for (var tileX = startTile; tileX < endTile; tileX++)
                {
                    for (var edgeIndex = 0; edgeIndex < wrapEdgePairs.Count; edgeIndex++)
                    {
                        var pair = wrapEdgePairs[edgeIndex];
                        WriteWaterLineVertices(
                            lineVertices,
                            waterSampleCoordinates,
                            triangles,
                            outputEdge++,
                            pair.x,
                            Vertices[pair.x].x,
                            tileX,
                            tileZ,
                            pair.y,
                            Vertices[pair.y].x,
                            tileX + 1,
                            tileZ,
                            halfWidth,
                            surfaceOffset,
                            waterJointCenters,
                            waterJointKeys);
                    }
                }
            }

            for (var jointIndex = 0; jointIndex < waterJointCenters.Count; jointIndex++)
            {
                WriteWaterJointVertices(
                    lineVertices,
                    waterSampleCoordinates,
                    triangles,
                    outputEdge++,
                    waterJointCenters[jointIndex],
                    halfWidth,
                    surfaceOffset);
            }

            Array.Resize(ref lineVertices, outputEdge * 4);
            Array.Resize(ref waterSampleCoordinates, outputEdge * 4);
            Array.Resize(ref triangles, outputEdge * 12);

            var bounds = new Bounds(lineVertices.Length > 0 ? lineVertices[0] : Vector3.zero, Vector3.zero);
            for (var vertexIndex = 1; vertexIndex < lineVertices.Length; vertexIndex++)
            {
                bounds.Encapsulate(lineVertices[vertexIndex]);
            }
            var uvs = new Vector2[lineVertices.Length];
            for (var vertexIndex = 0; vertexIndex < lineVertices.Length; vertexIndex++)
            {
                var vertex = lineVertices[vertexIndex];
                uvs[vertexIndex] = new Vector2(
                    Mathf.InverseLerp(bounds.min.x, bounds.max.x, vertex.x),
                    Mathf.InverseLerp(bounds.min.z, bounds.max.z, vertex.z));
            }
            mesh.vertices = lineVertices;
            mesh.uv = uvs;
            mesh.uv2 = waterSampleCoordinates;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ValidateLineMeshWaterSamples(mesh);
            return mesh;
        }

        private static void ValidateLineMeshWaterSamples(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            var samples = mesh.uv2;
            if (samples == null || samples.Length != mesh.vertexCount || mesh.vertexCount % 4 != 0)
            {
                Debug.LogWarning("Grid line mesh is missing per-endpoint water sample coordinates: " + mesh.name);
                return;
            }

            for (var vertexBase = 0; vertexBase < samples.Length; vertexBase += 4)
            {
                var sampleA = samples[vertexBase];
                var sampleB = samples[vertexBase + 1];
                var sampleC = samples[vertexBase + 2];
                var sampleD = samples[vertexBase + 3];
                if (!IsFinite(sampleA) || !IsFinite(sampleB) || !IsFinite(sampleC) || !IsFinite(sampleD))
                {
                    Debug.LogWarning("Grid line mesh has invalid water sample coordinates: " + mesh.name);
                    return;
                }

                if ((sampleA - sampleB).sqrMagnitude > 0.000001f ||
                    (sampleC - sampleD).sqrMagnitude > 0.000001f)
                {
                    Debug.LogWarning("Grid line mesh has split water samples on a widened endpoint: " + mesh.name);
                    return;
                }

                var looksLikeJointPatch = (sampleA - sampleC).sqrMagnitude <= 0.000001f;
                if (looksLikeJointPatch && (sampleA - sampleD).sqrMagnitude > 0.000001f)
                {
                    Debug.LogWarning("Grid line joint patch has split water samples: " + mesh.name);
                    return;
                }
            }
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
        }

        internal void ValidateWaterGridLineCoverage(Mesh waterLineMesh)
        {
            if (waterLineMesh == null)
            {
                throw new InvalidOperationException("Water grid line mesh is missing; seam coverage cannot be verified.");
            }

            var samples = waterLineMesh.uv2;
            if (samples == null || samples.Length != waterLineMesh.vertexCount || waterLineMesh.vertexCount % 4 != 0)
            {
                throw new InvalidOperationException("Water grid line mesh is missing sample coordinates; seam coverage cannot be verified.");
            }

            var renderedWaterEdges = new HashSet<string>();
            for (var vertexBase = 0; vertexBase < samples.Length; vertexBase += 4)
            {
                var start = samples[vertexBase];
                var end = samples[vertexBase + 2];
                if ((start - end).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }
                renderedWaterEdges.Add(WaterSampleEdgeKey(start, end));
            }

            if (renderedWaterEdges.Count == 0)
            {
                throw new InvalidOperationException("Water grid line mesh did not render any valid grid edge.");
            }
        }

        private static string WaterSampleEdgeKey(Vector2 a, Vector2 b)
        {
            var qa = QuantizeWaterSample(a);
            var qb = QuantizeWaterSample(b);
            if (qa.x > qb.x || qa.x == qb.x && qa.y > qb.y)
            {
                var swap = qa;
                qa = qb;
                qb = swap;
            }
            return qa.x + "," + qa.y + "|" + qb.x + "," + qb.y;
        }

        private static Vector2Int QuantizeWaterSample(Vector2 sample)
        {
            return new Vector2Int(
                Mathf.RoundToInt(sample.x * 1000f),
                Mathf.RoundToInt(sample.y * 1000f));
        }

        private void CollectWaterEdges(List<Quad> quads, List<Vector2Int> edgePairs, List<float> edgeAnchors)
        {
            var uniqueEdges = new HashSet<ulong>();
            for (var quadIndex = 0; quadIndex < quads.Count; quadIndex++)
            {
                var quad = quads[quadIndex];
                for (var edge = 0; edge < 4; edge++)
                {
                    var a = quad.GetVertex(edge);
                    var b = quad.GetVertex((edge + 1) % 4);
                    var key = EdgeKey(a, b);
                    if (!uniqueEdges.Add(key)) continue;
                    edgePairs.Add(new Vector2Int(a, b));
                    edgeAnchors.Add(quad.Centroid.x);
                }
            }
        }

        private void CollectVerticalTileStitchPairs(List<Vector2Int> stitchPairs)
        {
            var bottom = new List<int>();
            var top = new List<int>();
            var tolerance = Mathf.Max(0.0001f, (yMax - yMin) * 0.0005f);
            for (var vertex = 0; vertex < Vertices.Count; vertex++)
            {
                var point = Vertices[vertex];
                if (Mathf.Abs(point.y - yMin) <= tolerance)
                {
                    bottom.Add(vertex);
                }
                else if (Mathf.Abs(point.y - yMax) <= tolerance)
                {
                    top.Add(vertex);
                }
            }

            bottom.Sort((left, right) => Vertices[left].x.CompareTo(Vertices[right].x));
            top.Sort((left, right) => Vertices[left].x.CompareTo(Vertices[right].x));
            if (bottom.Count == 0 || top.Count == 0)
            {
                return;
            }

            var usedTop = new HashSet<int>();
            for (var bottomIndex = 0; bottomIndex < bottom.Count; bottomIndex++)
            {
                var bottomVertex = bottom[bottomIndex];
                var topVertex = FindNearestUnusedThetaVertex(Vertices[bottomVertex].x, top, usedTop);
                if (topVertex < 0)
                {
                    continue;
                }
                stitchPairs.Add(new Vector2Int(topVertex, bottomVertex));
                usedTop.Add(topVertex);
            }
        }

        private int FindNearestUnusedThetaVertex(float theta, List<int> candidates, HashSet<int> used)
        {
            var bestVertex = -1;
            var bestDistance = float.MaxValue;
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (used.Contains(candidate))
                {
                    continue;
                }
                var distance = Mathf.Abs(TownscaperGridTopology.ShortestThetaDelta(theta, Vertices[candidate].x));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestVertex = candidate;
                }
            }
            return bestVertex;
        }

        private void CollectPeriodicWrapEdges(List<Quad> quads, List<Vector2Int> wrapEdgePairs)
        {
            var uniqueEdges = new HashSet<ulong>();
            for (var quadIndex = 0; quadIndex < quads.Count; quadIndex++)
            {
                var quad = quads[quadIndex];
                for (var edge = 0; edge < 4; edge++)
                {
                    var a = quad.GetVertex(edge);
                    var b = quad.GetVertex((edge + 1) % 4);
                    var key = EdgeKey(a, b);
                    if (!uniqueEdges.Add(key)) continue;

                    var rawThetaDelta = Vertices[b].x - Vertices[a].x;
                    if (rawThetaDelta > Mathf.PI)
                    {
                        wrapEdgePairs.Add(new Vector2Int(b, a));
                    }
                    else if (rawThetaDelta < -Mathf.PI)
                    {
                        wrapEdgePairs.Add(new Vector2Int(a, b));
                    }
                }
            }
        }

        private static ulong EdgeKey(int a, int b)
        {
            var min = Mathf.Min(a, b);
            var max = Mathf.Max(a, b);
            return ((ulong)(uint)min << 32) | (uint)max;
        }

        private void WriteWaterLineVertices(
            Vector3[] lineVertices,
            Vector2[] waterSampleCoordinates,
            int[] triangles,
            int outputEdge,
            Vector2Int pair,
            float anchor,
            int tileX,
            int tileZ,
            float halfWidth,
            float surfaceOffset,
            List<Vector3> waterJointCenters,
            HashSet<Vector2Int> waterJointKeys)
        {
            var start = MapWaterTileVertex(pair.x, anchor, tileX, tileZ);
            var end = MapWaterTileVertex(pair.y, anchor, tileX, tileZ);
            WriteWaterLineVertices(lineVertices, waterSampleCoordinates, triangles, outputEdge, start, end, halfWidth, surfaceOffset);
            RegisterWaterJoint(waterJointCenters, waterJointKeys, start);
            RegisterWaterJoint(waterJointCenters, waterJointKeys, end);
        }

        private void WriteWaterLineVertices(
            Vector3[] lineVertices,
            Vector2[] waterSampleCoordinates,
            int[] triangles,
            int outputEdge,
            int startVertex,
            float startAnchor,
            int startTileX,
            int startTileZ,
            int endVertex,
            float endAnchor,
            int endTileX,
            int endTileZ,
            float halfWidth,
            float surfaceOffset,
            List<Vector3> waterJointCenters,
            HashSet<Vector2Int> waterJointKeys)
        {
            var start = MapWaterTileVertex(startVertex, startAnchor, startTileX, startTileZ);
            var end = MapWaterTileVertex(endVertex, endAnchor, endTileX, endTileZ);
            WriteWaterLineVertices(lineVertices, waterSampleCoordinates, triangles, outputEdge, start, end, halfWidth, surfaceOffset);
            RegisterWaterJoint(waterJointCenters, waterJointKeys, start);
            RegisterWaterJoint(waterJointCenters, waterJointKeys, end);
        }

        private void WriteWaterLineVertices(
            Vector3[] lineVertices,
            Vector2[] waterSampleCoordinates,
            int[] triangles,
            int outputEdge,
            Vector3 start,
            Vector3 end,
            float halfWidth,
            float surfaceOffset)
        {
            var normal = transform.up;
            var segment = end - start;
            var direction = segment.sqrMagnitude > 0.000001f ? segment.normalized : transform.right;
            var side = Vector3.Cross(normal, direction).normalized * halfWidth;
            var sampleStart = start;
            var sampleEnd = end;
            start += normal * surfaceOffset;
            end += normal * surfaceOffset;
            var vertexBase = outputEdge * 4;
            lineVertices[vertexBase] = generatedRoot.InverseTransformPoint(start - side);
            lineVertices[vertexBase + 1] = generatedRoot.InverseTransformPoint(start + side);
            lineVertices[vertexBase + 2] = generatedRoot.InverseTransformPoint(end + side);
            lineVertices[vertexBase + 3] = generatedRoot.InverseTransformPoint(end - side);
            waterSampleCoordinates[vertexBase] = new Vector2(sampleStart.x, sampleStart.z);
            waterSampleCoordinates[vertexBase + 1] = new Vector2(sampleStart.x, sampleStart.z);
            waterSampleCoordinates[vertexBase + 2] = new Vector2(sampleEnd.x, sampleEnd.z);
            waterSampleCoordinates[vertexBase + 3] = new Vector2(sampleEnd.x, sampleEnd.z);
            var triangleBase = outputEdge * 12;
            triangles[triangleBase] = vertexBase;
            triangles[triangleBase + 1] = vertexBase + 1;
            triangles[triangleBase + 2] = vertexBase + 2;
            triangles[triangleBase + 3] = vertexBase;
            triangles[triangleBase + 4] = vertexBase + 2;
            triangles[triangleBase + 5] = vertexBase + 3;
            triangles[triangleBase + 6] = vertexBase;
            triangles[triangleBase + 7] = vertexBase + 2;
            triangles[triangleBase + 8] = vertexBase + 1;
            triangles[triangleBase + 9] = vertexBase;
            triangles[triangleBase + 10] = vertexBase + 3;
            triangles[triangleBase + 11] = vertexBase + 2;
        }

        private void WriteWaterJointVertices(
            Vector3[] lineVertices,
            Vector2[] waterSampleCoordinates,
            int[] triangles,
            int outputQuad,
            Vector3 center,
            float halfWidth,
            float surfaceOffset)
        {
            var normal = transform.up;
            var right = transform.right.normalized * halfWidth;
            var forward = transform.forward.normalized * halfWidth;
            var sampleCenter = center;
            center += normal * surfaceOffset;
            var vertexBase = outputQuad * 4;
            lineVertices[vertexBase] = generatedRoot.InverseTransformPoint(center - right - forward);
            lineVertices[vertexBase + 1] = generatedRoot.InverseTransformPoint(center + right - forward);
            lineVertices[vertexBase + 2] = generatedRoot.InverseTransformPoint(center + right + forward);
            lineVertices[vertexBase + 3] = generatedRoot.InverseTransformPoint(center - right + forward);
            waterSampleCoordinates[vertexBase] = new Vector2(sampleCenter.x, sampleCenter.z);
            waterSampleCoordinates[vertexBase + 1] = new Vector2(sampleCenter.x, sampleCenter.z);
            waterSampleCoordinates[vertexBase + 2] = new Vector2(sampleCenter.x, sampleCenter.z);
            waterSampleCoordinates[vertexBase + 3] = new Vector2(sampleCenter.x, sampleCenter.z);
            var triangleBase = outputQuad * 12;
            triangles[triangleBase] = vertexBase;
            triangles[triangleBase + 1] = vertexBase + 1;
            triangles[triangleBase + 2] = vertexBase + 2;
            triangles[triangleBase + 3] = vertexBase;
            triangles[triangleBase + 4] = vertexBase + 2;
            triangles[triangleBase + 5] = vertexBase + 3;
            triangles[triangleBase + 6] = vertexBase;
            triangles[triangleBase + 7] = vertexBase + 2;
            triangles[triangleBase + 8] = vertexBase + 1;
            triangles[triangleBase + 9] = vertexBase;
            triangles[triangleBase + 10] = vertexBase + 3;
            triangles[triangleBase + 11] = vertexBase + 2;
        }

        private static void RegisterWaterJoint(List<Vector3> centers, HashSet<Vector2Int> keys, Vector3 center)
        {
            var key = new Vector2Int(
                Mathf.RoundToInt(center.x * 1000f),
                Mathf.RoundToInt(center.z * 1000f));
            if (keys.Add(key))
            {
                centers.Add(center);
            }
        }

        private Vector3 GetWaterTileOffset(int tileX, int tileZ)
        {
            var scale = pillarWorldRadius / Mathf.Max(0.0001f, pillarRadius);
            var tileWidth = Mathf.PI * 2f * scale;
            var tileDepth = (yMax - yMin) * scale;
            return transform.right * (tileWidth * tileX) + transform.forward * (tileDepth * tileZ);
        }

        private Vector3 ProjectPointToWaterHeight(Vector3 worldPoint)
        {
            var local = transform.InverseTransformPoint(worldPoint);
            local.y = waterHeight;
            return transform.TransformPoint(local);
        }

        private Bounds GetBaseWaterTileBounds()
        {
            if (hasCachedBaseWaterTileBounds)
            {
                return cachedBaseWaterTileBounds;
            }

            var bounds = new Bounds(MapPlanarVertex(Vertices[Quads[0].A], Quads[0].Centroid.x, waterHeight), Vector3.zero);
            for (var quadIndex = 0; quadIndex < Quads.Count; quadIndex++)
            {
                var quad = Quads[quadIndex];
                for (var corner = 0; corner < 4; corner++)
                {
                    bounds.Encapsulate(MapPlanarVertex(Vertices[quad.GetVertex(corner)], quad.Centroid.x, waterHeight));
                }
            }
            cachedBaseWaterTileBounds = bounds;
            hasCachedBaseWaterTileBounds = true;
            return bounds;
        }

        private void ValidateTopologyAndSeam()
        {
            if (Quads.Count == 0 || PillarQuads.Count == 0 || waterQuads.Count == 0)
            {
                throw new InvalidOperationException("Boolean Townscaper partition must produce pillar and water quads.");
            }
            if (seamVertexIds.Count == 0 || seamEdges.Count == 0)
            {
                throw new InvalidOperationException("Boolean partition did not create a shared pillar/water seam.");
            }
            if (gridData.DissolvedRhombusCount == 0 || gridData.RemainingTriangleCount == 0 ||
                Quads.Count != gridData.DissolvedRhombusCount * 4 + gridData.RemainingTriangleCount * 3)
            {
                throw new InvalidOperationException("Townscaper dissolution/subdivision did not produce the expected triangle and rhombus quads.");
            }

            for (var quadIndex = 0; quadIndex < PillarQuads.Count; quadIndex++)
            {
                if (!IsQuadCoveredByPillar(PillarQuads[quadIndex]))
                {
                    throw new InvalidOperationException("Pillar partition contains a tile not covered by the selection circle.");
                }
            }
            for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
            {
                if (IsQuadCoveredByPillar(waterQuads[quadIndex]))
                {
                    throw new InvalidOperationException("Water partition retained a tile covered by the selection circle.");
                }
            }

            var waterEdgeKeys = BuildEdgeKeySet(waterQuads);
            for (var edgeIndex = 0; edgeIndex < seamEdges.Count; edgeIndex++)
            {
                if (!waterEdgeKeys.Contains(EdgeKey(seamEdges[edgeIndex].x, seamEdges[edgeIndex].y)))
                {
                    throw new InvalidOperationException("Pillar/water seam edge is missing from the water grid edge set.");
                }
            }

            var minimumSeamSpacing = float.MaxValue;
            var maximumSeamSpacing = 0f;
            for (var edgeIndex = 0; edgeIndex < seamEdges.Count; edgeIndex++)
            {
                var edge = seamEdges[edgeIndex];
                var spacing = Vector3.Distance(MapWaterVertex(Vertices[edge.x]), MapWaterVertex(Vertices[edge.y]));
                minimumSeamSpacing = Mathf.Min(minimumSeamSpacing, spacing);
                maximumSeamSpacing = Mathf.Max(maximumSeamSpacing, spacing);
            }
            var verticalTileBoundaryError = CalculateVerticalTileBoundaryError();
            float minimumWaterAngle;
            float maximumWaterAngle;
            CalculateWaterAngleRange(out minimumWaterAngle, out maximumWaterAngle);
            float minimumPillarAngle;
            float maximumPillarAngle;
            CalculateAngleRange(PillarQuads, out minimumPillarAngle, out maximumPillarAngle);
            const float diagnosticAcuteAngle = 60f;
            var acuteCornerCount = CountWaterCornersBelow(diagnosticAcuteAngle);
            var acutePillarCornerCount = CountCornersBelow(PillarQuads, diagnosticAcuteAngle);
            float minimumPlacementAngle;
            float maximumPlacementAngle;
            int acutePlacementCornerCount;
            CalculatePlacementAngleRange(
                placementMinimumAngle,
                out minimumPlacementAngle,
                out maximumPlacementAngle,
                out acutePlacementCornerCount);
            var extremeCornerCount40 = CountWaterCornersBelow(40f);
            var extremeCornerCount45 = CountWaterCornersBelow(45f);
            var targetCornerCount = CountWaterCornersBelow(acuteCornerMinimumAngle);
            string extremeCornerDetails;
            DescribeWaterCornersBelow(40f, out extremeCornerDetails);
            var acuteSeamCornerCount = CountWaterCornersBelowAtSeam(diagnosticAcuteAngle);
            var acuteSeamCornerDetails = DescribeWaterCornersBelowAtSeam(diagnosticAcuteAngle);
            string acuteBoundaryDetails;
            var acuteBoundaryCount = DescribeSeamBoundaryAnglesBelow(diagnosticAcuteAngle, out acuteBoundaryDetails);
            float minimumWaterEdgeRatio;
            float maximumWaterEdgeRatio;
            CalculateWaterEdgeRatioRange(out minimumWaterEdgeRatio, out maximumWaterEdgeRatio);

            Debug.Log("Boolean Townscaper grid valid: quads=" + Quads.Count +
                      ", pillar=" + PillarQuads.Count +
                      ", water=" + waterQuads.Count +
                      ", seamVertices=" + seamVertexIds.Count +
                      ", seamEdges=" + seamEdges.Count +
                      ", seamSpacing=" + minimumSeamSpacing.ToString("F3") + ".." + maximumSeamSpacing.ToString("F3") +
                      ", tileZBoundaryError=" + verticalTileBoundaryError.ToString("F6") +
                      ", waterAngles=" + minimumWaterAngle.ToString("F1") + ".." + maximumWaterAngle.ToString("F1") +
                      ", cornersBelow60=" + acuteCornerCount +
                      ", pillarAngles=" + minimumPillarAngle.ToString("F1") + ".." + maximumPillarAngle.ToString("F1") +
                      ", pillarCornersBelow60=" + acutePillarCornerCount +
                      ", placementAngles=" + minimumPlacementAngle.ToString("F1") + ".." + maximumPlacementAngle.ToString("F1") +
                      ", placementTargetAngle=" + placementMinimumAngle.ToString("F1") +
                      ", placementCornersBelowTarget=" + acutePlacementCornerCount +
                      ", cornersBelow45=" + extremeCornerCount45 +
                      ", cornersBelow40=" + extremeCornerCount40 +
                      ", extremeCornerDetails=" + extremeCornerDetails +
                      ", cornersBelowTarget=" + targetCornerCount +
                      ", seamCornersBelow60=" + acuteSeamCornerCount +
                      ", acuteSeamDetails=" + acuteSeamCornerDetails +
                      ", boundaryCornersBelow60=" + acuteBoundaryCount +
                      ", acuteBoundaryDetails=" + acuteBoundaryDetails +
                      ", waterEdgeRatio=" + minimumWaterEdgeRatio.ToString("F3") + ".." + maximumWaterEdgeRatio.ToString("F3") +
                      ", dissolvedRhombi=" + gridData.DissolvedRhombusCount +
                      ", remainingTriangles=" + gridData.RemainingTriangleCount +
                      ", verticalBands=" + pillarVerticalBands +
                      ", pillarHeight=" + EffectivePillarHeight.ToString("F2"), this);
        }

        private void CalculateWaterEdgeRatioRange(out float minimumRatio, out float maximumRatio)
        {
            minimumRatio = 1f;
            maximumRatio = 0f;
            for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
            {
                Vector2[] points;
                if (!TryGetQuadParameterPoints(waterQuads[quadIndex], out points))
                {
                    minimumRatio = 0f;
                    continue;
                }

                var shortest = float.MaxValue;
                var longest = 0f;
                for (var edge = 0; edge < 4; edge++)
                {
                    var length = Vector2.Distance(points[edge], points[(edge + 1) % 4]);
                    shortest = Mathf.Min(shortest, length);
                    longest = Mathf.Max(longest, length);
                }

                if (longest <= 0.000001f)
                {
                    minimumRatio = 0f;
                    continue;
                }

                var ratio = shortest / longest;
                minimumRatio = Mathf.Min(minimumRatio, ratio);
                maximumRatio = Mathf.Max(maximumRatio, ratio);
            }
        }

        private void CalculateWaterAngleRange(out float minimumAngle, out float maximumAngle)
        {
            CalculateAngleRange(waterQuads, out minimumAngle, out maximumAngle);
        }

        private void CalculatePlacementAngleRange(
            float angleThreshold,
            out float minimumAngle,
            out float maximumAngle,
            out int cornersBelowThreshold)
        {
            minimumAngle = 180f;
            maximumAngle = 0f;
            cornersBelowThreshold = 0;
            var band = GetWaterAdjacentBandIndex();
            for (var quadIndex = 0; quadIndex < Quads.Count; quadIndex++)
            {
                Vector3[] corners;
                if (!TryBuildUnifiedCellCorners(Quads[quadIndex], 0, 0, band, true, out corners))
                {
                    continue;
                }
                var points = new Vector2[4];
                for (var corner = 0; corner < 4; corner++)
                {
                    var local = transform.InverseTransformPoint(corners[corner]);
                    points[corner] = new Vector2(local.x, local.z);
                }
                for (var corner = 0; corner < 4; corner++)
                {
                    var angle = CalculateCornerAngle(points, corner);
                    minimumAngle = Mathf.Min(minimumAngle, angle);
                    maximumAngle = Mathf.Max(maximumAngle, angle);
                    if (angle < angleThreshold - 0.01f) cornersBelowThreshold++;
                }
            }
            if (minimumAngle == 180f) minimumAngle = 0f;
        }

        private void CalculateAngleRange(List<Quad> source, out float minimumAngle, out float maximumAngle)
        {
            minimumAngle = 180f;
            maximumAngle = 0f;
            for (var quadIndex = 0; quadIndex < source.Count; quadIndex++)
            {
                Vector2[] points;
                if (!TryGetQuadParameterPoints(source[quadIndex], out points))
                {
                    minimumAngle = 0f;
                    continue;
                }

                for (var corner = 0; corner < 4; corner++)
                {
                    var angle = CalculateCornerAngle(points, corner);
                    minimumAngle = Mathf.Min(minimumAngle, angle);
                    maximumAngle = Mathf.Max(maximumAngle, angle);
                }
            }

            if (minimumAngle == 180f && maximumAngle == 0f)
            {
                minimumAngle = 0f;
            }
        }

        private int CountWaterCornersBelow(float angleThreshold)
        {
            return CountCornersBelow(waterQuads, angleThreshold);
        }

        private int CountCornersBelow(List<Quad> source, float angleThreshold)
        {
            var count = 0;
            for (var quadIndex = 0; quadIndex < source.Count; quadIndex++)
            {
                Vector2[] points;
                if (!TryGetQuadParameterPoints(source[quadIndex], out points))
                {
                    continue;
                }
                for (var corner = 0; corner < 4; corner++)
                {
                    if (CalculateCornerAngle(points, corner) < angleThreshold)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private int DescribeWaterCornersBelow(float angleThreshold, out string description)
        {
            var details = new List<string>();
            for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
            {
                var quad = waterQuads[quadIndex];
                Vector2[] points;
                if (!TryGetQuadParameterPoints(quad, out points))
                {
                    continue;
                }
                for (var corner = 0; corner < 4; corner++)
                {
                    var angle = CalculateCornerAngle(points, corner);
                    if (angle < angleThreshold)
                    {
                        var vertex = quad.GetVertex(corner);
                        details.Add("q" + quad.Id + ":v" + vertex + ":" + angle.ToString("F1") + ":y" + Vertices[vertex].y.ToString("F2"));
                    }
                }
            }
            description = details.Count == 0 ? "none" : string.Join("|", details.ToArray());
            return details.Count;
        }

        private int CountWaterCornersBelowAtSeam(float angleThreshold)
        {
            var count = 0;
            for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
            {
                var quad = waterQuads[quadIndex];
                Vector2[] points;
                if (!TryGetQuadParameterPoints(quad, out points))
                {
                    continue;
                }
                for (var corner = 0; corner < 4; corner++)
                {
                    if (seamVertexIds.Contains(quad.GetVertex(corner)) &&
                        CalculateCornerAngle(points, corner) < angleThreshold)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private string DescribeWaterCornersBelowAtSeam(float angleThreshold)
        {
            var details = new List<string>();
            for (var quadIndex = 0; quadIndex < waterQuads.Count; quadIndex++)
            {
                var quad = waterQuads[quadIndex];
                Vector2[] points;
                if (!TryGetQuadParameterPoints(quad, out points))
                {
                    continue;
                }
                for (var corner = 0; corner < 4; corner++)
                {
                    var vertex = quad.GetVertex(corner);
                    var angle = CalculateCornerAngle(points, corner);
                    if (seamVertexIds.Contains(vertex) && angle < angleThreshold)
                    {
                        details.Add("q" + quad.Id + ":v" + vertex + ":" + angle.ToString("F1"));
                    }
                }
            }
            return details.Count == 0 ? "none" : string.Join("|", details.ToArray());
        }

        private int DescribeSeamBoundaryAnglesBelow(float angleThreshold, out string description)
        {
            var details = new List<string>();
            var seamNeighbors = BuildSeamNeighborMap();
            foreach (var pair in seamNeighbors)
            {
                float angle;
                if (TryCalculateSeamBoundaryAngle(pair.Key, pair.Value, out angle) && angle < angleThreshold)
                {
                    details.Add("v" + pair.Key + ":" + angle.ToString("F1"));
                }
            }
            description = details.Count == 0 ? "none" : string.Join("|", details.ToArray());
            return details.Count;
        }

        private float CalculateVerticalTileBoundaryError()
        {
            var bottom = new List<int>();
            var top = new List<int>();
            var tolerance = Mathf.Max(0.0001f, (yMax - yMin) * 0.0005f);
            for (var vertex = 0; vertex < Vertices.Count; vertex++)
            {
                var point = Vertices[vertex];
                if (Mathf.Abs(point.y - yMin) <= tolerance)
                {
                    bottom.Add(vertex);
                }
                else if (Mathf.Abs(point.y - yMax) <= tolerance)
                {
                    top.Add(vertex);
                }
            }

            bottom.Sort((left, right) => Vertices[left].x.CompareTo(Vertices[right].x));
            top.Sort((left, right) => Vertices[left].x.CompareTo(Vertices[right].x));
            var count = Math.Min(bottom.Count, top.Count);
            float maximumError = Mathf.Abs(bottom.Count - top.Count);
            for (var index = 0; index < count; index++)
            {
                float thetaError = Mathf.Abs(TownscaperGridTopology.ShortestThetaDelta(Vertices[bottom[index]].x, Vertices[top[index]].x));
                if (thetaError > maximumError)
                {
                    maximumError = thetaError;
                }
            }
            return maximumError;
        }

        private static HashSet<ulong> BuildEdgeKeySet(List<Quad> quads)
        {
            var keys = new HashSet<ulong>();
            for (var quadIndex = 0; quadIndex < quads.Count; quadIndex++)
            {
                var quad = quads[quadIndex];
                for (var edgeIndex = 0; edgeIndex < 4; edgeIndex++)
                {
                    keys.Add(EdgeKey(quad.GetVertex(edgeIndex), quad.GetVertex((edgeIndex + 1) % 4)));
                }
            }
            return keys;
        }

        private Vector2 ParameterDeltaFromPillarCenter(Vector2 point)
        {
            return new Vector2(
                TownscaperGridTopology.ShortestThetaDelta(pillarCenter.x, point.x),
                point.y - pillarCenter.y);
        }

        private void EnsureGeneratedRoot()
        {
            if (generatedRoot != null) return;
            generatedRoot = new GameObject("Generated Boolean Grid Surfaces").transform;
            generatedRoot.SetParent(transform, false);
        }

        internal static Material CreateMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = materialName, color = color };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private Material CreatePillarMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("PillarsAbove/StratifiedPillar") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = materialName, color = color };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetColor("_BottomColor", color * 0.72f);
            material.SetColor("_MiddleColor", color);
            material.SetColor("_TopColor", Color.Lerp(color, Color.white, 0.12f));
            material.SetFloat("_GradientBottom", PillarBottomHeight);
            material.SetFloat("_GradientMiddle", Mathf.Lerp(PillarBottomHeight, PillarTopHeight, 0.48f));
            material.SetFloat("_GradientTop", PillarTopHeight);
            material.SetFloat("_DrySmoothness", 0.11f);
            material.SetFloat("_WetSmoothness", 0.46f);
            material.SetFloat("_WetFadeDistance", 1.8f);
            material.SetFloat("_WaterEdgeSink", WaterGridGenerator.PillarWaterEdgeSink);
            return material;
        }

        internal static Material CreateGridMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = materialName, color = color };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            return material;
        }

        private static Material CreateOverlayLineMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("Hidden/Internal-Colored") ??
                         Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard");
            var material = new Material(shader) { name = materialName, color = color };
            material.SetColor("_Color", color);
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", color * 1.4f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            return material;
        }

        internal static Material CreateHighlightGridMaterial(string materialName, Color color, bool waterMode)
        {
            var shader = Shader.Find("PillarsAbove/GridHighlight") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = materialName, color = color };
            material.SetColor("_Color", color);
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", color * 1.15f);
            material.SetFloat("_WaterMode", waterMode ? 1f : 0f);
            material.SetFloat("_UseWorldDistance", waterMode ? 0f : 1f);
            material.SetFloat("_ClipBelowDynamicWater", waterMode ? 0f : 1f);
            material.SetFloat("_UseClipRipple", 0f);
            material.SetFloat("_AnchorBottomToDynamicWater", 0f);
            material.SetFloat("_DynamicWaterAnchorRange", 0.08f);
            material.SetFloat("_DynamicWaterAnchorOffset", 0.006f);
            material.SetFloat("_Alpha", color.a);
            material.SetFloat("_HighlightRadius", 0f);
            material.SetFloat("_HighlightFadeWidth", 0.01f);
            material.SetFloat("_WaterClipSoftness", 0.006f);
            return material;
        }

        internal static void SetHighlightMaterial(Material material, Vector3 center, float radius, float fadeWidth, float alpha)
        {
            if (material == null) return;
            material.SetVector("_HighlightCenter", new Vector4(center.x, center.y, center.z, 1f));
            material.SetFloat("_HighlightRadius", Mathf.Max(0f, radius));
            material.SetFloat("_HighlightFadeWidth", Mathf.Max(0.001f, fadeWidth));
            material.SetFloat("_Alpha", Mathf.Clamp01(alpha));
        }

        internal Transform GetGeneratedRoot()
        {
            EnsureGeneratedRoot();
            return generatedRoot;
        }

        internal static void DestroyRuntimeObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }

    public sealed class GeneratedGridClickSurface : MonoBehaviour
    {
        private PillarGridGenerator generator;
        private bool pillarSurface;

        public PillarGridGenerator Generator => generator;
        public bool IsPillarSurface => pillarSurface;

        public void Initialize(PillarGridGenerator owner, bool isPillar)
        {
            generator = owner;
            pillarSurface = isPillar;
        }

        private void OnMouseDown()
        {
        }
    }

    public sealed class GeneratedPlacedCellSurface : MonoBehaviour
    {
        public PillarGridGenerator Generator { get; private set; }
        public string PlacementKey { get; private set; }

        public void Initialize(PillarGridGenerator owner, string placementKey)
        {
            Generator = owner;
            PlacementKey = placementKey;
        }
    }
}
