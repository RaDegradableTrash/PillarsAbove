using System.Collections.Generic;
using UnityEngine;

namespace PillarsAbove.BuildingTiles
{
    [CreateAssetMenu(menuName = "Pillars Above/Building Tile Catalog")]
    public sealed class BuildingTileCatalog : ScriptableObject
    {
        [SerializeField] private List<GameObject> prefabs = new List<GameObject>();
        public IReadOnlyList<GameObject> Prefabs => prefabs;

        public IEnumerable<BuildingTileDefinition> Definitions(TileLayer layer)
        {
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                var definition = prefab.GetComponent<BuildingTileDefinition>();
                if (definition != null && definition.Layer == layer) yield return definition;
            }
        }

#if UNITY_EDITOR
        public void ReplaceContents(List<GameObject> values) => prefabs = values;
#endif
    }
}
