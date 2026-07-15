# BuildingTiles WFC 与支柱规则

## 资源

- `Assets/Prefabs/BuildingTiles`：从 `BuildingTiles.fbx` 拆出的 36 个独立 prefab（26 Cube、5 Top Seal、5 Bottom Seal）。
- `Assets/Resources/BuildingTileCatalog.asset`：生成器使用的完整目录。
- 每个 prefab 都有 `BuildingTileDefinition`，保留 FBX 中原有轴心、旋转和缩放，只把展览排布位置归零。
- `Seal_TopSingle.001` 在 prefab 层规范为 `Seal_TopSingle`；源 FBX 不修改。
- 场景里的 `BuildingTiles` 源对象只作为编辑器来源/检查用途，运行画面会隐藏；可见建筑统一使用 `Assets/Prefabs/BuildingTiles` 下的白色 prefab。

## Cube 接口

`Cube_` 后的轴名直接表示敞开面。单个面只写正方向；紧跟 `-` 表示同轴正负两面都敞开：

- `Cube_Z+`：仅 Z+ 敞开。
- `Cube_X+-Y+`：X+、X-、Y+ 敞开。
- `Cube_Individual`：六面全部闭合。

Cube 运行时使用 prefab 轴心的 24 个轴向 90° 旋转。命名只描述规范姿态下的正方向开口；实际放置时可旋转到 X-、Y-、Z- 等任意需要的方向。相邻 Cube 仅在下列情况兼容：

1. A 朝向 B 的面敞开，B 朝向 A 的面也敞开；或
2. 两个相对面都闭合。

任何“开口对闭墙”组合都从候选集中删除。建筑边界默认只允许闭合面；需要开放边界时可启用 `allowOpenBoundary`。

## Seal 接口与同格占用

Seal 用平面四角位掩码表示缝合点：X+Z+、X+Z-、X-Z+、X-Z-。

- Single：1 个角。
- Double：2 个相邻角。
- Diagonal：2 个斜对角。
- Triple：3 个角。
- Quadric：4 个角。

旋转时角位掩码随 Y 轴同步旋转。两个相邻 Seal 在共享边上的两个角必须逐点相等。Top 只与 Top 比较，Bottom 只与 Bottom 比较。

每个网格格子的占用槽固定为：

- 0 或 1 个 Cube；
- 0 或 1 个 Seal_Top；
- 0 或 1 个 Seal_Bottom。

Cube 不能与任何其他 Cube 共格；Top 与 Bottom 可以共格；两个 Top 或两个 Bottom 不能共格。运行时可调用 `BuildingWfcGenerator.TryPlaceSeal(cell, layer, requiredCorners)` 放置指定角组合，它会同时检查旋转候选、同格槽和相邻 Seal 边。

## 生成流程

每次放置建筑时，`PillarForgeEngine` 会以点击面外侧生成一个 3×3×3 房间体积。可见 tile 是 26 个外壳 Cube；几何中心那 1 格是内部空腔，不放 Cube，因为源资产没有“六面全开”的中心块。

1. 为每个外壳格计算精确的开口掩码：朝向房间体积内部的面必须敞开，朝向房间外部的面必须闭合。
2. 从“所有 Cube prefab × 不重复的 24 向旋转”候选集中只保留开口掩码完全一致的候选。
3. 若某个外壳位置没有候选，立即报告错误并停止放置。
4. 全部 26 个外壳格确定后，在对应世界格实例化白色 prefab。旧黄色运行时建筑网格不再渲染，只保留逻辑占用用于承重和规则判断。

`BuildingWfcGenerator.Generate()` 仍保留为独立调试工具；它使用同一套 Cube/Seal 规则，不作为玩家放置建筑时的主流程。

`BuildingTileCatalogWindow` 可通过 `Window > Pillars Above > Building Tile Catalog Inspector` 打开，用于逐个查看 prefab 和预设。

## 支柱算法

每间房占 3×3×3；支柱位置固定在 3×3 地面的中央格。斜撑类型和生成逻辑已移除。

放置新房时：

1. 检查房间下方 3×3 地面。最多 4 个格子悬空视为安全局部悬挑，不加柱。
2. 5 个或更多地面格悬空时进入承重判断。
3. 一根柱最多承担 2 间同层附近房屋，并覆盖一个房间宽度（3 格）；因此完全悬空的连续房屋会形成“隔一间一根柱”的节奏。
4. 覆盖范围内没有柱，或最近柱已超过 2 间房承载量时，在新房中央生成柱。
5. 柱从中央格无限向下（受世界网格底部限制），直到碰到山体，或碰到其他非柱结构的顶面；已有柱可直接合并。
6. 柱子的可见部分也使用白色 `Cube_Individual` prefab 堆叠，旧棕黄柱体网格不再渲染。

编辑器校验会检查 26/5/5 的 prefab 数量、预设组件，并执行四方向重复放置验证：X+、X-、Z+、Z- 每个方向连续 4 间房都必须生成 104 个外壳 Cube、4 个内部空腔、2 根承重柱，并覆盖全部 26 种外壳开口掩码。
