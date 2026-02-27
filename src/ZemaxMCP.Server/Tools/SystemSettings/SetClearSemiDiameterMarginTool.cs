using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.SystemSettings;

[McpServerToolType]
public class SetClearSemiDiameterMarginTool
{
    private readonly IZemaxSession _session;

    public SetClearSemiDiameterMarginTool(IZemaxSession session) => _session = session;

    public record SetClearSemiDiameterMarginResult(
        bool Success,
        string? Error,
        double MarginMillimeters,
        double MarginPercent
    );

    [McpServerTool(Name = "zemax_set_clear_semi_diameter_margin")]
    [Description("Set the clear semi-diameter margin (mm and/or %)")]
    public Task<SetClearSemiDiameterMarginResult> ExecuteAsync(
        [Description("Margin in millimeters (lens units)")] double? marginMillimeters = null,
        [Description("Margin in percent")] double? marginPercent = null)
    {
        // Note: This feature is not available in OpticStudio 2022 R2 ZOSAPI
        return Task.FromResult(new SetClearSemiDiameterMarginResult(
            Success: false,
            Error: "Clear semi-diameter margin settings are not accessible via ZOSAPI in this version of OpticStudio",
            MarginMillimeters: 0,
            MarginPercent: 0
        ));
    }
}
