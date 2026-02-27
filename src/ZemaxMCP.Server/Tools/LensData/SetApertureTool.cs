using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetApertureTool
{
    private readonly IZemaxSession _session;

    public SetApertureTool(IZemaxSession session) => _session = session;

    public record SetApertureResult(
        bool Success,
        string? Error,
        string ApertureType,
        double ApertureValue
    );

    [McpServerTool(Name = "zemax_set_aperture")]
    [Description("Set the system aperture")]
    public async Task<SetApertureResult> ExecuteAsync(
        [Description("Aperture value (diameter, F/#, NA, etc.)")] double value,
        [Description("Aperture type: EPD, FNumber, ObjectNA, FloatByStop")] string apertureType = "EPD")
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["value"] = value,
                ["apertureType"] = apertureType
            };

            var result = await _session.ExecuteAsync("SetAperture", parameters, system =>
            {
                var aperture = system.SystemData.Aperture;

                var apType = apertureType.ToUpper() switch
                {
                    "EPD" => ZOSAPI.SystemData.ZemaxApertureType.EntrancePupilDiameter,
                    "FNUMBER" => ZOSAPI.SystemData.ZemaxApertureType.ImageSpaceFNum,
                    "OBJECTNA" => ZOSAPI.SystemData.ZemaxApertureType.ObjectSpaceNA,
                    "FLOATBYSTOP" => ZOSAPI.SystemData.ZemaxApertureType.FloatByStopSize,
                    _ => ZOSAPI.SystemData.ZemaxApertureType.EntrancePupilDiameter
                };

                aperture.ApertureType = apType;
                aperture.ApertureValue = value;

                return new SetApertureResult(
                    Success: true,
                    Error: null,
                    ApertureType: aperture.ApertureType.ToString(),
                    ApertureValue: aperture.ApertureValue
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SetApertureResult(false, ex.Message, apertureType, value);
        }
    }
}
