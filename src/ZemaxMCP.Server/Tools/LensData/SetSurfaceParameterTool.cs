using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetSurfaceParameterTool
{
    private readonly IZemaxSession _session;

    public SetSurfaceParameterTool(IZemaxSession session) => _session = session;

    public record ParameterEntry(int Number, double Value);

    public record SetParameterResult(
        bool Success,
        string? Error = null,
        int SurfaceNumber = 0,
        string? SurfaceType = null,
        ParameterEntry[]? Parameters = null);

    [McpServerTool(Name = "zemax_set_surface_parameter")]
    [Description(
        "Get or set surface-type-specific parameters (PARM values). "
        + "For CoordinateBreak: PARM 1=Decenter X (mm), 2=Decenter Y (mm), "
        + "3=Tilt About X (deg), 4=Tilt About Y (deg), 5=Tilt About Z (deg), "
        + "6=Order (0=decenter-then-tilt, 1=tilt-then-decenter). "
        + "Read mode: omit value and batchSet to return current values. "
        + "Single set: provide parameterNumber + value. "
        + "Batch set: use batchSet string like '3:0.2,4:0.1,6:1'.")]
    public async Task<SetParameterResult> ExecuteAsync(
        [Description("Surface number")] int surfaceNumber,
        [Description("Parameter number (1-indexed). 0 to return all parameters.")] int parameterNumber = 0,
        [Description("Value to set (omit to read only)")] double? value = null,
        [Description("Make this parameter variable for optimization")] bool? makeVariable = null,
        [Description("Batch set: comma-separated 'num:value' pairs, e.g. '3:0.2,4:0.1,6:1'")] string? batchSet = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber,
                ["parameterNumber"] = parameterNumber,
                ["value"] = value,
                ["batchSet"] = batchSet
            };

            return await _session.ExecuteAsync("SetSurfaceParameter", parameters, system =>
            {
                var lde = system.LDE;

                if (surfaceNumber < 0 || surfaceNumber >= lde.NumberOfSurfaces)
                    throw new ArgumentException(
                        $"Invalid surface number: {surfaceNumber}. Valid range: 0-{lde.NumberOfSurfaces - 1}");

                var surface = lde.GetSurfaceAt(surfaceNumber);
                string surfType = surface.Type.ToString();

                // Batch set mode
                if (!string.IsNullOrEmpty(batchSet))
                {
                    var entries = new List<ParameterEntry>();
                    foreach (var pair in batchSet.Split(','))
                    {
                        var parts = pair.Trim().Split(':');
                        if (parts.Length != 2)
                            return new SetParameterResult(false,
                                Error: $"Invalid batch format: '{pair}'. Expected 'num:value'.");

                        if (!int.TryParse(parts[0].Trim(), out int pNum) ||
                            !double.TryParse(parts[1].Trim(),
                                global::System.Globalization.NumberStyles.Float,
                                global::System.Globalization.CultureInfo.InvariantCulture,
                                out double pVal))
                            return new SetParameterResult(false,
                                Error: $"Cannot parse: '{pair}'.");

                        var bCell = surface.GetSurfaceCell(SurfaceColumn.Par0 + pNum);
                        try { bCell.DoubleValue = pVal; }
                        catch { bCell.IntegerValue = (int)pVal; }
                        double readback;
                        try { readback = bCell.DoubleValue; }
                        catch { readback = bCell.IntegerValue; }
                        entries.Add(new ParameterEntry(pNum, readback));
                    }

                    return new SetParameterResult(
                        Success: true,
                        SurfaceNumber: surfaceNumber,
                        SurfaceType: surfType,
                        Parameters: entries.ToArray());
                }

                // Single set mode
                if (parameterNumber > 0 && value.HasValue)
                {
                    var cell = surface.GetSurfaceCell(SurfaceColumn.Par0 + parameterNumber);
                    try { cell.DoubleValue = value.Value; }
                    catch { cell.IntegerValue = (int)value.Value; }

                    if (makeVariable.HasValue && makeVariable.Value)
                        cell.MakeSolveVariable();

                    double readback;
                    try { readback = cell.DoubleValue; }
                    catch { readback = cell.IntegerValue; }
                    return new SetParameterResult(
                        Success: true,
                        SurfaceNumber: surfaceNumber,
                        SurfaceType: surfType,
                        Parameters: new[] { new ParameterEntry(parameterNumber, readback) });
                }

                // Validate parameterNumber range for read mode
                if (parameterNumber < 0 || parameterNumber > 20)
                    return new SetParameterResult(false,
                        Error: $"Invalid parameter number: {parameterNumber}. Valid range: 1-20 (0 for all).");

                // Read mode
                var readEntries = new List<ParameterEntry>();
                int consecutiveFailures = 0;
                for (int p = 1; p <= 20; p++)
                {
                    try
                    {
                        var cell = surface.GetSurfaceCell(SurfaceColumn.Par0 + p);
                        if (cell == null) { consecutiveFailures++; continue; }
                        double v;
                        try { v = cell.DoubleValue; }
                        catch { v = cell.IntegerValue; } // Integer cells (e.g. CoordinateBreak Order)
                        consecutiveFailures = 0;
                        if (parameterNumber == 0 || parameterNumber == p)
                            readEntries.Add(new ParameterEntry(p, v));
                    }
                    catch
                    {
                        consecutiveFailures++;
                        if (consecutiveFailures >= 3) break;
                    }
                }

                return new SetParameterResult(
                    Success: true,
                    SurfaceNumber: surfaceNumber,
                    SurfaceType: surfType,
                    Parameters: readEntries.ToArray());
            });
        }
        catch (Exception ex)
        {
            return new SetParameterResult(false, Error: ex.Message);
        }
    }
}
