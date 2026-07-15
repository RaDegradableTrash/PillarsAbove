using System;
using System.Collections.Generic;
using UnityEngine;

namespace PillarsAbove.BuildingTiles
{
    public sealed class BuildingWfcGenerator : MonoBehaviour
    {
        [Serializable]
        private struct Variant
        {
            public BuildingTileDefinition Definition;
            public int Rotation;
        }

        [SerializeField] private BuildingTileCatalog catalog;
        [SerializeField] private Vector3Int gridSize = new Vector3Int(6, 2, 6);
        [SerializeField, Min(0.1f)] private float cellSize = 3f;
        [SerializeField] private int seed = 1701;
        [SerializeField] private bool allowOpenBoundary;
        [SerializeField] private bool generateOnStart;

        private readonly Dictionary<Vector3Int, Variant> cubes = new Dictionary<Vector3Int, Variant>();
        private readonly Dictionary<Vector3Int, Variant> topSeals = new Dictionary<Vector3Int, Variant>();
        private readonly Dictionary<Vector3Int, Variant> bottomSeals = new Dictionary<Vector3Int, Variant>();
        private Transform generatedRoot;

        private static readonly TileFace[] Directions =
        {
            TileFace.PositiveX, TileFace.NegativeX, TileFace.PositiveY,
            TileFace.NegativeY, TileFace.PositiveZ, TileFace.NegativeZ
        };

        private void Start()
        {
            if (generateOnStart) Generate();
        }

        [ContextMenu("Generate WFC Building")]
        public void Generate()
        {
            ClearGenerated();
            if (catalog == null)
            {
                Debug.LogError("BuildingWfcGenerator needs a BuildingTileCatalog.", this);
                return;
            }

            var variants = BuildVariants(TileLayer.Cube);
            var wave = new Dictionary<Vector3Int, List<Variant>>();
            for (var x = 0; x < gridSize.x; x++)
            for (var y = 0; y < gridSize.y; y++)
            for (var z = 0; z < gridSize.z; z++)
            {
                var cell = new Vector3Int(x, y, z);
                wave[cell] = FilterBoundary(cell, variants);
                if (wave[cell].Count == 0)
                {
                    Debug.LogError("No WFC tile can satisfy the boundary at " + cell, this);
                    return;
                }
            }

            var random = new System.Random(seed);
            while (true)
            {
                var target = FindLowestEntropy(wave, random);
                if (target.x < 0) break;
                var candidates = wave[target];
                var chosen = WeightedPick(candidates, random);
                candidates.Clear();
                candidates.Add(chosen);
                if (!Propagate(target, wave))
                {
                    Debug.LogError("WFC contradiction at " + target + ". Change the seed, bounds, or boundary rule.", this);
                    ClearGenerated();
                    return;
                }
            }

            generatedRoot = new GameObject("Generated Building Tiles").transform;
            generatedRoot.SetParent(transform, false);
            foreach (var pair in wave)
            {
                var variant = pair.Value[0];
                cubes[pair.Key] = variant;
                Spawn(variant, pair.Key, generatedRoot);
            }
        }

        public bool TryPlaceSeal(Vector3Int cell, TileLayer layer, SealCorner requiredCorners)
        {
            if (layer == TileLayer.Cube || !IsInside(cell) || catalog == null) return false;
            var targetMap = layer == TileLayer.SealTop ? topSeals : bottomSeals;
            if (targetMap.ContainsKey(cell)) return false;

            foreach (var variant in BuildVariants(layer))
            {
                if (variant.Definition.GetSealCorners(variant.Rotation) != requiredCorners) continue;
                if (!SealFitsNeighbors(cell, variant, targetMap)) continue;
                if (generatedRoot == null)
                {
                    generatedRoot = new GameObject("Generated Building Tiles").transform;
                    generatedRoot.SetParent(transform, false);
                }
                targetMap[cell] = variant;
                Spawn(variant, cell, generatedRoot);
                return true;
            }
            return false;
        }

        [ContextMenu("Clear Generated Building")]
        public void ClearGenerated()
        {
            cubes.Clear();
            topSeals.Clear();
            bottomSeals.Clear();
            if (generatedRoot == null) return;
            if (Application.isPlaying) Destroy(generatedRoot.gameObject);
            else DestroyImmediate(generatedRoot.gameObject);
            generatedRoot = null;
        }

