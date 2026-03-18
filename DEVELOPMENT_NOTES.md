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
- `zemax_set_variable_constraints` — Sets min/max bounds on variables (stored in-memory for the session).
- `zemax_constrained_optimize` — Bound-constrained Levenberg-Marquardt with optional Broyden rank-1 Jacobian updates.
- `zemax_multistart_optimize` — Multistart optimizer: randomizes variables within bounds + glass substitution, runs short LM per trial, keeps best.

### Services (ZemaxMCP.Core)
- `ConstraintStore` — In-memory singleton storing variable constraints keyed by CompositeKey.
- `VariableScanner` — Scans system for Variable solves across surfaces, fields, MCE.
- `MeritFunctionReader` — Reads active merit function rows (weight > 0, finite values).
- `ZosVariableAccessor` — Static methods to get/set variable values on IOpticalSystem.
- `LMOptimizer` — Core Levenberg-Marquardt optimizer with bounds clamping and Broyden updates.
- `MultistartOptimizer` — Multistart wrapper: randomizes variables + glasses, runs short LM trials.

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
*Last updated: 2025-12-25*
