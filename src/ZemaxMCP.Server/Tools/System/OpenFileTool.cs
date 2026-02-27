using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.System;

[McpServerToolType]
public class OpenFileTool
{
    private readonly IZemaxSession _session;

    public OpenFileTool(IZemaxSession session) => _session = session;

    public record OpenFileResult(
        bool Success,
        string? Error,
        string? FilePath,
        int NumberOfSurfaces,
        string? Title
    );

    [McpServerTool(Name = "zemax_open_file")]
    [Description("Open a Zemax lens file (.zmx or .zos)")]
    public async Task<OpenFileResult> ExecuteAsync(
        [Description("Full path to the lens file")] string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new OpenFileResult(
                    Success: false,
                    Error: $"File not found: {filePath}",
                    FilePath: null,
                    NumberOfSurfaces: 0,
                    Title: null
                );
            }

            await _session.OpenFileAsync(filePath);

            var result = await _session.ExecuteAsync("OpenFile",
                new Dictionary<string, object?> { ["filePath"] = filePath },
                system =>
            {
                return new OpenFileResult(
                    Success: true,
                    Error: null,
                    FilePath: system.SystemFile,
                    NumberOfSurfaces: system.LDE.NumberOfSurfaces,
                    Title: Path.GetFileNameWithoutExtension(system.SystemFile)
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new OpenFileResult(
                Success: false,
                Error: ex.Message,
                FilePath: null,
                NumberOfSurfaces: 0,
                Title: null
            );
        }
    }
}
