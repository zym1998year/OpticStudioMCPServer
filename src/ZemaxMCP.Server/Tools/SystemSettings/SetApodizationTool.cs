using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.SystemSettings;

[McpServerToolType]
public class SetApodizationTool
{
    private readonly IZemaxSession _session;

    public SetApodizationTool(IZemaxSession session) => _session = session;

    public record SetApodizationResult(
        bool Success,
        string? Error,
        string ApodizationType,
        double ApodizationFactor
    );

    [McpServerTool(Name = "zemax_set_apodization")]
    [Description("Set the apodization type and/or factor")]
    public async Task<SetApodizationResult> ExecuteAsync(
        [Description("Apodization type: Uniform, Gaussian, CosineCubed")] string? apodizationType = null,
        [Description("Apodization factor")] double? apodizationFactor = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["apodizationType"] = apodizationType,
                ["apodizationFactor"] = apodizationFactor
            };

            var result = await _session.ExecuteAsync("SetApodization", parameters, system =>
            {
                var aperture = system.SystemData.Aperture;

                if (apodizationType != null)
                {
                    var apoType = apodizationType.ToUpper() switch
                    {
                        "UNIFORM" => ZOSAPI.SystemData.ZemaxApodizationType.Uniform,
                        "GAUSSIAN" => ZOSAPI.SystemData.ZemaxApodizationType.Gaussian,
                        "COSINECUBED" => ZOSAPI.SystemData.ZemaxApodizationType.CosineCubed,
                        _ => throw new ArgumentException($"Invalid apodization type: {apodizationType}. Valid values: Uniform, Gaussian, CosineCubed")
                    };
                    aperture.ApodizationType = apoType;
                }

                if (apodizationFactor.HasValue)
                {
                    aperture.ApodizationFactor = apodizationFactor.Value;
                }

                return new SetApodizationResult(
                    Success: true,
                    Error: null,
                    ApodizationType: aperture.ApodizationType.ToString(),
                    ApodizationFactor: aperture.ApodizationFactor
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new SetApodizationResult(false, ex.Message, apodizationType ?? "", apodizationFactor ?? 0);
        }
    }
}
