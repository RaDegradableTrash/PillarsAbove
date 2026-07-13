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
- Taller 60-cell pillar and more vibrant water with animated gleam objects.
- Runtime hierarchy is organized into camera, lighting, pillar, ocean, and interaction preview groups.
- Ocean is made from separate scene objects: a low-poly swell mesh, hazy horizon water, soft oval wave patches, curved white shore foam, and subtle water highlights.

## Try It

Open `Assets/Scenes/SampleScene.unity` and press Play. The engine bootstraps itself at runtime, so no scene setup is required.

Controls:

- Left click: use the selected tool on the highlighted cell.
- Right drag: rotate around the pillar horizontally and move the perspective up/down vertically.
- Hold Shift: move the mouse to rotate or move up/down without holding right click.
- Mouse wheel: zoom.
- Q / E: move the camera target down / up the pillar.
- 1 / 2 / 3: carve / build / restore.
- Toolbar buttons: `-`, `+`, `o` choose carve, build, restore.

## Next Engine Work

- Replace cube modules with rule-driven architectural pieces.
- Add slope/roof/window rules so building clusters merge like a city toy.
- Save and load grid state.
- Add stronger visual distinction between carved interiors, terraces, and attached buildings.
- Add resource-free placement constraints: supports, entrances, bridges, and stair paths.
