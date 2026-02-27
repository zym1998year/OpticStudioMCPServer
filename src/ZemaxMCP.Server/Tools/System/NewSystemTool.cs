using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.System;

[McpServerToolType]
public class NewSystemTool
{
    private readonly IZemaxSession _session;

    public NewSystemTool(IZemaxSession session) => _session = session;

    public record NewSystemResult(
        bool Success,
        string? Error,
        int NumberOfSurfaces
    );

    [McpServerTool(Name = "zemax_new_system")]
    [Description("Create a new blank optical system")]
    public async Task<NewSystemResult> ExecuteAsync()
    {
        try
        {
            await _session.NewSystemAsync();

            var result = await _session.ExecuteAsync("NewSystem", null, system =>
            {
                return new NewSystemResult(
                    Success: true,
                    Error: null,
                    NumberOfSurfaces: system.LDE.NumberOfSurfaces
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new NewSystemResult(
                Success: false,
                Error: ex.Message,
                NumberOfSurfaces: 0
            );
        }
    }
}
