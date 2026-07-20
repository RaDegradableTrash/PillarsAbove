# Building Tile Mesh Seam Tool

This tool makes every `BuildingTileDefinition` prefab expose an editable six-face seam cage. It keeps source FBX meshes untouched: every placed tile receives its own runtime mesh copy before deformation.

## Author a prefab

1. Open a Building Tile prefab in Prefab Mode and select its root `BuildingTileDefinition`.
2. In **Mesh Seam Cage**, click **Create 6 Face Cages**. The initial handles are snapped to the nearest readable mesh vertices.
3. Select `+X`, `-X`, `+Y`, `-Y`, `+Z`, or `-Z`. Drag the colored Scene-view handles onto the exact seam vertices, like collider edit handles.
4. Add handles when the seam has more than four important vertices, then use **Snap Handles To Mesh**.
5. Keep every connection profile at the same point count; spatially corresponding handles are paired by nearest world position. **Validate Pair Counts** checks opposite faces on the current prefab.
6. Save the prefab. Do not rebuild the FBX-derived prefab catalog after manual seam authoring unless those authored prefab changes are intentionally being replaced.

The batch entry is **Tools > Pillars Above > Mesh Seams > Create Cages On Selected Tiles**.

## Six-face detail editor

Open **Window > Pillars Above > Building Tile Six-Face Editor**, or press **Open Six-Face Detail Editor** on a tile definition.

- Use the clickable orientation thumbnail on the right, or keys `1`–`6`, to switch between `+X`, `-X`, `+Y`, `-Y`, `+Z`, and `-Z`.
- The detail canvas uses green/red background tint for the logical open/closed state.
- Cyan dots are currently unbound mesh vertices. Green dots and translucent green regions are vertices covered by existing seam markers.
- Yellow diamonds are mesh-edge midpoints. Magenta dots are authored seam markers.
- Left-click a vertex or edge midpoint to add it to the current face. Right-click a magenta marker to remove it.

## Runtime behavior

- `BuildingWfcGenerator` stitches the completed generated cube grid.
- `PillarForgeEngine` stitches after each multi-cell room placement pass.
- Facing profiles are resolved after prefab rotation, paired one-to-one by nearest world position, and moved to their shared midpoint.
- Vertices within **Vertex Bind Radius** of each handle are deformed. Increase the radius only enough to include duplicate vertices at the same modeled point.
- Normals, tangents, bounds, and `MeshCollider` references are refreshed after deformation.

If two facing profiles have different handle counts, that connection is left unchanged and Unity logs a warning instead of guessing a destructive correspondence.
