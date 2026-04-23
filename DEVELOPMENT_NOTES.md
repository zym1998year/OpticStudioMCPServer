# ZEMAX MCP Server Development Notes

## Adding New Tools - CRITICAL CHECKLIST

When creating a new MCP tool, you MUST complete ALL of these steps:

### Step 1: Create the Tool Class
- Create the tool class file in the appropriate `Tools/` subdirectory
- Add `[McpServerToolType]` attribute to the class
- Add `[McpServerTool(Name = "zemax_tool_name")]` attribute to the method
- Add `[Description("...")]` to the method

### Step 2: Register the Tool in Program.cs (MANDATORY!)
**This is the step that is often forgotten and causes tools to not load!**

Open `src/ZemaxMCP.Server/Program.cs` and add a `.WithTools<>()` line in the appropriate section:

```csharp
// Example: Adding a new Lens Data tool
.WithTools<ZemaxMCP.Server.Tools.LensData.YourNewTool>()
```

The sections in Program.cs are organized by category:
- Lines 51-56: Analysis Tools
- Lines 57-69: Optimization Tools
- Lines 70-81: Lens Data Tools  <-- Add lens data tools here
- Lines 82-89: Configuration Tools
- Lines 90-93: System Tools

### Step 3: Rebuild and Restart
```bash
# Kill any running MCP server first
taskkill /F /IM ZemaxMCP.Server.exe

# Rebuild
dotnet build src\ZemaxMCP.Server\ZemaxMCP.Server.csproj

# The MCP server will auto-restart when Claude Code uses a Zemax tool
```

## Root Cause of "Tools Not Loading" Issue

The MCP server uses **explicit registration** - NOT reflection-based discovery:
- Each tool class MUST be manually registered with `.WithTools<ToolType>()`
- Simply creating a tool file and adding attributes is NOT sufficient
- The tool will compile but won't be available at runtime

## Files Involved

- `src/ZemaxMCP.Server/Program.cs` - Main tool registration (USED)
- `src/ZemaxMCP.Server/Hosting/McpServerExtensions.cs` - Alternative registration (NOT USED)

Note: `McpServerExtensions.cs` exists but is NOT used by Program.cs. Keep Program.cs up to date.

## Verification

After adding a tool, verify it's registered by:
1. Rebuilding the project
2. Restarting Claude Code (or the MCP server)
3. Testing the tool via Claude

---

## Constrained Optimization Tools

Custom MCP-implemented optimization algorithms (NOT built-in Zemax optimizers). The LM algorithm runs entirely in the MCP server, using ZOSAPI only to get/set variable values and evaluate the merit function.

### Tools
- `zemax_get_variables` — Scans LDE, fields, MCE for all Variable solves. Returns variable numbers for use with constraint tools.
- `zemax_set_variable_constraints` — Sets min/max bounds on variables. Constraints are persisted in a `.constraints` sidecar file alongside the .zmx file and auto-loaded on open.
- `zemax_constrained_optimize` — Blocking bound-constrained Levenberg-Marquardt with optional Broyden rank-1 Jacobian updates.
- `zemax_multistart_optimize` — Non-blocking multistart optimizer: randomizes variables within bounds + glass substitution, runs short LM per trial, keeps best. Auto-saves improvements to a `_multistart/` folder.
- `zemax_multistart_status` — Poll progress of a running multistart optimization (trial count, best merit, acceptance count).
- `zemax_multistart_stop` — Cancel a running multistart optimization gracefully.

### Services (ZemaxMCP.Core)
- `ConstraintStore` — Singleton storing variable constraints keyed by CompositeKey. Supports save/load to `.constraints` sidecar files.
- `VariableScanner` — Scans system for Variable solves across surfaces, fields, MCE. Also scans for Material Substitute solves.
- `MeritFunctionReader` — Reads active merit function rows (weight > 0, finite values).
- `ZosVariableAccessor` — Static methods to get/set variable values on IOpticalSystem.
- `LMOptimizer` — Core Levenberg-Marquardt optimizer with bounds clamping and Broyden updates.
- `MultistartOptimizer` — Multistart wrapper: randomizes variables + glasses, runs short LM trials.
- `MultistartState` — Shared state for non-blocking multistart (progress, cancellation, save tracking).

---

## Known Issues & Disabled Tools

### `zemax_optimization_wizard` - DISABLED
**Status:** Do not use until fixed

**Problem:** The optimization wizard tool does not work correctly.

