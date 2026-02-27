using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.System;

[McpServerToolType]
public class SaveFileTool
{
    private readonly IZemaxSession _session;

    public SaveFileTool(IZemaxSession session) => _session = session;

    public record SaveFileResult(
        bool Success,
        string? Error,
        string? FilePath
    );

    [McpServerTool(Name = "zemax_save_file")]
    [Description("Save the current lens system to file")]
    public async Task<SaveFileResult> ExecuteAsync(
        [Description("File path (optional, uses current file if not specified)")] string? filePath = null)
    {
        try
        {
            await _session.SaveFileAsync(filePath);

            return new SaveFileResult(
                Success: true,
                Error: null,
                FilePath: filePath ?? _session.CurrentFilePath
            );
        }
        catch (Exception ex)
        {
            return new SaveFileResult(
                Success: false,
                Error: ex.Message,
                FilePath: null
            );
        }
    }
}
