# Zemax MCP: XDAT / POP / Surface-Ops Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 5 new MCP tools to Zemax MCP Server to cover 4 functionality gaps identified in Mephisto WFS work: `zemax_remove_surface`, `zemax_list_surface_types`, `zemax_get_extra_data`, `zemax_set_extra_data`, `zemax_pop`.

**Architecture:** Follow existing "one tool per class" pattern. Each tool lives in `src/ZemaxMCP.Server/Tools/<Category>/<Name>Tool.cs`, uses `IZemaxSession.ExecuteAsync(commandName, parameters, op)` wrapper, returns a `record` result type. Register each tool in `src/ZemaxMCP.Server/Program.cs` under the matching category section. Use `dynamic` for ZOSAPI calls with version-sensitive signatures (XDAT access, POP settings).

**Tech Stack:** C# / .NET Framework 4.8, ZOSAPI (Zemax OpticStudio API), ModelContextProtocol.Server, xUnit-free (no test project; smoke test via Claude MCP calls).

**Working Directory:** `E:/ZemaxProject/ZemaxMCP_源码_本地开发版`

**Spec:** [docs/superpowers/specs/2026-04-23-zemax-mcp-xdat-pop-tools-design.md](../specs/2026-04-23-zemax-mcp-xdat-pop-tools-design.md)

---

## Testing Strategy (No xUnit)

This project has no automated test project. Each task's "verify" step uses one of:

- **Build check:** `dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj` — expects `Build succeeded. 0 Error(s)`.
- **Tool registration check:** After build, restart MCP session and call `zemax_status` — verify new tool names appear in the server's tool list (the user runs this in Claude Code).
- **Smoke test (manual):** Sequence of Claude-Code MCP calls documented per tool, using `TestCase/Double Gauss 28 degree field SYN3.zmx` as base system.

Manual smoke tests run during Task 7 after all tools are in place. Individual tasks only verify via build check — functional verification is batched at the end because starting/stopping OpticStudio between every task is expensive.

---

## Task 0: Setup Feature Branch & Baseline Build

**Files:** None (git + build only)

- [ ] **Step 0.1: Verify clean state on main**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git status
```

Expected: On branch `main`, untracked: `docs/superpowers/` (spec + this plan), no other uncommitted changes.

- [ ] **Step 0.2: Create feature branch**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git checkout -b feat/xdat-pop-tools
```

Expected: `Switched to a new branch 'feat/xdat-pop-tools'`.

- [ ] **Step 0.3: Commit spec + plan files on the new branch**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git add docs/superpowers/ && git commit -m "docs: add spec and plan for XDAT/POP/surface-ops tools"
```

Expected: 1 commit created with 2 files.

- [ ] **Step 0.4: Baseline build (confirm nothing is already broken)**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj 2>&1 | tail -20
```

Expected: `Build succeeded. 0 Error(s)`. If this fails, stop and ask user — the baseline build must be green before adding code.

- [ ] **Step 0.5: Kill any running MCP server to free locked binaries**

Run:
```bash
taskkill //F //IM ZemaxMCP.Server.exe 2>&1 || echo "not running"
```

Expected: Either `SUCCESS: The process "ZemaxMCP.Server.exe"...` or `not running`. Either is fine.

---

## Task 1: `zemax_remove_surface`

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/LensData/RemoveSurfaceTool.cs`
- Modify: `src/ZemaxMCP.Server/Program.cs` (add one `.WithTools<>()` line in Lens Data section, ~line 121)

- [ ] **Step 1.1: Create `RemoveSurfaceTool.cs`**

Full content of `src/ZemaxMCP.Server/Tools/LensData/RemoveSurfaceTool.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class RemoveSurfaceTool
{
    private readonly IZemaxSession _session;

    public RemoveSurfaceTool(IZemaxSession session) => _session = session;

    public record RemoveSurfaceResult(
        bool Success,
        string? Error = null,
        int RemovedSurfaceNumber = 0,
        int TotalSurfaces = 0);

    [McpServerTool(Name = "zemax_remove_surface")]
    [Description(
        "Delete a surface from the Lens Data Editor at the specified surface number. "
        + "Object (0) and Image (last) surfaces cannot be deleted. "
        + "Useful to undo an accidental insert without rebuilding the system.")]
    public async Task<RemoveSurfaceResult> ExecuteAsync(
        [Description("Surface number to remove (cannot be 0 Object or last Image)")] int surfaceNumber)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber
            };

            return await _session.ExecuteAsync("RemoveSurface", parameters, system =>
            {
                var lde = system.LDE;
                int totalBefore = lde.NumberOfSurfaces;

                if (surfaceNumber < 0 || surfaceNumber >= totalBefore)
                    return new RemoveSurfaceResult(false,
                        Error: $"Invalid surface number: {surfaceNumber}. Valid range: 1-{totalBefore - 2} (0=Object, {totalBefore - 1}=Image not removable).");

                if (surfaceNumber == 0)
                    return new RemoveSurfaceResult(false,
                        Error: "Object surface (0) cannot be removed.");

                if (surfaceNumber == totalBefore - 1)
                    return new RemoveSurfaceResult(false,
                        Error: $"Image surface ({surfaceNumber}) cannot be removed.");

                bool ok = lde.RemoveSurfaceAt(surfaceNumber);
                int totalAfter = lde.NumberOfSurfaces;

                if (!ok || totalAfter != totalBefore - 1)
                    return new RemoveSurfaceResult(false,
                        Error: $"RemoveSurfaceAt returned false or surface count unchanged (before={totalBefore}, after={totalAfter}).",
                        RemovedSurfaceNumber: surfaceNumber,
                        TotalSurfaces: totalAfter);

                return new RemoveSurfaceResult(
                    Success: true,
                    RemovedSurfaceNumber: surfaceNumber,
                    TotalSurfaces: totalAfter);
            });
        }
        catch (Exception ex)
        {
            return new RemoveSurfaceResult(false, Error: ex.Message);
        }
    }
}
```

- [ ] **Step 1.2: Register in `Program.cs`**

In `src/ZemaxMCP.Server/Program.cs`, find this line (around line 121):

```csharp
    .WithTools<ZemaxMCP.Server.Tools.LensData.SetSurfaceTypeTool>()
```

Add immediately after it:

```csharp
    .WithTools<ZemaxMCP.Server.Tools.LensData.RemoveSurfaceTool>()
```

- [ ] **Step 1.3: Build and verify**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`. If build fails, read the error output carefully and fix before moving on.

