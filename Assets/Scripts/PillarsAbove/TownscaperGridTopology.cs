using System;
using System.Collections.Generic;
using UnityEngine;

namespace PillarsAbove
{
    [Serializable]
    public sealed class Quad
    {
        public int Id;
        public int A;
        public int B;
        public int C;
        public int D;
        public Vector2 Centroid;

        public int GetVertex(int index)
        {
            switch (index)
            {
                case 0: return A;
                case 1: return B;
                case 2: return C;
                default: return D;
            }
        }
    }

    [Serializable]
    public sealed class TownscaperGridData
    {
        public List<Vector2> Vertices = new List<Vector2>();
        public List<Quad> Quads = new List<Quad>();
        public int InitialTriangleCount;
        public int DissolvedRhombusCount;
        public int RemainingTriangleCount;
    }

    /// <summary>
    /// Triangular lattice -> random shared-edge dissolution -> triangle/rhombus
    /// subdivision -> periodic Lloyd/Laplacian relaxation.
    /// </summary>
    public static class TownscaperGridTopology
    {
        private const float MinimumDissolvedQuadAngleDegrees = 42f;
        private const float MaximumDissolvedQuadDiagonalRatio = 2.15f;

        private sealed class Triangle
        {
            public readonly int[] Vertices = new int[3];
            public bool Dissolved;
        }

        private sealed class Polygon
        {
            public readonly List<int> Vertices = new List<int>(4);
        }

        private struct EdgeKey : IEquatable<EdgeKey>
        {
            public EdgeKey(int first, int second)
            {
                A = Mathf.Min(first, second);
                B = Mathf.Max(first, second);
            }

            public readonly int A;
            public readonly int B;

            public bool Equals(EdgeKey other)
            {
                return A == other.A && B == other.B;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey && Equals((EdgeKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (A * 397) ^ B;
                }
            }
        }

        public static TownscaperGridData Generate(
            int nodesPerRing,
            int levelCount,
            float yMin,
            float yMax,
            float dissolutionProbability,
            int randomSeed,
            int relaxationIterations,
            float relaxationStrength)
        {
            nodesPerRing = Mathf.Max(4, nodesPerRing);
            levelCount = Mathf.Max(2, levelCount);
            var data = new TownscaperGridData();
            var triangles = BuildTriangularLattice(data.Vertices, nodesPerRing, levelCount, yMin, yMax);
            data.InitialTriangleCount = triangles.Count;
            var polygons = DissolveEdges(triangles, data.Vertices, dissolutionProbability, randomSeed);
            for (var polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
            {
                if (polygons[polygonIndex].Vertices.Count == 4) data.DissolvedRhombusCount++;
                else if (polygons[polygonIndex].Vertices.Count == 3) data.RemainingTriangleCount++;
            }
            SubdivideToQuads(data, polygons);
            Relax(data, yMin, yMax, relaxationIterations, relaxationStrength);
            RecalculateCentroids(data);
            return data;
        }

        private static List<Triangle> BuildTriangularLattice(
            List<Vector2> vertices,
            int nodesPerRing,
            int levelCount,
            float yMin,
            float yMax)
        {
            var triangles = new List<Triangle>(nodesPerRing * (levelCount - 1) * 2);
            var thetaStep = Mathf.PI * 2f / nodesPerRing;
            for (var level = 0; level < levelCount; level++)
            {
                var y = Mathf.Lerp(yMin, yMax, (float)level / (levelCount - 1));
                var stagger = (level & 1) == 0 ? 0f : thetaStep * 0.5f;
                for (var column = 0; column < nodesPerRing; column++)
                {
                    vertices.Add(new Vector2(WrapTheta(column * thetaStep + stagger), y));
                }
            }

            for (var level = 0; level < levelCount - 1; level++)
            {
                for (var column = 0; column < nodesPerRing; column++)
                {
                    var next = WrapColumn(column + 1, nodesPerRing);
                    var lowerA = level * nodesPerRing + column;
                    var lowerB = level * nodesPerRing + next;
                    var upperA = (level + 1) * nodesPerRing + column;
                    var upperB = (level + 1) * nodesPerRing + next;

                    if ((level & 1) == 0)
                    {
                        triangles.Add(CreateTriangle(lowerA, lowerB, upperA));
                        triangles.Add(CreateTriangle(lowerB, upperB, upperA));
                    }
                    else
                    {
                        triangles.Add(CreateTriangle(lowerA, lowerB, upperB));
                        triangles.Add(CreateTriangle(lowerA, upperB, upperA));
                    }
                }
            }

            return triangles;
        }

        private static List<Polygon> DissolveEdges(
            List<Triangle> triangles,
            List<Vector2> vertices,
            float probability,
            int randomSeed)
        {
            var edgeTriangles = new Dictionary<EdgeKey, List<int>>();
            for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                var triangle = triangles[triangleIndex];
                for (var edge = 0; edge < 3; edge++)
                {
                    var key = new EdgeKey(triangle.Vertices[edge], triangle.Vertices[(edge + 1) % 3]);
                    List<int> owners;
                    if (!edgeTriangles.TryGetValue(key, out owners))
                    {
                        owners = new List<int>(2);
                        edgeTriangles.Add(key, owners);
                    }
                    owners.Add(triangleIndex);
                }
            }

            var candidates = new List<EdgeKey>();
            foreach (var pair in edgeTriangles)
            {
                if (pair.Value.Count == 2)
                {
                    candidates.Add(pair.Key);
                }
            }

            var random = new System.Random(randomSeed);
            for (var i = candidates.Count - 1; i > 0; i--)
            {
                var swap = random.Next(i + 1);
                var temporary = candidates[i];
                candidates[i] = candidates[swap];
                candidates[swap] = temporary;
            }

            var polygons = new List<Polygon>(triangles.Count);
            probability = Mathf.Clamp01(probability);
            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                var key = candidates[candidateIndex];
                var owners = edgeTriangles[key];
                var first = triangles[owners[0]];
                var second = triangles[owners[1]];
                if (first.Dissolved || second.Dissolved || random.NextDouble() > probability)
                {
                    continue;
                }

                var unique = new List<int>(4);
                AddUnique(unique, first.Vertices[0]);
                AddUnique(unique, first.Vertices[1]);
                AddUnique(unique, first.Vertices[2]);
                AddUnique(unique, second.Vertices[0]);
                AddUnique(unique, second.Vertices[1]);
                AddUnique(unique, second.Vertices[2]);
                SortAroundPeriodicCentroid(unique, vertices);
                if (!IsAcceptableDissolvedQuad(unique, vertices))
                {
                    continue;
                }

                first.Dissolved = true;
                second.Dissolved = true;
                var rhombus = new Polygon();
                rhombus.Vertices.AddRange(unique);
                polygons.Add(rhombus);
            }