**Alternatives:** Use these tools instead for building merit functions:
- `zemax_forbes_merit_function` - Forbes 1988 Gaussian quadrature OPD-based merit function
- `zemax_add_operand` - Manually add individual operands one at a time
- `zemax_load_merit_function_file` - Load pre-built .MF files

---
*Last updated: 2026-03-21*

---

## XDAT (Extra Data Editor) Access Paths

The `zemax_get_extra_data` / `zemax_set_extra_data` tools try multiple ZOSAPI access paths in priority order. The `AccessPath` field in the tool's response tells you which one worked on your machine:

1. **`ZernikeTypedInterface`** — `surface.SurfaceData as ISurfaceNthZernike` (and the Phase-variant descendant `ISurfaceZernikeStandardPhase`). Exposes cells with semantic numbering that matches the Zemax UI's Extra Data column. This is the preferred path for every Zernike-family surface.
2. **`SurfaceColumnExtraData`** — `surface.GetSurfaceCell(SurfaceColumn.ExtraData0 + N)` (requires the enum value to exist).
3. **`GetCellAtAbsolute`** — `surface.GetCellAt(absoluteColumnIndex)` where absolute index = base offset + N (base is one of 14, 26, 28, 30).
4. **`ExtraDataGetCell`** — `surface.ExtraData.GetCell(N)` or similar property path.

**Empirical behavior on this installation (ZOSAPI 2023 R1.00, smoke-tested 2026-04-23):**
- For Zernike-family surfaces (ZernikeStandardPhase, ZernikeFringePhase, ZernikeAnnularPhase, and the three Sag variants), the `ZernikeTypedInterface` path succeeds and gives clean access to every cell including `Extrapolate` and `DiffractOrder` (which are inaccessible via `GetCellAtAbsolute`).
- For other surface types, only `GetCellAtAbsolute` with base offset **14** works; paths 2 and 4 both return null.

If every path fails, the tool returns a clear error with surface type info. Report such cases so a new path can be added.

### Cell Number Semantics (ZernikeTypedInterface path)

When the tool reports `AccessPath: ZernikeTypedInterface`, `cellNumber` maps directly to the Zemax UI's Extra Data rows. The mapping depends on whether the surface is a **Phase** variant (ZernikeStandardPhase, ZernikeFringePhase, ZernikeAnnularPhase) or a **Sag** variant (ZernikeStandardSag, ZernikeFringeSag, ZernikeAnnularSag).

**Phase variants** (have `Extrapolate` and `DiffractOrder`):

| Tool `cellNumber` | Zemax UI meaning |
|-------------------|-------------------|
| 1                 | `Extrapolate` (Int32; 0 = off, 1 = on) |
| 2                 | `DiffractOrder` (Double) |
| 3                 | `NumberOfTerms` / `MaxTerm` (Int32; **set this FIRST to unlock coefficient cells**) |
| 4                 | `NormRadius` (Double, lens units) |
| 5                 | `Z1` (first Zernike coefficient) |
| 5 + (n - 1)       | `Zn` |
| 4 + MaxTerm       | `Z<MaxTerm>` (last accessible coefficient) |

**Sag variants** (no `Extrapolate`/`DiffractOrder`):

| Tool `cellNumber` | Zemax UI meaning |
|-------------------|-------------------|
| 1                 | `NumberOfTerms` / `MaxTerm` |
| 2                 | `NormRadius` |
| 3                 | `Z1` |
| 3 + (n - 1)       | `Zn` |

**Example — ZernikeStandardPhase, set a phase with Z4 = 0.1, Z5 = -0.05, Z11 = 0.08:**
```
1. zemax_set_extra_data surfaceNumber=<N> cell=3 value=37          (enable 37-term Zernike)
2. zemax_set_extra_data surfaceNumber=<N> cell=4 value=100         (norm radius in mm)
3. zemax_set_extra_data surfaceNumber=<N> batchSet='8:0.1,9:-0.05,15:0.08'
   (Z4 -> cell 5+3=8, Z5 -> cell 9, Z11 -> cell 15)
4. zemax_set_extra_data surfaceNumber=<N> variableCells='8,9,15'   (mark as optimization variables)
```

If the access path is `GetCellAtAbsolute` (non-Zernike surface), `cellNumber` follows absolute column indexing with base offset 14 — see the old-offset mapping that was the only option before the typed-interface path was added: cells 1-9 are inaccessible, cell 10 is `MaxTerm`, cell 11 is `NormRadius`, cell 12+n maps to Zn. Zernike surfaces should no longer hit this code path; if you see it happen, report the case.