- [ ] **Step 1.4: Commit**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git add src/ZemaxMCP.Server/Tools/LensData/RemoveSurfaceTool.cs src/ZemaxMCP.Server/Program.cs && git commit -m "feat(lens-data): add zemax_remove_surface tool"
```

Expected: 1 commit with 2 files changed.

---

## Task 2: `zemax_list_surface_types` + Fix `SetSurfaceTypeTool` Fallback

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/LensData/ListSurfaceTypesTool.cs`
- Modify: `src/ZemaxMCP.Server/Tools/LensData/SetSurfaceTypeTool.cs` (lines 55-73, the `listTypes=true` branch)
- Modify: `src/ZemaxMCP.Server/Program.cs` (one `.WithTools<>()` line)

- [ ] **Step 2.1: Create `ListSurfaceTypesTool.cs`**

Full content of `src/ZemaxMCP.Server/Tools/LensData/ListSurfaceTypesTool.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class ListSurfaceTypesTool
{
    private readonly IZemaxSession _session;

    public ListSurfaceTypesTool(IZemaxSession session) => _session = session;

    public record ListSurfaceTypesResult(
        bool Success,
        string? Error = null,
        string[]? AvailableTypes = null,
        string? Source = null);

    [McpServerTool(Name = "zemax_list_surface_types")]
    [Description(
        "List all surface type names supported by the installed ZOSAPI version. "
        + "Returns the names of the ZOSAPI.Editors.LDE.SurfaceType enum, which is stable "
        + "across ZOSAPI versions (unlike the dynamic GetAvailableSurfaceTypes() call "
        + "that fails in some versions). Some listed types may require specific licenses "
        + "or DLLs (e.g., UserDefined) to actually apply.")]
    public Task<ListSurfaceTypesResult> ExecuteAsync()
    {
        try
        {
            var names = Enum.GetNames(typeof(ZOSAPI.Editors.LDE.SurfaceType));
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new ListSurfaceTypesResult(
                Success: true,
                AvailableTypes: names,
                Source: "ZOSAPI.Editors.LDE.SurfaceType enum"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ListSurfaceTypesResult(false, Error: ex.Message));
        }
    }
}
```

Note: This tool does not call `_session.ExecuteAsync` because reading the enum does not require a Zemax connection.

- [ ] **Step 2.2: Patch `SetSurfaceTypeTool.cs` `listTypes` branch with enum fallback**

In `src/ZemaxMCP.Server/Tools/LensData/SetSurfaceTypeTool.cs`, find this block (around lines 55-73):

```csharp
                if (listTypes)
                {
                    // Use dynamic to access GetSurfaceTypeSettings and enumerate types
                    try
                    {
                        dynamic dynSurface = surface;
                        var settings = dynSurface.GetSurfaceTypeSettings(surface.Type);
                        var types = (string[])settings.GetAvailableSurfaceTypes();
                        return new SetSurfaceTypeResult(
                            Success: true,
                            SurfaceNumber: surfaceNumber,
                            PreviousType: previousType,
                            AvailableTypes: types);
                    }
                    catch
                    {
                        return new SetSurfaceTypeResult(false,
                            Error: "Unable to list surface types in this ZOSAPI version.");
                    }
                }
```

Replace the `catch` block so it falls back to the enum:

```csharp
                if (listTypes)
                {
                    // Primary path: dynamic GetAvailableSurfaceTypes (version-sensitive)
                    try
                    {
                        dynamic dynSurface = surface;
                        var settings = dynSurface.GetSurfaceTypeSettings(surface.Type);
                        var types = (string[])settings.GetAvailableSurfaceTypes();
                        return new SetSurfaceTypeResult(
                            Success: true,
                            SurfaceNumber: surfaceNumber,
                            PreviousType: previousType,
                            AvailableTypes: types);
                    }
                    catch
                    {
                        // Fallback: enumerate the SurfaceType enum (stable across versions)
                        var enumNames = Enum.GetNames(typeof(ZOSAPI.Editors.LDE.SurfaceType));
                        Array.Sort(enumNames, StringComparer.OrdinalIgnoreCase);
                        return new SetSurfaceTypeResult(
                            Success: true,
                            SurfaceNumber: surfaceNumber,
                            PreviousType: previousType,
                            AvailableTypes: enumNames);
                    }
                }
```

- [ ] **Step 2.3: Register in `Program.cs`**

In `src/ZemaxMCP.Server/Program.cs`, immediately after the line added in Task 1:

```csharp
    .WithTools<ZemaxMCP.Server.Tools.LensData.RemoveSurfaceTool>()
```

Add:

```csharp
    .WithTools<ZemaxMCP.Server.Tools.LensData.ListSurfaceTypesTool>()
```

- [ ] **Step 2.4: Build and verify**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2.5: Commit**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git add src/ZemaxMCP.Server/Tools/LensData/ListSurfaceTypesTool.cs src/ZemaxMCP.Server/Tools/LensData/SetSurfaceTypeTool.cs src/ZemaxMCP.Server/Program.cs && git commit -m "feat(lens-data): add zemax_list_surface_types and enum fallback in SetSurfaceTypeTool"
```

Expected: 1 commit with 3 files changed.

---

## Task 3: XDAT Read/Write — `zemax_get_extra_data` + `zemax_set_extra_data`

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/LensData/ExtraDataHelper.cs` (private helper shared by Get + Set, within same project)
- Create: `src/ZemaxMCP.Server/Tools/LensData/GetExtraDataTool.cs`
- Create: `src/ZemaxMCP.Server/Tools/LensData/SetExtraDataTool.cs`
- Modify: `src/ZemaxMCP.Server/Program.cs` (two `.WithTools<>()` lines)

**Rationale for helper class:** Get and Set both need the same "try three paths to access XDAT cell N" logic. Inlining twice violates DRY and risks the two tools diverging. One small internal helper with one public static method is the minimum abstraction.

- [ ] **Step 3.1: Create `ExtraDataHelper.cs`**

Full content of `src/ZemaxMCP.Server/Tools/LensData/ExtraDataHelper.cs`:

