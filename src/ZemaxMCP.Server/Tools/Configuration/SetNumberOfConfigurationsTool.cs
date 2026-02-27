using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Configuration;

[McpServerToolType]
public class SetNumberOfConfigurationsTool
{
    private readonly IZemaxSession _session;

    public SetNumberOfConfigurationsTool(IZemaxSession session) => _session = session;

    public record SetNumberOfConfigurationsResult(
        bool Success,
        string? Error,
        int NumberOfConfigurations
    );

    [McpServerTool(Name = "zemax_set_number_of_configurations")]
    [Description("Set the number of configurations in the multi-configuration editor")]
    public async Task<SetNumberOfConfigurationsResult> ExecuteAsync(
        [Description("Number of configurations to set")] int numberOfConfigurations)
    {
        if (numberOfConfigurations < 1)
        {
            return new SetNumberOfConfigurationsResult(false, "Number of configurations must be at least 1", 0);
        }

        try
        {
            var result = await _session.ExecuteAsync("SetNumberOfConfigurations",
                new Dictionary<string, object?> { ["numberOfConfigurations"] = numberOfConfigurations },
                system =>
            {
                var mce = system.MCE;
                int currentCount = mce.NumberOfConfigurations;

                if (numberOfConfigurations > currentCount)
                {
                    // Add configurations
                    for (int i = currentCount; i < numberOfConfigurations; i++)
                    {
                        mce.AddConfiguration(false);
                    }
                }
                else if (numberOfConfigurations < currentCount)
                {
                    // Remove configurations from the end
                    for (int i = currentCount; i > numberOfConfigurations; i--)
                    {
                        mce.DeleteConfiguration(i);
                    }
                }

                return new SetNumberOfConfigurationsResult(
                    Success: true,
                    Error: null,
                    NumberOfConfigurations: mce.NumberOfConfigurations
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new SetNumberOfConfigurationsResult(false, ex.Message, 0);
        }
    }
}
