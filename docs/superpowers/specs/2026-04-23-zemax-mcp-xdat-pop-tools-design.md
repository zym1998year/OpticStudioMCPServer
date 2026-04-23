# Zemax MCP: XDAT / POP / Surface-Ops 工具补缺

**日期**：2026-04-23
**起因**：Mephisto WFS 前向模型项目在 MCP 链路上遇到 4 处能力缺口：
1. 无 Extra Data Editor（XDAT）读写工具 → 目前靠手工解析 .zmx UTF-16 文本修改 Zernike 系数
2. 无 `zemax_remove_surface` → 插错面后无法回退
3. `zemax_set_surface_type` 的 `listTypes=true` 在本机 ZOSAPI 版本下返回 "Unable to list surface types"
4. 无 POP / Defocused PSF 工具 → 无法让 Zemax 计算 WFS donut（phase + defocus）

本 spec 为以上 4 个缺口提供补缺方案，合入源码：`E:\ZemaxProject\ZemaxMCP_源码_本地开发版`。

---

## 范围与决策

| 条目 | 决策 |
|------|------|
| 实现范围 | 4 个功能一次性合入 |
| 代码组织 | 每个功能独立工具类，不抽共享 helper；保持现有"一工具一类"模式 |
| XDAT 工具设计 | 通用 `get_extra_data` / `set_extra_data`，支持 batch、`makeVariable`；不做 Zernike 语义层封装 |
| POP 输出 | 结构化 2D intensity grid + 物理坐标 + 元数据；大 grid 支持写磁盘 |
| 测试 | 借助 `TestCase/Double Gauss 28 degree field SYN3.zmx` 做手工 smoke test，不引入 xUnit test project |
| 分支 | `feat/xdat-pop-tools`；4 个工具 + 1 个 Program.cs/README 提交 |

---

## 工具 1：`zemax_remove_surface`

**文件**：`src/ZemaxMCP.Server/Tools/LensData/RemoveSurfaceTool.cs`
**Program.cs 注册段**：Lens Data Tools

### 接口

| 字段 | 类型 | 必填 | 默认 | 说明 |
|------|------|------|------|------|
| `surfaceNumber` | int | 是 | — | 要删除的面号 |

**输出**：`RemoveSurfaceResult { Success, Error?, RemovedSurfaceNumber, TotalSurfaces }`

### 实现要点

- 核心调用：`lde.RemoveSurfaceAt(surfaceNumber)`
- 校验：
  - `surfaceNumber == 0` → 拒绝（Object 面不可删）
  - `surfaceNumber == lde.NumberOfSurfaces - 1` → 拒绝（Image 面不可删）
  - `surfaceNumber < 0 || >= NumberOfSurfaces` → 拒绝（越界）
- 用 `_session.ExecuteAsync("RemoveSurface", parameters, system => { ... })` 模式，和现有 `AddSurfaceTool` 对称

---

## 工具 2：`zemax_list_surface_types`

**文件**：`src/ZemaxMCP.Server/Tools/LensData/ListSurfaceTypesTool.cs`
**Program.cs 注册段**：Lens Data Tools

### 接口

无入参。

**输出**：`ListSurfaceTypesResult { Success, Error?, AvailableTypes: string[] }`

### 实现要点

- 直接 `Enum.GetNames(typeof(ZOSAPI.Editors.LDE.SurfaceType))`，按字母序排序后返回。
- 同步修订 `SetSurfaceTypeTool.cs:55-73`（`listTypes=true` 分支）：先尝试 `dynSurface.GetAvailableSurfaceTypes()`，失败时 fallback 到同一枚举逻辑，而不是返回 "Unable to list surface types"。

### 为什么不用 dynamic
本机 ZOSAPI 版本的 `GetSurfaceTypeSettings(Type).GetAvailableSurfaceTypes()` 抛异常；直接枚举 `SurfaceType` enum 稳定、不依赖运行时版本。代价是可能列出几个在当前 license 下不可用的类型（如 `UserDefined` 需 DLL），但 MCP 调用方会在实际 `ChangeType` 时得到明确错误，可接受。

---

## 工具 3：`zemax_get_extra_data` / `zemax_set_extra_data`

**文件**：
- `src/ZemaxMCP.Server/Tools/LensData/GetExtraDataTool.cs`
- `src/ZemaxMCP.Server/Tools/LensData/SetExtraDataTool.cs`

**Program.cs 注册段**：Lens Data Tools

### 接口（Get）

| 字段 | 类型 | 必填 | 默认 | 说明 |
|------|------|------|------|------|
| `surfaceNumber` | int | 是 | — | 面号 |
| `startCell` | int | 否 | 1 | 起始 XDAT 单元（1-indexed） |
| `endCell` | int | 否 | 0 | 结束单元；0 表示自动探测（直到连续 3 格读失败或返回 0） |