```csharp
using ZOSAPI.Editors;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Server.Tools.LensData;

/// Encapsulates the three ZOSAPI access paths for XDAT (Extra Data Editor) cells.
/// ZOSAPI exposes XDAT differently across versions; the helper tries each path
/// in priority order and returns the first IEditorCell that responds.
internal static class ExtraDataHelper
{
    public enum AccessPath { Unknown, SurfaceColumnExtraData, GetCellAtAbsolute, ExtraDataGetCell }

    /// Returns a cell-like object that supports DoubleValue / IntegerValue / MakeSolveVariable.
    /// Callers use dynamic to read/write. Returns null if no path worked.
    /// Records the successful path in <paramref name="pathUsed"/> for diagnostics.
    public static dynamic? TryGetCell(ILDERow surface, int cellNumber, out AccessPath pathUsed)
    {
        pathUsed = AccessPath.Unknown;

        // Path 1: SurfaceColumn.ExtraData0 + N (if the enum value exists)
        try
        {
            var extraDataEnum = (SurfaceColumn)Enum.Parse(typeof(SurfaceColumn), "ExtraData0", ignoreCase: true);
            var col = (SurfaceColumn)((int)extraDataEnum + cellNumber);
            var cell = surface.GetSurfaceCell(col);
            if (cell != null)
            {
                pathUsed = AccessPath.SurfaceColumnExtraData;
                return cell;
            }
        }
        catch { /* enum value missing in this ZOSAPI version; fall through */ }

        // Path 2: GetCellAt(absoluteColumnIndex). XDAT columns follow the standard columns.
        // Standard LDE has ~12 visible columns (Type, Comment, Radius, Thickness, Material,
        // Coating, SemiDiameter, ChipZone, MechSemiDia, Conic, TCE, + Par1..Par14).
        // Absolute XDAT column index = standardColumnCount + N. Try N+14 first (after Par14),
        // then a few common alternates; the first non-null wins.
        foreach (int offset in new[] { 14, 26, 28, 30 })
        {
            try
            {
                dynamic dynSurface = surface;
                var cell = dynSurface.GetCellAt(offset + cellNumber);
                if (cell != null)
                {
                    pathUsed = AccessPath.GetCellAtAbsolute;
                    return cell;
                }
            }
            catch { /* method missing or column out of range; try next offset */ }
        }

        // Path 3a: surface.ExtraDataCell(N) — direct method
        try
        {
            dynamic dynSurface = surface;
            var cell = dynSurface.ExtraDataCell(cellNumber);
            if (cell != null)
            {
                pathUsed = AccessPath.ExtraDataGetCell;
                return cell;
            }
        }
        catch { /* method missing */ }

        // Path 3b: surface.ExtraData.GetCell(N) — property then method
        try
        {
            dynamic dynSurface = surface;
            var xd = dynSurface.ExtraData;
            if (xd != null)
            {
                var cell = xd.GetCell(cellNumber);
                if (cell != null)
                {
                    pathUsed = AccessPath.ExtraDataGetCell;
                    return cell;
                }
            }
        }
        catch { /* property missing */ }

        // Path 3c: surface.GetExtraData(N)
        try
        {
            dynamic dynSurface = surface;
            var cell = dynSurface.GetExtraData(cellNumber);
            if (cell != null)
            {
                pathUsed = AccessPath.ExtraDataGetCell;
                return cell;
            }
        }
        catch { /* method missing */ }

        return null;
    }

    /// Reads the numeric value of a cell, falling back to IntegerValue on failure.
    public static double ReadValue(dynamic cell)
    {
        try { return (double)cell.DoubleValue; }
        catch { return (double)(int)cell.IntegerValue; }
    }

    /// Writes a numeric value, falling back to IntegerValue if DoubleValue rejects.
    public static void WriteValue(dynamic cell, double value)
    {
        try { cell.DoubleValue = value; }
        catch { cell.IntegerValue = (int)value; }
    }

    /// Queries whether a cell has a Variable solve attached.
    public static bool IsVariable(dynamic cell)
    {
        try
        {
            var solveData = cell.GetSolveData();
            if (solveData == null) return false;
            string typeName = solveData.Type.ToString();
            return string.Equals(typeName, "Variable", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
```

- [ ] **Step 3.2: Create `GetExtraDataTool.cs`**

Full content of `src/ZemaxMCP.Server/Tools/LensData/GetExtraDataTool.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class GetExtraDataTool
{
    private readonly IZemaxSession _session;

    public GetExtraDataTool(IZemaxSession session) => _session = session;

    public record ExtraDataEntry(int Cell, double Value, bool IsVariable);

    public record GetExtraDataResult(
        bool Success,
        string? Error = null,
        int SurfaceNumber = 0,
        string? SurfaceType = null,
        string? AccessPath = null,
        ExtraDataEntry[]? Entries = null);

    [McpServerTool(Name = "zemax_get_extra_data")]
    [Description(
        "Read cells from the Extra Data Editor (XDAT) for a surface. "
        + "XDAT stores per-surface-type data like Zernike coefficients, Max Term, "
        + "Normalization Radius, extended-polynomial terms, etc. "
        + "When endCell=0, auto-detects by reading until 3 consecutive cells fail. "
        + "Returns the access path used (SurfaceColumnExtraData / GetCellAtAbsolute / "
        + "ExtraDataGetCell) for diagnostics.")]
    public async Task<GetExtraDataResult> ExecuteAsync(
        [Description("Surface number")] int surfaceNumber,
        [Description("Start cell number (1-indexed)")] int startCell = 1,
        [Description("End cell number (0 = auto-detect; stops after 3 consecutive read failures)")] int endCell = 0)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber,
                ["startCell"] = startCell,
                ["endCell"] = endCell
            };

            return await _session.ExecuteAsync("GetExtraData", parameters, system =>
            {
                var lde = system.LDE;
                if (surfaceNumber < 0 || surfaceNumber >= lde.NumberOfSurfaces)
                    return new GetExtraDataResult(false,
                        Error: $"Invalid surface number: {surfaceNumber}. Valid range: 0-{lde.NumberOfSurfaces - 1}.");

                var surface = lde.GetSurfaceAt(surfaceNumber);
                string surfType = surface.Type.ToString();

                if (startCell < 1) startCell = 1;
                int hardLimit = endCell > 0 ? endCell : 250;

                var entries = new List<ExtraDataEntry>();
                int consecutiveFailures = 0;
                ExtraDataHelper.AccessPath pathUsed = ExtraDataHelper.AccessPath.Unknown;

                for (int n = startCell; n <= hardLimit; n++)
                {
                    var cell = ExtraDataHelper.TryGetCell(surface, n, out var path);
                    if (cell == null)
                    {
                        consecutiveFailures++;
                        if (endCell == 0 && consecutiveFailures >= 3) break;
                        continue;
                    }

                    if (pathUsed == ExtraDataHelper.AccessPath.Unknown) pathUsed = path;

                    try
                    {
                        double v = ExtraDataHelper.ReadValue(cell);
                        bool isVar = ExtraDataHelper.IsVariable(cell);
                        entries.Add(new ExtraDataEntry(n, v, isVar));
                        consecutiveFailures = 0;
                    }
                    catch
                    {
                        consecutiveFailures++;
                        if (endCell == 0 && consecutiveFailures >= 3) break;
                    }
                }

                if (entries.Count == 0)
                    return new GetExtraDataResult(false,
                        Error: $"No XDAT cells accessible for surface {surfaceNumber} (type {surfType}). " +
                               "Check that the surface type supports Extra Data.",
                        SurfaceNumber: surfaceNumber,
                        SurfaceType: surfType);

                return new GetExtraDataResult(
                    Success: true,
                    SurfaceNumber: surfaceNumber,
                    SurfaceType: surfType,
                    AccessPath: pathUsed.ToString(),
                    Entries: entries.ToArray());
            });
        }
        catch (Exception ex)
        {
            return new GetExtraDataResult(false, Error: ex.Message);
        }
    }
}
```