            for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                var triangle = triangles[triangleIndex];
                if (triangle.Dissolved)
                {
                    continue;
                }

                var polygon = new Polygon();
                polygon.Vertices.AddRange(triangle.Vertices);
                SortAroundPeriodicCentroid(polygon.Vertices, vertices);
                polygons.Add(polygon);
            }

            return polygons;
        }

        private static void SubdivideToQuads(TownscaperGridData data, List<Polygon> polygons)
        {
            var midpointByEdge = new Dictionary<EdgeKey, int>();
            for (var polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
            {
                var polygon = polygons[polygonIndex];
                var center = PeriodicAverage(polygon.Vertices, data.Vertices);
                var centerId = data.Vertices.Count;
                data.Vertices.Add(center);

                var midpointIds = new int[polygon.Vertices.Count];
                for (var edge = 0; edge < polygon.Vertices.Count; edge++)
                {
                    var a = polygon.Vertices[edge];
                    var b = polygon.Vertices[(edge + 1) % polygon.Vertices.Count];
                    var key = new EdgeKey(a, b);
                    int midpointId;
                    if (!midpointByEdge.TryGetValue(key, out midpointId))
                    {
                        midpointId = data.Vertices.Count;
                        midpointByEdge.Add(key, midpointId);
                        data.Vertices.Add(PeriodicMidpoint(data.Vertices[a], data.Vertices[b]));
                    }
                    midpointIds[edge] = midpointId;
                }

                for (var corner = 0; corner < polygon.Vertices.Count; corner++)
                {
                    var previous = (corner + polygon.Vertices.Count - 1) % polygon.Vertices.Count;
                    data.Quads.Add(new Quad
                    {
                        Id = data.Quads.Count,
                        A = polygon.Vertices[corner],
                        B = midpointIds[corner],
                        C = centerId,
                        D = midpointIds[previous]
                    });
                }
            }
        }

        private static void Relax(
            TownscaperGridData data,
            float yMin,
            float yMax,
            int iterations,
            float strength)
        {
            iterations = Mathf.Max(0, iterations);
            strength = Mathf.Clamp01(strength);
            var boundaryTolerance = Mathf.Max(0.0001f, (yMax - yMin) * 0.0001f);
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                RecalculateCentroids(data);
                var adjacentQuads = new List<int>[data.Vertices.Count];
                var neighbors = new HashSet<int>[data.Vertices.Count];
                for (var vertex = 0; vertex < data.Vertices.Count; vertex++)
                {
                    adjacentQuads[vertex] = new List<int>(6);
                    neighbors[vertex] = new HashSet<int>();
                }

                for (var quadIndex = 0; quadIndex < data.Quads.Count; quadIndex++)
                {
                    var quad = data.Quads[quadIndex];
                    for (var corner = 0; corner < 4; corner++)
                    {
                        var current = quad.GetVertex(corner);
                        adjacentQuads[current].Add(quadIndex);
                        neighbors[current].Add(quad.GetVertex((corner + 1) % 4));
                        neighbors[current].Add(quad.GetVertex((corner + 3) % 4));
                    }
                }

                var relaxed = new Vector2[data.Vertices.Count];
                for (var vertex = 0; vertex < data.Vertices.Count; vertex++)
                {
                    var original = data.Vertices[vertex];
                    if (adjacentQuads[vertex].Count == 0)
                    {
                        relaxed[vertex] = original;
                        continue;
                    }

                    var thetaSum = 0f;
                    var ySum = 0f;
                    var sampleCount = 0;
                    foreach (var neighbor in neighbors[vertex])
                    {
                        var point = data.Vertices[neighbor];
                        thetaSum += original.x + ShortestThetaDelta(original.x, point.x);
                        ySum += point.y;
                        sampleCount++;
                    }
                    for (var adjacent = 0; adjacent < adjacentQuads[vertex].Count; adjacent++)
                    {
                        var centroid = data.Quads[adjacentQuads[vertex][adjacent]].Centroid;
                        thetaSum += original.x + ShortestThetaDelta(original.x, centroid.x);
                        ySum += centroid.y;
                        sampleCount++;
                    }

                    var target = new Vector2(WrapTheta(thetaSum / sampleCount), ySum / sampleCount);
                    var deltaTheta = ShortestThetaDelta(original.x, target.x);
                    var next = new Vector2(WrapTheta(original.x + deltaTheta * strength), Mathf.Lerp(original.y, target.y, strength));
                    if (Mathf.Abs(original.y - yMin) <= boundaryTolerance)
                    {
                        next.y = yMin;
                    }
                    else if (Mathf.Abs(original.y - yMax) <= boundaryTolerance)
                    {
                        next.y = yMax;
                    }
                    relaxed[vertex] = next;
                }

                data.Vertices.Clear();
                data.Vertices.AddRange(relaxed);
                StitchVerticalTileBoundary(data, yMin, yMax, boundaryTolerance);
            }
        }