        private List<Variant> BuildVariants(TileLayer layer)
        {
            var result = new List<Variant>();
            foreach (var definition in catalog.Definitions(layer))
            {
                var seenFaces = new HashSet<int>();
                for (var rotation = 0; rotation < definition.RotationCount; rotation++)
                {
                    var signature = layer == TileLayer.Cube
                        ? (int)definition.GetOpenFaces(rotation)
                        : (int)definition.GetSealCorners(rotation);
                    if (!seenFaces.Add(signature)) continue;
                    result.Add(new Variant { Definition = definition, Rotation = rotation });
                }
            }
            return result;
        }

        private List<Variant> FilterBoundary(Vector3Int cell, List<Variant> source)
        {
            var result = new List<Variant>(source.Count);
            foreach (var variant in source)
            {
                var openings = variant.Definition.GetOpenFaces(variant.Rotation);
                var invalid = !allowOpenBoundary &&
                    (cell.x == 0 && (openings & TileFace.NegativeX) != 0 ||
                     cell.x == gridSize.x - 1 && (openings & TileFace.PositiveX) != 0 ||
                     cell.y == 0 && (openings & TileFace.NegativeY) != 0 ||
                     cell.y == gridSize.y - 1 && (openings & TileFace.PositiveY) != 0 ||
                     cell.z == 0 && (openings & TileFace.NegativeZ) != 0 ||
                     cell.z == gridSize.z - 1 && (openings & TileFace.PositiveZ) != 0);
                if (!invalid) result.Add(variant);
            }
            return result;
        }

        private bool Propagate(Vector3Int changed, Dictionary<Vector3Int, List<Variant>> wave)
        {
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(changed);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var direction in Directions)
                {
                    var neighbor = cell + BuildingWfcRules.Direction(direction);
                    if (!wave.TryGetValue(neighbor, out var neighborCandidates)) continue;
                    var before = neighborCandidates.Count;
                    neighborCandidates.RemoveAll(candidate => !HasCompatible(wave[cell], candidate, direction));
                    if (neighborCandidates.Count == 0) return false;
                    if (neighborCandidates.Count < before) queue.Enqueue(neighbor);
                }
            }
            return true;
        }

        private static bool HasCompatible(List<Variant> sources, Variant candidate, TileFace direction)
        {
            foreach (var source in sources)
            {
                if (BuildingWfcRules.CubeFacesMatch(source.Definition, source.Rotation, candidate.Definition, candidate.Rotation, direction)) return true;
            }
            return false;
        }

        private Vector3Int FindLowestEntropy(Dictionary<Vector3Int, List<Variant>> wave, System.Random random)
        {
            var best = int.MaxValue;
            var ties = new List<Vector3Int>();
            foreach (var pair in wave)
            {
                var count = pair.Value.Count;
                if (count <= 1) continue;
                if (count < best)
                {
                    best = count;
                    ties.Clear();
                }
                if (count == best) ties.Add(pair.Key);
            }
            return ties.Count == 0 ? new Vector3Int(-1, -1, -1) : ties[random.Next(ties.Count)];
        }

        private static Variant WeightedPick(List<Variant> candidates, System.Random random)
        {
            var total = 0;
            foreach (var candidate in candidates) total += candidate.Definition.Weight;
            var roll = random.Next(total);
            foreach (var candidate in candidates)
            {
                roll -= candidate.Definition.Weight;
                if (roll < 0) return candidate;
            }
            return candidates[candidates.Count - 1];
        }

        private bool SealFitsNeighbors(Vector3Int cell, Variant candidate, Dictionary<Vector3Int, Variant> map)
        {
            foreach (var direction in BuildingWfcRules.HorizontalFaces)
            {
                if (!map.TryGetValue(cell + BuildingWfcRules.Direction(direction), out var neighbor)) continue;
                if (!BuildingWfcRules.SealEdgesMatch(candidate.Definition, candidate.Rotation, neighbor.Definition, neighbor.Rotation, direction)) return false;
            }
            return true;
        }

        private void Spawn(Variant variant, Vector3Int cell, Transform parent)
        {
            var instance = Instantiate(variant.Definition.gameObject, parent);
            instance.name = variant.Definition.name + "_R" + variant.Rotation;
            instance.transform.localPosition = Vector3.Scale((Vector3)cell, new Vector3(cellSize, cellSize, cellSize));
            instance.transform.localRotation = variant.Definition.GetRotation(variant.Rotation) * variant.Definition.transform.localRotation;
        }

        private bool IsInside(Vector3Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.z >= 0 && cell.x < gridSize.x && cell.y < gridSize.y && cell.z < gridSize.z;
        }
    }
}
