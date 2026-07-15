using UnityEngine;

namespace PillarsAbove.BuildingTiles
{
    public static class BuildingWfcRules
    {
        public const int SealRotationCount = 4;

        public struct CubeOrientation
        {
            public CubeOrientation(Quaternion rotation)
            {
                Rotation = rotation;
            }

            public readonly Quaternion Rotation;
        }

        public static readonly TileFace[] HorizontalFaces =
        {
            TileFace.PositiveX, TileFace.NegativeX, TileFace.PositiveZ, TileFace.NegativeZ
        };

        private static readonly Vector3Int[] AxisDirections =
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.up,
            Vector3Int.down,
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        private static readonly CubeOrientation[] CubeOrientations = BuildCubeOrientations();

        public static int CubeRotationCount => CubeOrientations.Length;

        public static Quaternion CubeRotation(int orientation)
        {
            return CubeOrientations[NormalizeOrientation(orientation)].Rotation;
        }

        public static TileFace RotateCubeFace(TileFace face, int orientation)
        {
            if (face == TileFace.None) return TileFace.None;
            var world = CubeRotation(orientation) * FaceToVector(face);
            return VectorToFace(world);
        }

        public static TileFace RotateFace(TileFace face, Quaternion rotation)
        {
            if (face == TileFace.None) return TileFace.None;
            return VectorToFace(rotation * FaceToVector(face));
        }

        public static TileFace RotateHorizontalFace(TileFace face, int quarterTurns)
        {
            var turns = ((quarterTurns % 4) + 4) % 4;
            for (var i = 0; i < turns; i++)
            {
                switch (face)
                {
                    case TileFace.PositiveX: face = TileFace.NegativeZ; break;
                    case TileFace.NegativeZ: face = TileFace.NegativeX; break;
                    case TileFace.NegativeX: face = TileFace.PositiveZ; break;
                    case TileFace.PositiveZ: face = TileFace.PositiveX; break;
                }
            }
            return face;
        }

        public static SealCorner RotateSealCorners(SealCorner corners, int quarterTurns)
        {
            var result = SealCorner.None;
            var values = new[]
            {
                SealCorner.PositiveXPositiveZ, SealCorner.PositiveXNegativeZ,
                SealCorner.NegativeXPositiveZ, SealCorner.NegativeXNegativeZ
            };
            foreach (var corner in values)
            {
                if ((corners & corner) == 0) continue;
                var x = corner == SealCorner.PositiveXPositiveZ || corner == SealCorner.PositiveXNegativeZ ? 1 : -1;
                var z = corner == SealCorner.PositiveXPositiveZ || corner == SealCorner.NegativeXPositiveZ ? 1 : -1;
                var turns = ((quarterTurns % 4) + 4) % 4;
                for (var i = 0; i < turns; i++)
                {
                    var oldX = x;
                    x = z;
                    z = -oldX;
                }
                result |= CornerFromSigns(x, z);
            }
            return result;
        }

        public static TileFace Opposite(TileFace face)
        {
            switch (face)
            {
                case TileFace.PositiveX: return TileFace.NegativeX;
                case TileFace.NegativeX: return TileFace.PositiveX;
                case TileFace.PositiveY: return TileFace.NegativeY;
                case TileFace.NegativeY: return TileFace.PositiveY;
                case TileFace.PositiveZ: return TileFace.NegativeZ;
                case TileFace.NegativeZ: return TileFace.PositiveZ;
                default: return TileFace.None;
            }
        }

        public static bool CubeFacesMatch(BuildingTileDefinition a, int aRotation, BuildingTileDefinition b, int bRotation, TileFace directionFromA)
        {
            if (a == null || b == null || a.Layer != TileLayer.Cube || b.Layer != TileLayer.Cube) return false;
            var aOpen = (a.GetOpenFaces(aRotation) & directionFromA) != 0;
            var bOpen = (b.GetOpenFaces(bRotation) & Opposite(directionFromA)) != 0;
            return aOpen == bOpen;
        }

