using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Configuration;

[McpServerToolType]
public class SetCurrentConfigurationTool
{
    private readonly IZemaxSession _session;

    public SetCurrentConfigurationTool(IZemaxSession session) => _session = session;

    public record SetCurrentConfigurationResult(
        bool Success,
        string? Error,
        int CurrentConfiguration,
        int NumberOfConfigurations
    );

    [McpServerTool(Name = "zemax_set_current_configuration")]
    [Description("Set the current active configuration")]
    public async Task<SetCurrentConfigurationResult> ExecuteAsync(
        [Description("Configuration number to set as current (1-indexed)")] int configurationNumber)
    {
        if (configurationNumber < 1)
        {
            return new SetCurrentConfigurationResult(false, "Configuration number must be at least 1", 0, 0);
        }

        try
        {
            var result = await _session.ExecuteAsync("SetCurrentConfiguration",
                new Dictionary<string, object?> { ["configurationNumber"] = configurationNumber },
                system =>
            {
                var mce = system.MCE;

                if (configurationNumber > mce.NumberOfConfigurations)
                {
                    return new SetCurrentConfigurationResult(
                        Success: false,
                        Error: $"Configuration {configurationNumber} does not exist. System has {mce.NumberOfConfigurations} configurations.",
                        CurrentConfiguration: mce.CurrentConfiguration,
                        NumberOfConfigurations: mce.NumberOfConfigurations
                    );
                }

                mce.SetCurrentConfiguration(configurationNumber);

                return new SetCurrentConfigurationResult(
                    Success: true,
                    Error: null,
                    CurrentConfiguration: mce.CurrentConfiguration,
                    NumberOfConfigurations: mce.NumberOfConfigurations
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new SetCurrentConfigurationResult(false, ex.Message, 0, 0);
        }
    }
}