- [ ] **Step 3.3: Create `SetExtraDataTool.cs`**

Full content of `src/ZemaxMCP.Server/Tools/LensData/SetExtraDataTool.cs`:

```csharp
using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetExtraDataTool
{
    private readonly IZemaxSession _session;

    public SetExtraDataTool(IZemaxSession session) => _session = session;

    public record ExtraDataEntry(int Cell, double Value, bool IsVariable);

    public record SetExtraDataResult(
        bool Success,
        string? Error = null,
        int SurfaceNumber = 0,
        string? SurfaceType = null,
        string? AccessPath = null,
        ExtraDataEntry[]? Entries = null);

    [McpServerTool(Name = "zemax_set_extra_data")]
    [Description(
        "Write cells in the Extra Data Editor (XDAT) for a surface. "
        + "Three modes: "
        + "(1) single set: provide cell + value; "
        + "(2) batch set: provide batchSet='3:0.1,4:-0.05,11:0.08'; "
        + "(3) variable flag: provide variableCells='3,4,11' to mark cells as optimization variables "
        + "(works standalone or combined with value writes). "
        + "Returns the readback of all modified cells.")]
    public async Task<SetExtraDataResult> ExecuteAsync(
        [Description("Surface number")] int surfaceNumber,
        [Description("Single set: cell number (1-indexed); 0 = not used")] int cell = 0,
        [Description("Single set: value to write")] double? value = null,
        [Description("Single set: mark this cell as Variable after write")] bool? makeVariable = null,
        [Description("Batch set: comma-separated 'cell:value' pairs, e.g. '3:0.1,4:-0.05'")] string? batchSet = null,
        [Description("Batch mark as Variable: comma-separated cell numbers, e.g. '3,4,11'")] string? variableCells = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber,
                ["cell"] = cell,
                ["value"] = value,
                ["batchSet"] = batchSet,
                ["variableCells"] = variableCells
            };

            return await _session.ExecuteAsync("SetExtraData", parameters, system =>
            {
                var lde = system.LDE;
                if (surfaceNumber < 0 || surfaceNumber >= lde.NumberOfSurfaces)
                    return new SetExtraDataResult(false,
                        Error: $"Invalid surface number: {surfaceNumber}. Valid range: 0-{lde.NumberOfSurfaces - 1}.");

                var surface = lde.GetSurfaceAt(surfaceNumber);
                string surfType = surface.Type.ToString();

                var writes = new List<(int cellNum, double val)>();
                var varMarks = new HashSet<int>();
                ExtraDataHelper.AccessPath pathUsed = ExtraDataHelper.AccessPath.Unknown;

                // Parse batchSet
                if (!string.IsNullOrWhiteSpace(batchSet))
                {
                    foreach (var pair in batchSet.Split(','))
                    {
                        var parts = pair.Trim().Split(':');
                        if (parts.Length != 2)
                            return new SetExtraDataResult(false,
                                Error: $"Invalid batchSet entry: '{pair}'. Expected 'cell:value'.");

                        if (!int.TryParse(parts[0].Trim(), out int cn) ||
                            !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double cv))
                            return new SetExtraDataResult(false,
                                Error: $"Cannot parse batchSet entry: '{pair}'.");
                        writes.Add((cn, cv));
                    }
                }

                // Parse single set
                if (cell > 0 && value.HasValue)
                    writes.Add((cell, value.Value));

                // Parse variableCells
                if (!string.IsNullOrWhiteSpace(variableCells))
                {
                    foreach (var tok in variableCells.Split(','))
                    {
                        if (!int.TryParse(tok.Trim(), out int cn))
                            return new SetExtraDataResult(false,
                                Error: $"Cannot parse variableCells entry: '{tok}'.");
                        varMarks.Add(cn);
                    }
                }

                // Also mark the single-set cell variable if flagged
                if (makeVariable == true && cell > 0) varMarks.Add(cell);

                if (writes.Count == 0 && varMarks.Count == 0)
                    return new SetExtraDataResult(false,
                        Error: "No writes requested. Provide cell+value, batchSet, or variableCells.");

                // Execute writes
                var touched = new SortedSet<int>();
                foreach (var (cn, cv) in writes)
                {
                    var xcell = ExtraDataHelper.TryGetCell(surface, cn, out var path);
                    if (xcell == null)
                        return new SetExtraDataResult(false,
                            Error: $"Cannot access XDAT cell {cn} on surface {surfaceNumber} (type {surfType}).");
                    if (pathUsed == ExtraDataHelper.AccessPath.Unknown) pathUsed = path;

                    ExtraDataHelper.WriteValue(xcell, cv);
                    touched.Add(cn);
                }

                // Execute variable marks
                foreach (var cn in varMarks)
                {
                    var xcell = ExtraDataHelper.TryGetCell(surface, cn, out var path);
                    if (xcell == null)
                        return new SetExtraDataResult(false,
                            Error: $"Cannot access XDAT cell {cn} for Variable mark on surface {surfaceNumber}.");
                    if (pathUsed == ExtraDataHelper.AccessPath.Unknown) pathUsed = path;

                    try { xcell.MakeSolveVariable(); }
                    catch (Exception ex)
                    {
                        return new SetExtraDataResult(false,
                            Error: $"Failed to mark cell {cn} as Variable: {ex.Message}");
                    }
                    touched.Add(cn);
                }

                // Read back all touched cells
                var entries = new List<ExtraDataEntry>();
                foreach (var cn in touched)
                {
                    var xcell = ExtraDataHelper.TryGetCell(surface, cn, out _);
                    if (xcell == null) continue;
                    double v = ExtraDataHelper.ReadValue(xcell);
                    bool isVar = ExtraDataHelper.IsVariable(xcell);
                    entries.Add(new ExtraDataEntry(cn, v, isVar));
                }

                return new SetExtraDataResult(
                    Success: true,
                    SurfaceNumber: surfaceNumber,
                    SurfaceType: surfType,
                    AccessPath: pathUsed.ToString(),
                    Entries: entries.ToArray());
            });
        }
        catch (Exception ex)
        {
            return new SetExtraDataResult(false, Error: ex.Message);
        }
    }
}
```