**输出**：
```
GetExtraDataResult {
  Success, Error?,
  SurfaceNumber, SurfaceType,
  Entries: [ { Cell: int, Value: double, IsVariable: bool }, ... ]
}
```

### 接口（Set）

| 字段 | 类型 | 必填 | 默认 | 说明 |
|------|------|------|------|------|
| `surfaceNumber` | int | 是 | — | 面号 |
| `cell` | int | 否 | 0 | 单设模式：要写的单元号 |
| `value` | double | 否 | null | 单设模式：值 |
| `makeVariable` | bool | 否 | null | 单设模式：设为 Variable |
| `batchSet` | string | 否 | null | 批量写：`"3:0.1,4:-0.05,11:0.08"` |
| `variableCells` | string | 否 | null | 批量将单元设为 Variable：`"3,4,11"` |

**输出**：`SetExtraDataResult { Success, Error?, SurfaceNumber, SurfaceType, Entries: [...] }`（同 Get 的 Entries 格式，返回所改单元的 readback）

### 实现要点

- ZOSAPI 的 XDAT 访问路径在本机 version 需实测确认，优先级顺序：
  1. `surface.GetSurfaceCell(SurfaceColumn.ExtraData0 + N)` — 若枚举存在
  2. `((IEditorCell)surface).GetCellAt(absoluteColumnIndex)` — 用 Programming Tab "Extra Data Values" 对应列号
  3. `dynSurface.ExtraData.GetCell(N)` — 部分版本暴露的 `ISurfaceExtraData`
- 三条路径全部用 `dynamic` + try/catch 兜底，成功一次即记录该 surface type → 路径的映射（仅本次调用生命周期，不持久化）
- 读取时 `value = cell.DoubleValue`，失败 fallback `cell.IntegerValue`（对齐 `SetSurfaceParameterTool.cs:81-85` 做法）
- 写入时 `cell.DoubleValue = v`，失败 fallback `cell.IntegerValue = (int)v`
- `cell.MakeSolveVariable()` 设置为优化变量；`IsVariable` 通过 `cell.GetSolveData()?.Type == SolveType.Variable` 读取
- 自动探测 `endCell`：N=1..250 循环，连续 3 次抛异常即停（不以 value==0 判停，因为合法 XDAT 单元可以写 0）

### 风险缓解

- 若 3 条路径都失败：返回 `Error = "ExtraData access not available for surface type X in this ZOSAPI version. Surface type: Y"`，不抛异常。
- 计划阶段应在真实 OpticStudio 上跑 `zemax_get_surface` 读 `ZernikeStandardPhase` 面，确认哪条路径可用，将结果记入 DEVELOPMENT_NOTES.md。

---

## 工具 4：`zemax_pop`

**文件**：`src/ZemaxMCP.Server/Tools/Analysis/PopTool.cs`
**Program.cs 注册段**：Analysis Tools

### 接口

| 字段 | 类型 | 必填 | 默认 | 说明 |
|------|------|------|------|------|
| `startSurface` | int | 否 | 1 | POP 起始面 |
| `endSurface` | int | 否 | -1 | POP 结束面；-1=image |
| `wavelength` | int | 否 | 1 | 波长编号 |
| `field` | int | 否 | 1 | 视场编号 |
| `beamType` | string | 否 | "GaussianWaist" | `GaussianWaist`/`GaussianAngle`/`GaussianSize`/`TopHat`/`FileBeam` |
| `beamParam1` | double | 否 | 0 | Waist X（mm；beam 类型相关） |
| `beamParam2` | double | 否 | 0 | Waist Y |
| `beamParam3` | double | 否 | 0 | Decenter X |
| `beamParam4` | double | 否 | 0 | Decenter Y |
| `xSampling` | int | 否 | 5 | 1→32, 2→64, 3→128, 4→256, 5→512, 6→1024, 7→2048, 8→4096 |
| `ySampling` | int | 否 | 5 | 同上 |
| `xWidth` | double | 否 | 0 | 物理宽度（lens unit，通常 mm）；0+`autoCalculate=true` 时 Zemax 自算 |
| `yWidth` | double | 否 | 0 | 同上 |
| `autoCalculate` | bool | 否 | true | 启用 Zemax Auto 采样/宽度 |
| `dataType` | string | 否 | "Irradiance" | `Irradiance`/`PhaseRadians`/`RealPart`/`ImagPart`/`Ex`/`Ey` |
| `peakNormalize` | bool | 否 | false | Peak irradiance normalization |
| `outputGridPath` | string | 否 | null | grid 写磁盘路径（raw bin，见下） |
| `exportBmpPath` | string | 否 | null | 导出 BMP 的路径（复用 `AnalysisBmpHelper`） |

### 输出

```
PopResult {
  Success, Error?,
  StartSurface, EndSurface, Wavelength, Field,
  BeamType, DataType, PeakIrradiance, TotalPower,
  GridWidthX, GridWidthY,   // lens unit (mm)
  PixelPitchX, PixelPitchY, // lens unit (mm)
  Nx, Ny,
  Grid: double[][]?,        // 行主序 (Ny × Nx)；大 grid 时为 null
  GridFilePath: string?,    // 若 outputGridPath 被填且写成功
  BmpFilePath: string?      // 若 exportBmpPath 被填且写成功
}
```

