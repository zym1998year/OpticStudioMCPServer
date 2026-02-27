using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.SystemSettings;

[McpServerToolType]
public class SetAfocalModeTool
{
    private readonly IZemaxSession _session;

    public SetAfocalModeTool(IZemaxSession session) => _session = session;

    public record SetAfocalModeResult(
        bool Success,
        string? Error,
        bool AfocalMode
    );

    [McpServerTool(Name = "zemax_set_afocal_mode")]
    [Description("Set the afocal mode setting")]
    public async Task<SetAfocalModeResult> ExecuteAsync(
        [Description("Enable or disable afocal mode")] bool afocalMode)
    {
        try
        {
            var result = await _session.ExecuteAsync("SetAfocalMode",
                new Dictionary<string, object?> { ["afocalMode"] = afocalMode },
                system =>
            {
                system.SystemData.Aperture.AFocalImageSpace = afocalMode;

                return new SetAfocalModeResult(
                    Success: true,
                    Error: null,
                    AfocalMode: system.SystemData.Aperture.AFocalImageSpace
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new SetAfocalModeResult(false, ex.Message, false);
        }
    }
}