- [ ] **Step 3.4: Register both tools in `Program.cs`**

In `src/ZemaxMCP.Server/Program.cs`, immediately after the `ListSurfaceTypesTool` line from Task 2:

```csharp
    .WithTools<ZemaxMCP.Server.Tools.LensData.ListSurfaceTypesTool>()
```

Add:

```csharp
    .WithTools<ZemaxMCP.Server.Tools.LensData.GetExtraDataTool>()
    .WithTools<ZemaxMCP.Server.Tools.LensData.SetExtraDataTool>()
```

- [ ] **Step 3.5: Build and verify**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`. If this fails with e.g. "`GetSolveData` not found on `dynamic`", investigate the ZOSAPI IEditorCell interface and patch `ExtraDataHelper.IsVariable`.

- [ ] **Step 3.6: Commit**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git add src/ZemaxMCP.Server/Tools/LensData/ExtraDataHelper.cs src/ZemaxMCP.Server/Tools/LensData/GetExtraDataTool.cs src/ZemaxMCP.Server/Tools/LensData/SetExtraDataTool.cs src/ZemaxMCP.Server/Program.cs && git commit -m "feat(lens-data): add zemax_get_extra_data and zemax_set_extra_data tools"
```

Expected: 1 commit with 4 files changed.

---

## Task 4: `zemax_pop` (Physical Optics Propagation)

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Analysis/PopTool.cs`
- Modify: `src/ZemaxMCP.Server/Program.cs` (one `.WithTools<>()` line in Analysis section)

- [ ] **Step 4.1: Create `PopTool.cs`**

Full content of `src/ZemaxMCP.Server/Tools/Analysis/PopTool.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Analysis;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class PopTool
{
    private readonly IZemaxSession _session;

    public PopTool(IZemaxSession session) => _session = session;

    public record PopResult(
        bool Success,
        string? Error = null,
        int StartSurface = 0,
        int EndSurface = 0,
        int Wavelength = 0,
        int Field = 0,
        string? BeamType = null,
        string? DataType = null,
        double PeakIrradiance = 0,
        double TotalPower = 0,
        double GridWidthX = 0,
        double GridWidthY = 0,
        double PixelPitchX = 0,
        double PixelPitchY = 0,
        int Nx = 0,
        int Ny = 0,
        double[][]? Grid = null,
        string? GridFilePath = null,
        string? BmpFilePath = null);

    // Inline threshold: 256x256 = 65536 cells ~ 2 MB JSON. Larger grids must write to disk.
    private const int InlineGridCellLimit = 65536;

    [McpServerTool(Name = "zemax_pop")]
    [Description(
        "Run Physical Optics Propagation and return the intensity (or phase) grid. "
        + "Used for wavefront sensor donut simulation: add Zernike phase via zemax_set_extra_data, "
        + "shift focus, then call this tool to get the defocused intensity pattern. "
        + "Grid <= 256x256 returns inline; larger grids require outputGridPath (raw float64 little-endian "
        + "with 24-byte header: int32 Nx | int32 Ny | float64 Dx | float64 Dy, then Ny*Nx*8 bytes row-major). "
        + "All linear units are lens units (usually mm).")]
    public async Task<PopResult> ExecuteAsync(
        [Description("Start surface for POP")] int startSurface = 1,
        [Description("End surface; -1 = image")] int endSurface = -1,
        [Description("Wavelength number (1-indexed)")] int wavelength = 1,
        [Description("Field number (1-indexed)")] int field = 1,
        [Description("Beam type: GaussianWaist, GaussianAngle, GaussianSize, TopHat, FileBeam, etc.")] string beamType = "GaussianWaist",
        [Description("Beam param 1 (Waist X or beam-type-specific first param, in lens units)")] double beamParam1 = 0,
        [Description("Beam param 2 (Waist Y)")] double beamParam2 = 0,
        [Description("Beam param 3 (Decenter X)")] double beamParam3 = 0,
        [Description("Beam param 4 (Decenter Y)")] double beamParam4 = 0,
        [Description("X sampling: 1=32,2=64,3=128,4=256,5=512,6=1024,7=2048,8=4096")] int xSampling = 5,
        [Description("Y sampling: same scale as xSampling")] int ySampling = 5,
        [Description("X width in lens units (0 = auto when autoCalculate=true)")] double xWidth = 0,
        [Description("Y width in lens units (0 = auto when autoCalculate=true)")] double yWidth = 0,
        [Description("Use Zemax auto sampling/width")] bool autoCalculate = true,
        [Description("Data type: Irradiance, PhaseRadians, RealPart, ImagPart, Ex, Ey")] string dataType = "Irradiance",
        [Description("Peak-irradiance normalization")] bool peakNormalize = false,
        [Description("Optional path to write raw grid (required if Nx*Ny > 65536)")] string? outputGridPath = null,
        [Description("Optional path to export BMP image")] string? exportBmpPath = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["startSurface"] = startSurface,
                ["endSurface"] = endSurface,
                ["wavelength"] = wavelength,
                ["field"] = field,
                ["beamType"] = beamType,
                ["xSampling"] = xSampling,
                ["ySampling"] = ySampling,
                ["xWidth"] = xWidth,
                ["yWidth"] = yWidth,
                ["autoCalculate"] = autoCalculate,
                ["dataType"] = dataType,
                ["peakNormalize"] = peakNormalize,
                ["outputGridPath"] = outputGridPath,
                ["exportBmpPath"] = exportBmpPath
            };

            return await _session.ExecuteAsync("Pop", parameters, system =>
            {
                var analysis = system.Analyses.New_Analysis(AnalysisIDM.PhysicalOpticsPropagation);
                try
                {
                    // Settings configuration via dynamic to tolerate ZOSAPI version drift
                    dynamic settings = analysis.GetSettings();

                    TrySet(() => settings.Wavelength = wavelength);
                    TrySet(() => settings.Field = field);
                    TrySet(() => settings.StartSurface = startSurface);
                    TrySet(() => settings.EndSurface = endSurface);
                    TrySet(() => settings.AutoCalculate = autoCalculate);
                    TrySet(() => settings.PeakIrradiance = peakNormalize);

                    TrySet(() => settings.XSampling = MapSampling(xSampling));
                    TrySet(() => settings.YSampling = MapSampling(ySampling));

                    if (!autoCalculate)
                    {
                        TrySet(() => settings.XWidth = xWidth);
                        TrySet(() => settings.YWidth = yWidth);
                    }

                    // Beam type — try enum parse across common ZOSAPI names
                    if (!TrySetBeamType(settings, beamType, out string beamTypeError))
                        return new PopResult(false, Error: beamTypeError);

                    TrySet(() => settings.BeamParameter1 = beamParam1);
                    TrySet(() => settings.BeamParameter2 = beamParam2);
                    TrySet(() => settings.BeamParameter3 = beamParam3);
                    TrySet(() => settings.BeamParameter4 = beamParam4);

                    if (!TrySetDataType(settings, dataType, out string dataTypeError))
                        return new PopResult(false, Error: dataTypeError);

                    analysis.ApplyAndWaitForCompletion();

                    var results = analysis.GetResults();

                    // Extract grid via dynamic (GetDataGrid / GetDataGridDouble)
                    dynamic? grid = null;
                    try { grid = results.DataGrids != null && results.NumberOfDataGrids > 0 ? results.DataGrids[0] : null; }
                    catch { }
                    if (grid == null)
                    {
                        try { grid = results.GetDataGrid(0); }
                        catch { }
                    }
                    if (grid == null)
                    {
                        try { grid = results.GetDataGridDouble(0); }
                        catch { }
                    }

                    if (grid == null)
                        return new PopResult(false,
                            Error: "POP analysis produced no data grid. Check startSurface/endSurface and beam settings.");

                    int nx = (int)grid.Nx;
                    int ny = (int)grid.Ny;
                    double dx = (double)grid.Dx;
                    double dy = (double)grid.Dy;
                    double widthX = dx * nx;
                    double widthY = dy * ny;

                    double peak = 0, total = 0;
                    try { peak = (double)grid.PeakValue; } catch { }
                    try { total = (double)grid.Total; } catch { }

                    // Copy values to double[][]
                    var values2d = new double[ny][];
                    for (int y = 0; y < ny; y++)
                    {
                        values2d[y] = new double[nx];
                        for (int x = 0; x < nx; x++)
                        {
                            try { values2d[y][x] = (double)grid.Values[y, x]; }
                            catch
                            {
                                try { values2d[y][x] = (double)grid.Values(y, x); } catch { }
                            }
                        }
                    }

                    // Decide inline vs file output
                    int cells = nx * ny;
                    double[][]? inlineGrid = null;
                    string? gridFilePath = null;

                    if (cells <= InlineGridCellLimit && string.IsNullOrEmpty(outputGridPath))
                    {
                        inlineGrid = values2d;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(outputGridPath))
                            return new PopResult(false,
                                Error: $"Grid {nx}x{ny}={cells} exceeds inline limit {InlineGridCellLimit}. Provide outputGridPath.");

                        EnsureDirectory(outputGridPath);
                        WriteGridBin(outputGridPath!, nx, ny, dx, dy, values2d);
                        gridFilePath = outputGridPath;
                    }

                    // Optional BMP export
                    string? bmpPath = null;
                    if (!string.IsNullOrEmpty(exportBmpPath))
                    {
                        EnsureDirectory(exportBmpPath);
                        if (AnalysisBmpHelper.TryExportBmp(results, exportBmpPath!))
                            bmpPath = exportBmpPath;
                    }

                    return new PopResult(
                        Success: true,
                        StartSurface: startSurface,
                        EndSurface: endSurface,
                        Wavelength: wavelength,
                        Field: field,
                        BeamType: beamType,
                        DataType: dataType,
                        PeakIrradiance: peak,
                        TotalPower: total,
                        GridWidthX: widthX,
                        GridWidthY: widthY,
                        PixelPitchX: dx,
                        PixelPitchY: dy,
                        Nx: nx,
                        Ny: ny,
                        Grid: inlineGrid,
                        GridFilePath: gridFilePath,
                        BmpFilePath: bmpPath);
                }
                finally
                {
                    analysis.Close();
                }
            });
        }
        catch (Exception ex)
        {
            return new PopResult(false, Error: ex.Message);
        }
    }

    private static void TrySet(Action setter)
    {
        try { setter(); } catch { /* property not supported in this ZOSAPI version; skip */ }
    }

    private static bool TrySetBeamType(dynamic settings, string beamType, out string error)
    {
        error = "";
        // Try on multiple candidate enum types and property names
        string[] enumTypeNames =
        {
            "ZOSAPI.Analysis.Settings.PhysicalOptics.POPBeamTypes",
            "ZOSAPI.Analysis.Settings.POP.POPBeamTypes",
            "ZOSAPI.Analysis.PhysicalOpticsPropagation.POPBeamTypes"
        };
        string[] propertyNames = { "BeamType", "SourceBeamType", "InputBeamType" };

        foreach (var tn in enumTypeNames)
        {
            var enumType = Type.GetType(tn + ", ZOSAPI");
            if (enumType == null) continue;
            if (!Enum.TryParse(enumType, beamType, ignoreCase: true, out object? enumVal)) continue;

            foreach (var pn in propertyNames)
            {
                try
                {
                    var prop = ((object)settings).GetType().GetProperty(pn);
                    if (prop == null) continue;
                    prop.SetValue(settings, enumVal);
                    return true;
                }
                catch { }
            }
        }

        // Fallback: try assigning the string directly (some ZOSAPI versions accept string setters)
        try { settings.BeamType = beamType; return true; } catch { }

        error = $"Unable to set beam type '{beamType}'. Tried enums POPBeamTypes in multiple namespaces and properties BeamType/SourceBeamType/InputBeamType.";
        return false;
    }

    private static bool TrySetDataType(dynamic settings, string dataType, out string error)
    {
        error = "";
        string[] enumTypeNames =
        {
            "ZOSAPI.Analysis.Settings.PhysicalOptics.POPDataTypes",
            "ZOSAPI.Analysis.Settings.POP.POPDataTypes",
            "ZOSAPI.Analysis.PhysicalOpticsPropagation.POPDataTypes"
        };
        string[] propertyNames = { "DataType", "OutputDataType" };

        foreach (var tn in enumTypeNames)
        {
            var enumType = Type.GetType(tn + ", ZOSAPI");
            if (enumType == null) continue;
            if (!Enum.TryParse(enumType, dataType, ignoreCase: true, out object? enumVal)) continue;

            foreach (var pn in propertyNames)
            {
                try
                {
                    var prop = ((object)settings).GetType().GetProperty(pn);
                    if (prop == null) continue;
                    prop.SetValue(settings, enumVal);
                    return true;
                }
                catch { }
            }
        }

        try { settings.DataType = dataType; return true; } catch { }

        error = $"Unable to set data type '{dataType}'.";
        return false;
    }

    private static ZOSAPI.Analysis.SampleSizes MapSampling(int sampling) => sampling switch
    {
        1 => ZOSAPI.Analysis.SampleSizes.S_32x32,
        2 => ZOSAPI.Analysis.SampleSizes.S_64x64,
        3 => ZOSAPI.Analysis.SampleSizes.S_128x128,
        4 => ZOSAPI.Analysis.SampleSizes.S_256x256,
        5 => ZOSAPI.Analysis.SampleSizes.S_512x512,
        6 => ZOSAPI.Analysis.SampleSizes.S_1024x1024,
        7 => ZOSAPI.Analysis.SampleSizes.S_2048x2048,
        8 => ZOSAPI.Analysis.SampleSizes.S_4096x4096,
        _ => ZOSAPI.Analysis.SampleSizes.S_512x512
    };

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static void WriteGridBin(string path, int nx, int ny, double dx, double dy, double[][] values)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(nx);                // int32
        bw.Write(ny);                // int32
        bw.Write(dx);                // float64
        bw.Write(dy);                // float64
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
                bw.Write(values[y][x]);
    }
}
```

Note: if `MapSampling` fails with "S_2048x2048 does not exist", remove that arm and the 4096 arm; the project's ZOSAPI version may cap at 1024. The `TrySet` pattern keeps the tool working even if some settings are missing.

- [ ] **Step 4.2: Register in `Program.cs`**

In `src/ZemaxMCP.Server/Program.cs`, find the last Analysis tool registration (around line 88):

```csharp
    .WithTools<ZemaxMCP.Server.Tools.Analysis.GeometricImageAnalysisTool>()