### 实现要点

1. `analysis = system.Analyses.New_Analysis(AnalysisIDM.PhysicalOpticsPropagation)`
2. `settings = analysis.GetSettings()`；属性访问全程 `dynamic`，容错 ZOSAPI 版本差异
3. Map 参数：
   - `beamType` → `Enum.TryParse<POPBeamTypes>` 大小写不敏感
   - `xSampling`/`ySampling` → 沿用 `DiffractionEncircledEnergyTool.MapSampling` 风格的 switch，扩展到 1024/2048/4096
   - `dataType` → ZOSAPI `POPDataTypes` 枚举
4. `settings.StartSurface / EndSurface / Wavelength / Field / BeamType / ...`（具体属性名在实机验证）
5. `analysis.ApplyAndWaitForCompletion()`
6. 读 grid：`results.GetDataGrid(0)` 返回 `IAR_DataGrid`
   - 读 `.Nx`、`.Ny`、`.Dx`、`.Dy`、`.MinX`、`.MinY`、`.Values[y,x]`
   - 若该 API 不可用，次选 `GetDataGridDouble(0)` 或解析 `GetTextFile` 输出
7. 返回策略：
   - `Nx * Ny <= 65536`（即 ≤ 256×256）：inline `Grid: double[][]` 直接走 JSON
   - `> 65536`：强制需要 `outputGridPath`，否则返回 `Error`；写 raw bin 格式 = 24 字节 header（`int32 Nx` + `int32 Ny` + `float64 Dx` + `float64 Dy`，小端序），然后 `Ny × Nx × 8` 字节 float64 小端序 raw 数据，行主序
   - 同时支持 `exportBmpPath` → 走 `AnalysisBmpHelper.TryExportBmp(results, path)`
8. `analysis.Close()` 在 `finally` 块（对齐 `DiffractionEncircledEnergyTool.cs:56`）

### 与 `zemax_export_analysis` 的关系

`ExportAnalysisTool` 已支持 `PhysicalOpticsPropagation` 的 BMP/TXT 导出。新 `zemax_pop` 定位为"结构化 POP 专用工具"，提供 grid 数组级访问；两者并存。

### 风险点

- ZOSAPI 属性名 `BeamType` vs `SourceBeamType` 需实测
- `GetDataGrid` vs `GetDataGridDouble` vs 解析 TextFile 三选一，计划阶段按可用性落地
- Large grid 的 stdio 传输：512×512 double 直出 = 2 MB JSON，会阻塞 MCP；阈值 256×256 是经验值

---

## Program.cs 改动

在 `src/ZemaxMCP.Server/Program.cs` 对应段加注册（保持按类别分组）：

```csharp
// Lens Data Tools（108–121 行之间）
.WithTools<ZemaxMCP.Server.Tools.LensData.RemoveSurfaceTool>()
.WithTools<ZemaxMCP.Server.Tools.LensData.ListSurfaceTypesTool>()
.WithTools<ZemaxMCP.Server.Tools.LensData.GetExtraDataTool>()
.WithTools<ZemaxMCP.Server.Tools.LensData.SetExtraDataTool>()

// Analysis Tools（67–88 行之间）
.WithTools<ZemaxMCP.Server.Tools.Analysis.PopTool>()
```

---

## 文档更新

- `README.md`：在 Lens Data Tools / Analysis Tools 表格分别追加 5 行（工具名、描述、参数）
- `DEVELOPMENT_NOTES.md`：追加一小节 "XDAT Access Paths"，记录实机验证的可用 ZOSAPI 路径（待计划阶段补齐）

---

## 测试策略

| 工具 | Smoke test |
|------|-----------|
| `remove_surface` | Double Gauss → add_surface → remove_surface 回原状，对比 NumberOfSurfaces |
| `list_surface_types` | 调用后验证 `AvailableTypes` 含 `Standard`、`ZernikeStandardPhase`、`CoordinateBreak` 等关键项 |
| `get/set_extra_data` | 插 `ZernikeStandardPhase` 面 → set Z4=0.1 → get 验证读回 0.1 |
| `pop` | Double Gauss 示例 → 默认参数运行 → 验证 grid 非零、Nx/Ny 与 sampling 匹配 |

无自动化 xUnit 集成；手工 + Claude 调用验证即可。

---

## 成功判据

1. 5 个新工具在 `claude mcp list` 后可被调用，无注册遗漏
2. 上述 4 条 smoke test 全部通过
3. Mephisto WFS 流程里彻底不再需要手工读写 .zmx UTF-16 文本
4. 可用 MCP 工具生成"Zernike phase + defocus → donut intensity grid"完整流程，无 fallback
