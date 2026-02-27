using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.SystemSettings;

[McpServerToolType]
public class SetMtfUnitsTool
{
    private readonly IZemaxSession _session;

    public SetMtfUnitsTool(IZemaxSession session) => _session = session;

    public record SetMtfUnitsResult(
        bool Success,
        string? Error,
        string MtfUnits
    );

    [McpServerTool(Name = "zemax_set_mtf_units")]
    [Description("Set the MTF units for the system (cycles/mm or cycles/mr)")]
    public async Task<SetMtfUnitsResult> ExecuteAsync(
        [Description("MTF units: CyclesPerMillimeter or CyclesPerMilliradian")] string mtfUnits)
    {
        try
        {
            var result = await _session.ExecuteAsync("SetMtfUnits",
                new Dictionary<string, object?> { ["mtfUnits"] = mtfUnits },
                system =>
            {
                var unitType = mtfUnits.ToUpper() switch
                {
                    "CYCLESPERMILLIMETER" or "CYCLES/MM" or "CY/MM" =>
                        ZOSAPI.SystemData.ZemaxMTFUnits.CyclesPerMillimeter,
                    "CYCLESPERMILLIRADIAN" or "CYCLES/MR" or "CY/MR" =>
                        ZOSAPI.SystemData.ZemaxMTFUnits.CyclesPerMilliradian,
                    _ => throw new ArgumentException(
                        $"Invalid MTF units: {mtfUnits}. Valid values: CyclesPerMillimeter, CyclesPerMilliradian, cycles/mm, cycles/mr")
                };

                system.SystemData.Units.MTFUnits = unitType;

                return new SetMtfUnitsResult(
                    Success: true,
                    Error: null,
                    MtfUnits: system.SystemData.Units.MTFUnits.ToString()
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new SetMtfUnitsResult(false, ex.Message, mtfUnits);
        }
    }
}
