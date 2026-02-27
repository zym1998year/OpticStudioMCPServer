using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZemaxMCP.Server.Tools.Optimization;

namespace ZemaxMCP.Server.Resources;

[McpServerResourceType]
public class MeritFunctionResource
{
    private readonly IZemaxSession _session;
    private readonly ILogger<GetMeritFunctionTool> _logger;

    public MeritFunctionResource(IZemaxSession session, ILogger<GetMeritFunctionTool> logger)
    {
        _session = session;
        _logger = logger;
    }

    [McpServerResource(Name = "zemax://merit-function")]
    [Description("Current merit function with all operands and their values")]
    public async Task<string> GetAsync()
    {
        if (!_session.IsConnected)
        {
            return JsonSerializer.Serialize(new { error = "Not connected to OpticStudio" });
        }

        var tool = new GetMeritFunctionTool(_session, _logger);
        var mf = await tool.ExecuteAsync(true, 0, 0);

        return JsonSerializer.Serialize(mf, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