        public static bool CanShareCell(BuildingTileDefinition a, BuildingTileDefinition b)
        {
            if (a == null || b == null || a.Layer == TileLayer.Cube || b.Layer == TileLayer.Cube) return false;
            return a.Layer != b.Layer;
        }

        public static bool SealEdgesMatch(BuildingTileDefinition a, int aRotation, BuildingTileDefinition b, int bRotation, TileFace directionFromA)
        {
            if (a == null || b == null || a.Layer != b.Layer || a.Layer == TileLayer.Cube) return false;
            var ac = a.GetSealCorners(aRotation);
            var bc = b.GetSealCorners(bRotation);
            switch (directionFromA)
            {
                case TileFace.PositiveX:
                    return Has(ac, SealCorner.PositiveXPositiveZ) == Has(bc, SealCorner.NegativeXPositiveZ) &&
                           Has(ac, SealCorner.PositiveXNegativeZ) == Has(bc, SealCorner.NegativeXNegativeZ);
                case TileFace.NegativeX:
                    return SealEdgesMatch(b, bRotation, a, aRotation, TileFace.PositiveX);
                case TileFace.PositiveZ:
                    return Has(ac, SealCorner.PositiveXPositiveZ) == Has(bc, SealCorner.PositiveXNegativeZ) &&
                           Has(ac, SealCorner.NegativeXPositiveZ) == Has(bc, SealCorner.NegativeXNegativeZ);
                case TileFace.NegativeZ:
                    return SealEdgesMatch(b, bRotation, a, aRotation, TileFace.PositiveZ);
                default:
                    return true;
            }
        }

        public static Vector3Int Direction(TileFace face)
        {
            switch (face)
            {
                case TileFace.PositiveX: return Vector3Int.right;
                case TileFace.NegativeX: return Vector3Int.left;
                case TileFace.PositiveY: return Vector3Int.up;
                case TileFace.NegativeY: return Vector3Int.down;
                case TileFace.PositiveZ: return new Vector3Int(0, 0, 1);
                case TileFace.NegativeZ: return new Vector3Int(0, 0, -1);
                default: return Vector3Int.zero;
            }
        }

        private static bool Has(SealCorner value, SealCorner flag) => (value & flag) != 0;

        private static SealCorner CornerFromSigns(int x, int z)
        {
            if (x > 0) return z > 0 ? SealCorner.PositiveXPositiveZ : SealCorner.PositiveXNegativeZ;
            return z > 0 ? SealCorner.NegativeXPositiveZ : SealCorner.NegativeXNegativeZ;
        }

        private static int NormalizeOrientation(int orientation)
        {
            return ((orientation % CubeOrientations.Length) + CubeOrientations.Length) % CubeOrientations.Length;
        }

        private static CubeOrientation[] BuildCubeOrientations()
        {
            var result = new CubeOrientation[24];
            var index = 0;
            foreach (var up in AxisDirections)
            {
                foreach (var forward in AxisDirections)
                {
                    if (Dot(up, forward) != 0) continue;
                    result[index++] = new CubeOrientation(Quaternion.LookRotation(forward, up));
                }
            }
            return result;
        }

        private static int Dot(Vector3Int a, Vector3Int b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }

        private static Vector3 FaceToVector(TileFace face)
        {
            switch (face)
            {
                case TileFace.PositiveX: return Vector3.right;
                case TileFace.NegativeX: return Vector3.left;
                case TileFace.PositiveY: return Vector3.up;
                case TileFace.NegativeY: return Vector3.down;
                case TileFace.PositiveZ: return Vector3.forward;
                case TileFace.NegativeZ: return Vector3.back;
                default: return Vector3.zero;
            }
        }

        private static TileFace VectorToFace(Vector3 value)
        {
            var x = Mathf.Abs(value.x);
            var y = Mathf.Abs(value.y);
            var z = Mathf.Abs(value.z);
            if (x >= y && x >= z) return value.x >= 0f ? TileFace.PositiveX : TileFace.NegativeX;
            if (y >= x && y >= z) return value.y >= 0f ? TileFace.PositiveY : TileFace.NegativeY;
            return value.z >= 0f ? TileFace.PositiveZ : TileFace.NegativeZ;
        }
    }
}
