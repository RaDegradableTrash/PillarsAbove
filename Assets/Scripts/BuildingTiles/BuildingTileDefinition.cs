using System;
using UnityEngine;

namespace PillarsAbove.BuildingTiles
{
    [Flags]
    public enum TileFace
    {
        None = 0,
        PositiveX = 1 << 0,
        NegativeX = 1 << 1,
        PositiveY = 1 << 2,
        NegativeY = 1 << 3,
        PositiveZ = 1 << 4,
        NegativeZ = 1 << 5
    }

    [Flags]
    public enum SealCorner
    {
        None = 0,
        PositiveXPositiveZ = 1 << 0,
        PositiveXNegativeZ = 1 << 1,
        NegativeXPositiveZ = 1 << 2,
        NegativeXNegativeZ = 1 << 3,
        All = PositiveXPositiveZ | PositiveXNegativeZ | NegativeXPositiveZ | NegativeXNegativeZ
    }

    public enum TileLayer
    {
        Cube,
        SealTop,
        SealBottom
    }

    [DisallowMultipleComponent]
    public sealed class BuildingTileDefinition : MonoBehaviour
    {
        [SerializeField] private TileLayer layer;
        [SerializeField] private TileFace canonicalOpenFaces;
        [SerializeField] private TileFace rootIdentityOpenFacesOverride;
        [SerializeField] private SealCorner canonicalSealCorners;
        [SerializeField, Min(1)] private int weight = 10;
        [SerializeField] private bool allowQuarterTurns = true;

        public TileLayer Layer => layer;
        public TileFace CanonicalOpenFaces => canonicalOpenFaces;
        public TileFace RootIdentityOpenFacesOverride => rootIdentityOpenFacesOverride;
        public SealCorner CanonicalSealCorners => canonicalSealCorners;
        public int Weight => Mathf.Max(1, weight);
        public int RotationCount => !allowQuarterTurns ? 1 : layer == TileLayer.Cube ? BuildingWfcRules.CubeRotationCount : BuildingWfcRules.SealRotationCount;

        public Quaternion VisualRotationOffset
        {
            get
            {
                var visual = transform.Find("Visual");
                return visual == null ? Quaternion.identity : visual.localRotation;
            }
        }

        public TileFace GetOpenFaces(int rotation)
        {
            var result = TileFace.None;
            var faces = new[]
            {
                TileFace.PositiveX, TileFace.NegativeX, TileFace.PositiveY,
                TileFace.NegativeY, TileFace.PositiveZ, TileFace.NegativeZ
            };
            foreach (var face in faces)
            {
                if ((canonicalOpenFaces & face) == 0) continue;
                if (layer == TileLayer.Cube)
                {
                    // Names describe the imported model's canonical axes. The FBX importer
                    // keeps its axis conversion on the Visual child, so the real rendered
                    // direction is RootRotation * VisualLocalRotation * canonicalDirection.
                    if (rootIdentityOpenFacesOverride == TileFace.None)
                    {
                        var renderedRotation = GetRotation(rotation) * VisualRotationOffset;
                        result |= BuildingWfcRules.RotateFace(face, renderedRotation);
                    }
                }
                else if (face == TileFace.PositiveY || face == TileFace.NegativeY)
                {
                    result |= face;
                }
                else
                {
                    result |= BuildingWfcRules.RotateHorizontalFace(face, rotation);
                }
            }

            if (layer == TileLayer.Cube && rootIdentityOpenFacesOverride != TileFace.None)
            {
                result = TileFace.None;
                foreach (var face in faces)
                {
                    if ((rootIdentityOpenFacesOverride & face) != 0)
                    {
                        result |= BuildingWfcRules.RotateCubeFace(face, rotation);
                    }
                }
            }
            return result;
        }

        public SealCorner GetSealCorners(int rotation)
        {
            return BuildingWfcRules.RotateSealCorners(canonicalSealCorners, rotation);
        }

        public Quaternion GetRotation(int rotation)
        {
            return layer == TileLayer.Cube
                ? BuildingWfcRules.CubeRotation(rotation)
                : Quaternion.Euler(0f, rotation * 90f, 0f);
        }

#if UNITY_EDITOR
        public void Configure(TileLayer newLayer, TileFace openFaces, SealCorner sealCorners, int newWeight = 10, TileFace newRootIdentityOpenFacesOverride = TileFace.None)
        {
            layer = newLayer;
            canonicalOpenFaces = openFaces;
            rootIdentityOpenFacesOverride = newRootIdentityOpenFacesOverride;
            canonicalSealCorners = sealCorners;
            weight = Mathf.Max(1, newWeight);
            allowQuarterTurns = true;
        }
#endif
    }
}
