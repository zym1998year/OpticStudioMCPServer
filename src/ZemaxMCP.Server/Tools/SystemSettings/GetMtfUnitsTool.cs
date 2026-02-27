using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.SystemSettings;

[McpServerToolType]
public class GetMtfUnitsTool
{
    private readonly IZemaxSession _session;

    public GetMtfUnitsTool(IZemaxSession session) => _session = session;

    public record GetMtfUnitsResult(
        bool Success,
        string? Error,
        string MtfUnits
    );

    [McpServerTool(Name = "zemax_get_mtf_units")]
    [Description("Get the current MTF units setting")]
    public async Task<GetMtfUnitsResult> ExecuteAsync()
    {
        try
        {
            var result = await _session.ExecuteAsync("GetMtfUnits", null, system =>
            {
                return new GetMtfUnitsResult(
                    Success: true,
                    Error: null,
                    MtfUnits: system.SystemData.Units.MTFUnits.ToString()
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new GetMtfUnitsResult(false, ex.Message, "Unknown");
        }
    }
}
