# Pillars Above

Prototype foundation for a vertical stone-pillar building game inspired by Townscaper-style one-click creation, but built around a lower-weight 3D grid carved into a sea pillar.

## Current Engine Slice

- Runtime-generated stylized stone pillar with a tall cylindrical silhouette.
- Three-axis grid state for stone, carved voids, and attached building cells.
- Combined visible-face mesh generation for the pillar, sunlit relief, and building layer.
- Mesh collider based picking that maps mouse hits back to grid cells.
- One-click carving that removes a small cluster of stone cells.
- One-click wall building that places a small multi-cell module outward from the rock face.
- Restore tool that fills a small cluster back into stone.
- Orbit camera, zoom, vertical camera target adjustment, warm stylized lighting, fog, and a large ocean plane.
- Double-sided surface geometry to avoid accidental see-through faces in the current prototype.
- Relief panels and ledges layered on top of the grid so cells read more like stylized terrain units than individual Minecraft blocks.
- Multi-cell build preview that shows the full module footprint before placement.
- Rule-driven structure cells using the A0100/A1001/A1002/A2X00/A3100 naming scheme.
- Local placement propagation that marks nearby cells dirty after a build, then auto-completes door landings, platforms, and columns.
- Connector-style placement rules for ground support, wall continuity, vertical support, and door-facing platform requirements.
- A 71-cell cross-section radius (5x the original) with a wider overview camera, expanded ocean horizon, and distance-scaled atmospheric fog.
- Runtime hierarchy is organized into camera, lighting, pillar, ocean, and interaction preview groups.
- Ocean impact foam is generated from one procedural shore-collision mesh driven by wave phase, incoming flow direction, shoreline radius, and turbulence noise instead of pre-placed foam strips.

## Try It

Open `Assets/Scenes/SampleScene.unity` and press Play. The engine bootstraps itself at runtime, so no scene setup is required.

Controls:

- Left click: use the selected tool on the highlighted cell.
- Hold right mouse or Shift: temporarily hide and lock the cursor, then restore it to the original position when released; move the mouse to drive the camera direction. Up/down moves the camera target vertically, and left/right moves quickly around the pillar while staying faced toward it.
- Mouse wheel: zoom.
- Q / E: move the camera target down / up the pillar.
- 1 / 2 / 3: carve / build / restore.
- Toolbar buttons: `-`, `+`, `o` choose carve, build, restore.

## Next Engine Work

- Replace the first-pass procedural panels with authored isolated/connected meshes for each structure type.
- Add slope/roof/window rules so building clusters merge more like a city toy.
- Add mesh-morphing or SDF-style blending for wider bases when adjacent supports touch.
- Save and load grid state.
- Add stronger visual distinction between carved interiors, terraces, and attached buildings.
- Add stair paths and bridge constraints on top of the current supports and entrances.
