using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.SystemSettings;

[McpServerToolType]
public class GetApodizationTool
{
    private readonly IZemaxSession _session;

    public GetApodizationTool(IZemaxSession session) => _session = session;

    public record GetApodizationResult(
        bool Success,
        string? Error,
        string ApodizationType,
        double ApodizationFactor
    );

    [McpServerTool(Name = "zemax_get_apodization")]
    [Description("Get the apodization type and factor settings")]
    public async Task<GetApodizationResult> ExecuteAsync()
    {
        try
        {
            var result = await _session.ExecuteAsync("GetApodization", null, system =>
            {
                var aperture = system.SystemData.Aperture;
                return new GetApodizationResult(
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
            return new GetApodizationResult(false, ex.Message, "", 0);
        }
    }
}
