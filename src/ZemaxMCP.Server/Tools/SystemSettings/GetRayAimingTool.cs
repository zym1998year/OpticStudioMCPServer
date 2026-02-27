using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.SystemSettings;

[McpServerToolType]
public class GetRayAimingTool
{
    private readonly IZemaxSession _session;

    public GetRayAimingTool(IZemaxSession session) => _session = session;

    public record GetRayAimingResult(
        bool Success,
        string? Error,
        string RayAiming
    );

    [McpServerTool(Name = "zemax_get_ray_aiming")]
    [Description("Get the ray aiming setting")]
    public async Task<GetRayAimingResult> ExecuteAsync()
    {
        try
        {
            var result = await _session.ExecuteAsync("GetRayAiming", null, system =>
            {
                var rayAiming = system.SystemData.RayAiming;
                return new GetRayAimingResult(
                    Success: true,
                    Error: null,
                    RayAiming: rayAiming.RayAiming.ToString()
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new GetRayAimingResult(false, ex.Message, "");
        }
    }
}
