using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetNumberOfWavelengthsTool
{
    private readonly IZemaxSession _session;

    public SetNumberOfWavelengthsTool(IZemaxSession session) => _session = session;

    public record SetNumberOfWavelengthsResult(
        bool Success,
        string? Error,
        int NumberOfWavelengths
    );

    [McpServerTool(Name = "zemax_set_number_of_wavelengths")]
    [Description("Set the number of wavelengths in the system")]
    public async Task<SetNumberOfWavelengthsResult> ExecuteAsync(
        [Description("Number of wavelengths to set (1-24)")] int numberOfWavelengths)
    {
        try
        {
            if (numberOfWavelengths < 1 || numberOfWavelengths > 24)
            {
                return new SetNumberOfWavelengthsResult(
                    false,
                    "Number of wavelengths must be between 1 and 24",
                    0);
            }

            var result = await _session.ExecuteAsync("SetNumberOfWavelengths",
                new Dictionary<string, object?> { ["numberOfWavelengths"] = numberOfWavelengths },
                system =>
            {
                var sysWaves = system.SystemData.Wavelengths;
                var currentCount = sysWaves.NumberOfWavelengths;

                // Add wavelengths if needed
                while (sysWaves.NumberOfWavelengths < numberOfWavelengths)
                {
                    sysWaves.AddWavelength(0.55, 1.0); // Default to 550nm with weight 1.0
                }

                // Remove wavelengths if needed
                while (sysWaves.NumberOfWavelengths > numberOfWavelengths)
                {
                    sysWaves.RemoveWavelength(sysWaves.NumberOfWavelengths);
                }

                return new SetNumberOfWavelengthsResult(
                    Success: true,
                    Error: null,
                    NumberOfWavelengths: sysWaves.NumberOfWavelengths
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SetNumberOfWavelengthsResult(false, ex.Message, 0);
        }
    }
}
