using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.SystemSettings;

[McpServerToolType]
public class GetClearSemiDiameterMarginTool
{
    private readonly IZemaxSession _session;

    public GetClearSemiDiameterMarginTool(IZemaxSession session) => _session = session;

    public record GetClearSemiDiameterMarginResult(
        bool Success,
        string? Error,
        double MarginMillimeters,
        double MarginPercent
    );

    [McpServerTool(Name = "zemax_get_clear_semi_diameter_margin")]
    [Description("Get the clear semi-diameter margin settings (both mm and %)")]
    public Task<GetClearSemiDiameterMarginResult> ExecuteAsync()
    {
        // Note: This feature is not available in OpticStudio 2022 R2 ZOSAPI
        return Task.FromResult(new GetClearSemiDiameterMarginResult(
            Success: false,
            Error: "Clear semi-diameter margin settings are not accessible via ZOSAPI in this version of OpticStudio",
            MarginMillimeters: 0,
            MarginPercent: 0
        ));
    }
}