        private static void StitchVerticalTileBoundary(
            TownscaperGridData data,
            float yMin,
            float yMax,
            float boundaryTolerance)
        {
            var bottom = new List<int>();
            var top = new List<int>();
            for (var vertex = 0; vertex < data.Vertices.Count; vertex++)
            {
                var point = data.Vertices[vertex];
                if (Mathf.Abs(point.y - yMin) <= boundaryTolerance)
                {
                    bottom.Add(vertex);
                }
                else if (Mathf.Abs(point.y - yMax) <= boundaryTolerance)
                {
                    top.Add(vertex);
                }
            }

            if (bottom.Count == 0 || top.Count == 0)
            {
                return;
            }

            bottom.Sort((left, right) => data.Vertices[left].x.CompareTo(data.Vertices[right].x));
            top.Sort((left, right) => data.Vertices[left].x.CompareTo(data.Vertices[right].x));
            var matched = Mathf.Min(bottom.Count, top.Count);
            for (var index = 0; index < matched; index++)
            {
                var bottomVertex = bottom[index];
                var topVertex = top[index];
                var bottomPoint = data.Vertices[bottomVertex];
                var topPoint = data.Vertices[topVertex];
                var averageTheta = WrapTheta(bottomPoint.x + ShortestThetaDelta(bottomPoint.x, topPoint.x) * 0.5f);
                data.Vertices[bottomVertex] = new Vector2(averageTheta, yMin);
                data.Vertices[topVertex] = new Vector2(averageTheta, yMax);
            }
        }