```

Add immediately after it:

```csharp
    .WithTools<ZemaxMCP.Server.Tools.Analysis.PopTool>()
```

- [ ] **Step 4.3: Build and verify**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj 2>&1 | tail -20
```

Expected: `Build succeeded. 0 Error(s)`.

Likely build-error patches:
- If `SampleSizes.S_2048x2048` is missing → delete the 7 and 8 arms from `MapSampling`.
- If `AnalysisIDM.PhysicalOpticsPropagation` is missing → the ZOSAPI version does not ship POP; stop and report back. (This would also mean `ExportAnalysisTool`'s `fftpsf`/`huygenspsf` references are already broken, which they are not, so this is unlikely.)

- [ ] **Step 4.4: Commit**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git add src/ZemaxMCP.Server/Tools/Analysis/PopTool.cs src/ZemaxMCP.Server/Program.cs && git commit -m "feat(analysis): add zemax_pop for physical optics propagation with structured grid output"
```

Expected: 1 commit with 2 files changed.

---

## Task 5: Update README and DEVELOPMENT_NOTES

**Files:**
- Modify: `README.md` (add rows to Lens Data Tools table and Analysis Tools table)
- Modify: `DEVELOPMENT_NOTES.md` (append XDAT Access Paths section; path identification happens in Task 7 smoke test)

- [ ] **Step 5.1: Append rows to README.md Lens Data Tools table**

In `README.md`, find the Lens Data Tools table. The last row is:

```
| `zemax_set_aperture` | Set system aperture | `value` (**required**): Aperture value · `apertureType` (opt, default: `"EPD"`): `EPD`, `FNumber`, `ObjectNA`, `FloatByStop` |
```

After that row (and before the next `###` heading or blank line separator), add these rows:

```
| `zemax_remove_surface` | Delete a surface from the Lens Data Editor | `surfaceNumber` (**required**): Surface number to remove (Object 0 and Image not allowed) |
| `zemax_list_surface_types` | List surface type names supported by ZOSAPI | *none* |
| `zemax_get_extra_data` | Read Extra Data Editor (XDAT) cells for a surface | `surfaceNumber` (**required**) · `startCell` (opt, default: `1`) · `endCell` (opt, default: `0` = auto-detect) |
| `zemax_set_extra_data` | Write XDAT cells; batch and Variable-mark supported | `surfaceNumber` (**required**) · `cell` (opt) · `value` (opt) · `makeVariable` (opt) · `batchSet` (opt): e.g. `"3:0.1,4:-0.05"` · `variableCells` (opt): e.g. `"3,4,11"` |
```

- [ ] **Step 5.2: Append row to README.md Analysis Tools table**

In the Analysis Tools table, find the last row (after `zemax_geometric_encircled_energy`). Add:

```
| `zemax_pop` | Physical Optics Propagation returning intensity/phase grid | `startSurface` (opt, default: `1`) · `endSurface` (opt, default: `-1`=image) · `wavelength` (opt, default: `1`) · `field` (opt, default: `1`) · `beamType` (opt, default: `"GaussianWaist"`) · `beamParam1`-`beamParam4` (opt) · `xSampling`/`ySampling` (opt, default: `5`=512) · `xWidth`/`yWidth` (opt) · `autoCalculate` (opt, default: `true`) · `dataType` (opt, default: `"Irradiance"`): `Irradiance`, `PhaseRadians`, `RealPart`, `ImagPart`, `Ex`, `Ey` · `peakNormalize` (opt) · `outputGridPath` (opt): required if Nx*Ny > 65536 · `exportBmpPath` (opt) |
```

- [ ] **Step 5.3: Append XDAT section to DEVELOPMENT_NOTES.md**

Append to the end of `DEVELOPMENT_NOTES.md`:

```markdown

---

## XDAT (Extra Data Editor) Access Paths

The `zemax_get_extra_data` / `zemax_set_extra_data` tools try three ZOSAPI access paths in priority order. The `AccessPath` field in the tool's response tells you which one worked on your machine:

1. **`SurfaceColumnExtraData`** — `surface.GetSurfaceCell(SurfaceColumn.ExtraData0 + N)` (cleanest; requires the enum value to exist)
2. **`GetCellAtAbsolute`** — `surface.GetCellAt(absoluteColumnIndex)` where absolute index = base offset + N (base is one of 14, 26, 28, 30)
3. **`ExtraDataGetCell`** — `surface.ExtraData.GetCell(N)` or similar property path

**Empirical path on this installation:** (to be filled after first successful smoke test — see Task 7.4 of the XDAT/POP tools plan)

If all three paths fail, the tool returns a clear error with surface type info. Report such cases so a new path can be added.
```

- [ ] **Step 5.4: Commit**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git add README.md DEVELOPMENT_NOTES.md && git commit -m "docs: document new XDAT/POP/surface-ops tools in README and DEVELOPMENT_NOTES"
```

Expected: 1 commit with 2 files changed.

---

## Task 6: Pre-Smoke-Test Restart

**Files:** none (runtime only)

- [ ] **Step 6.1: Kill any running MCP server**

Run:
```bash
taskkill //F //IM ZemaxMCP.Server.exe 2>&1 || echo "not running"
```

- [ ] **Step 6.2: Kill any running OpticStudio background processes**

Run:
```bash
taskkill //F //IM OpticStudio.exe 2>&1 || echo "not running"
```

- [ ] **Step 6.3: Instruct the user to reload MCP in Claude Code**

Tell the user:

> Please run `/mcp` in Claude Code (or restart the Claude Code session) to reconnect to the rebuilt MCP server. After reconnecting, the 5 new tools should appear in the tool list: `zemax_remove_surface`, `zemax_list_surface_types`, `zemax_get_extra_data`, `zemax_set_extra_data`, `zemax_pop`.

Wait for user confirmation before proceeding to Task 7.

---

## Task 7: Smoke Tests (Manual via Claude MCP Calls)

**Files:** modify `DEVELOPMENT_NOTES.md` at the end (to record empirical XDAT access path).

Each step here is a Claude-Code prompt that invokes MCP tools. Record the actual JSON responses (or summaries) for each step so any regressions are caught.

- [ ] **Step 7.1: Smoke test `zemax_list_surface_types`**

Claude prompt:
> Call `zemax_list_surface_types` and report the returned `AvailableTypes` array.

Expected: `Success: true`, `AvailableTypes` includes at least `Standard`, `CoordinateBreak`, `EvenAspheric`, `ZernikeStandardSag`, `ZernikeStandardPhase` (or close variant names), sorted case-insensitive alphabetically.

- [ ] **Step 7.2: Smoke test `zemax_remove_surface`**

Claude prompt:
> Connect to OpticStudio standalone. Open `E:/ZemaxProject/ZemaxMCP_源码_本地开发版/TestCase/Double Gauss 28 degree field SYN3.zmx`. Call `zemax_get_system` and note `NumberOfSurfaces` (call it N). Call `zemax_add_surface` with `insertAt=2, thickness=1.0, comment='smoke-test-insert'`. Verify N grew to N+1. Call `zemax_remove_surface` with `surfaceNumber=2`. Verify N returned to original. Call `zemax_remove_surface` with `surfaceNumber=0` and confirm it fails with an Object-surface error. Call `zemax_remove_surface` with `surfaceNumber=N` (the image surface number) and confirm it fails with an Image-surface error.

Expected: All 4 assertions hold.

- [ ] **Step 7.3: Smoke test `zemax_get_extra_data` + `zemax_set_extra_data`**

Claude prompt:
> With the Double Gauss file still open, call `zemax_add_surface` with `insertAt=1, thickness=0`. Call `zemax_set_surface_type` on that new surface with `surfaceType='ZernikeStandardPhase'`. Call `zemax_get_extra_data` on that surface with `startCell=1, endCell=15`. Record the `AccessPath` field from the response. Then call `zemax_set_extra_data` with `surfaceNumber=<the new surface number>, batchSet='4:0.1,5:-0.05,11:0.08'`. Call `zemax_get_extra_data` again on cells 4, 5, 11 (set startCell=4, endCell=11). Verify values round-trip (within 1e-9). Then call `zemax_set_extra_data` with `variableCells='4,5,11'`. Call `zemax_get_extra_data` again and verify `IsVariable=true` for those cells.

Expected: Round-trip values match. `IsVariable=true` for the three marked cells. Record which `AccessPath` value (`SurfaceColumnExtraData` / `GetCellAtAbsolute` / `ExtraDataGetCell`) worked.

- [ ] **Step 7.4: Update DEVELOPMENT_NOTES.md with the empirical XDAT path**

Edit `DEVELOPMENT_NOTES.md`, replace the line:

```
**Empirical path on this installation:** (to be filled after first successful smoke test — see Task 7.4 of the XDAT/POP tools plan)
```

with the actual path observed in Step 7.3, e.g.:

```
**Empirical path on this installation:** `GetCellAtAbsolute` with base offset 14 (ZOSAPI shipped with OpticStudio 2024 R1), as of 2026-04-23.
```

Adjust the base offset and OpticStudio version to match what was observed.

- [ ] **Step 7.5: Smoke test `zemax_pop`**

Claude prompt:
> With the Double Gauss file and the ZernikeStandardPhase surface still in place, call `zemax_pop` with default params except `xSampling=3, ySampling=3` (128x128 inline), `autoCalculate=true`. Verify `Success=true`, `Nx=128`, `Ny=128`, `Grid` is non-null with the expected shape, `PeakIrradiance > 0`, `GridWidthX > 0`. Then call `zemax_pop` again with `xSampling=5, ySampling=5` (512x512) and `outputGridPath='E:/tmp/zemax_pop_test.bin'`. Verify `Success=true`, `Grid=null`, `GridFilePath` matches the requested path, and the file exists on disk (ask the user to `ls` it).

Expected: Both calls succeed. Inline grid for 128x128, disk grid for 512x512.

- [ ] **Step 7.6: Disconnect and commit the DEVELOPMENT_NOTES update**

Claude prompt:
> Call `zemax_disconnect`.

Then:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git add DEVELOPMENT_NOTES.md && git commit -m "docs: record empirical XDAT access path from smoke test"
```

Expected: 1 commit (only if path was filled in).

- [ ] **Step 7.7: Final check — all 7 commits on the feature branch**

Run:
```bash
cd "E:/ZemaxProject/ZemaxMCP_源码_本地开发版" && git log --oneline main..HEAD
```

Expected output (order and commit messages):
```
<hash> docs: record empirical XDAT access path from smoke test
<hash> docs: document new XDAT/POP/surface-ops tools in README and DEVELOPMENT_NOTES
<hash> feat(analysis): add zemax_pop for physical optics propagation with structured grid output
<hash> feat(lens-data): add zemax_get_extra_data and zemax_set_extra_data tools
<hash> feat(lens-data): add zemax_list_surface_types and enum fallback in SetSurfaceTypeTool
<hash> feat(lens-data): add zemax_remove_surface tool
<hash> docs: add spec and plan for XDAT/POP/surface-ops tools
```

(7 commits on branch `feat/xdat-pop-tools`.)

---

## Out of Scope

- No xUnit test project introduction (per design decision)
- No performance benchmarking of POP for large grids (2048×2048 and above)
- No automatic ZOSAPI path-detection persistence across sessions (each MCP session re-detects)
- No UI changes to `ConfigureClaudeMCP` or `ConfigureOllama` tools
- No `zemax_pop` extensions for multi-surface POP (e.g., propagate through series of defined intermediate pilot beams) — only startSurface→endSurface

## Done Definition

- 5 new tools callable in Claude Code after MCP reconnect
- 7 smoke tests in Task 7 pass
- Feature branch `feat/xdat-pop-tools` has 7 commits
- Mephisto WFS workflow can do: set Zernike phase via `zemax_set_extra_data` → call `zemax_pop` → get donut grid, with no `.zmx` text manipulation
