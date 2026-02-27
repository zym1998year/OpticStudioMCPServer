using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.SystemSettings;

[McpServerToolType]
public class SetRayAimingTool
{
    private readonly IZemaxSession _session;

    public SetRayAimingTool(IZemaxSession session) => _session = session;

    public record SetRayAimingResult(
        bool Success,
        string? Error,
        string RayAiming
    );

    [McpServerTool(Name = "zemax_set_ray_aiming")]
    [Description("Set the ray aiming setting")]
    public async Task<SetRayAimingResult> ExecuteAsync(
        [Description("Ray aiming type: Off, Paraxial, Real")] string rayAiming)
    {
        try
        {
            var result = await _session.ExecuteAsync("SetRayAiming",
                new Dictionary<string, object?> { ["rayAiming"] = rayAiming },
                system =>
            {
                var rayAimingSettings = system.SystemData.RayAiming;

                var aimType = rayAiming.ToUpper() switch
                {
                    "OFF" => ZOSAPI.SystemData.RayAimingMethod.Off,
                    "PARAXIAL" => ZOSAPI.SystemData.RayAimingMethod.Paraxial,
                    "REAL" => ZOSAPI.SystemData.RayAimingMethod.Real,
                    _ => throw new ArgumentException($"Invalid ray aiming type: {rayAiming}. Valid values: Off, Paraxial, Real")
                };

                rayAimingSettings.RayAiming = aimType;

                return new SetRayAimingResult(
                    Success: true,
                    Error: null,
                    RayAiming: rayAimingSettings.RayAiming.ToString()
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new SetRayAimingResult(false, ex.Message, rayAiming);
        }
    }
}