        public static void RecalculateCentroids(TownscaperGridData data)
        {
            for (var quadIndex = 0; quadIndex < data.Quads.Count; quadIndex++)
            {
                var quad = data.Quads[quadIndex];
                var ids = new List<int>(4) { quad.A, quad.B, quad.C, quad.D };
                quad.Centroid = PeriodicAverage(ids, data.Vertices);
            }
        }

        public static float ShortestThetaDelta(float from, float to)
        {
            var delta = WrapTheta(to) - WrapTheta(from);
            if (delta > Mathf.PI) delta -= Mathf.PI * 2f;
            if (delta < -Mathf.PI) delta += Mathf.PI * 2f;
            return delta;
        }

        public static float WrapTheta(float theta)
        {
            var period = Mathf.PI * 2f;
            theta %= period;
            return theta < 0f ? theta + period : theta;
        }

        private static Triangle CreateTriangle(int a, int b, int c)
        {
            var triangle = new Triangle();
            triangle.Vertices[0] = a;
            triangle.Vertices[1] = b;
            triangle.Vertices[2] = c;
            return triangle;
        }

        private static int WrapColumn(int column, int count)
        {
            return ((column % count) + count) % count;
        }

        private static void AddUnique(List<int> values, int value)
        {
            if (!values.Contains(value)) values.Add(value);
        }

        private static Vector2 PeriodicMidpoint(Vector2 a, Vector2 b)
        {
            return new Vector2(WrapTheta(a.x + ShortestThetaDelta(a.x, b.x) * 0.5f), (a.y + b.y) * 0.5f);
        }

        private static Vector2 PeriodicAverage(List<int> vertexIds, List<Vector2> vertices)
        {
            var anchor = vertices[vertexIds[0]].x;
            var theta = 0f;
            var y = 0f;
            for (var i = 0; i < vertexIds.Count; i++)
            {
                var point = vertices[vertexIds[i]];
                theta += anchor + ShortestThetaDelta(anchor, point.x);
                y += point.y;
            }
            return new Vector2(WrapTheta(theta / vertexIds.Count), y / vertexIds.Count);
        }

        private static bool IsAcceptableDissolvedQuad(List<int> ids, List<Vector2> vertices)
        {
            if (ids.Count != 4)
            {
                return false;
            }

            var points = UnwrapAroundPeriodicCenter(ids, vertices);
            var minAngle = 180f;
            for (var index = 0; index < points.Length; index++)
            {
                var previous = points[(index + points.Length - 1) % points.Length] - points[index];
                var next = points[(index + 1) % points.Length] - points[index];
                if (previous.sqrMagnitude < 0.000001f || next.sqrMagnitude < 0.000001f)
                {
                    return false;
                }

                minAngle = Mathf.Min(minAngle, Vector2.Angle(previous, next));
            }

            var diagonalA = Vector2.Distance(points[0], points[2]);
            var diagonalB = Vector2.Distance(points[1], points[3]);
            var minDiagonal = Mathf.Min(diagonalA, diagonalB);
            var maxDiagonal = Mathf.Max(diagonalA, diagonalB);
            if (minDiagonal < 0.000001f)
            {
                return false;
            }

            return minAngle >= MinimumDissolvedQuadAngleDegrees &&
                   maxDiagonal / minDiagonal <= MaximumDissolvedQuadDiagonalRatio;
        }

        private static Vector2[] UnwrapAroundPeriodicCenter(List<int> ids, List<Vector2> vertices)
        {
            var center = PeriodicAverage(ids, vertices);
            var points = new Vector2[ids.Count];
            for (var index = 0; index < ids.Count; index++)
            {
                var point = vertices[ids[index]];
                points[index] = new Vector2(
                    center.x + ShortestThetaDelta(center.x, point.x),
                    point.y);
            }
            return points;
        }

        private static void SortAroundPeriodicCentroid(List<int> ids, List<Vector2> vertices)
        {
            var center = PeriodicAverage(ids, vertices);
            ids.Sort((left, right) =>
            {
                var a = vertices[left];
                var b = vertices[right];
                var angleA = Mathf.Atan2(a.y - center.y, ShortestThetaDelta(center.x, a.x));
                var angleB = Mathf.Atan2(b.y - center.y, ShortestThetaDelta(center.x, b.x));
                return angleA.CompareTo(angleB);
            });
        }
    }
}
