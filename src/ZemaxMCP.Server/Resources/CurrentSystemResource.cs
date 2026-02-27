using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZemaxMCP.Server.Tools.LensData;

namespace ZemaxMCP.Server.Resources;

[McpServerResourceType]
public class CurrentSystemResource
{
    private readonly IZemaxSession _session;

    public CurrentSystemResource(IZemaxSession session) => _session = session;

    [McpServerResource(Name = "zemax://system")]
    [Description("Current optical system state including all surfaces, fields, and wavelengths")]
    public async Task<string> GetAsync()
    {
        if (!_session.IsConnected)
        {
            return JsonSerializer.Serialize(new { error = "Not connected to OpticStudio" });
        }

        var tool = new GetSystemDataTool(_session);
        var system = await tool.ExecuteAsync(true, true, true);

        return JsonSerializer.Serialize(system, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
