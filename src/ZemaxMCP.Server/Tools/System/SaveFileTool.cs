using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.System;

[McpServerToolType]
public class SaveFileTool
{
    private readonly IZemaxSession _session;
    private readonly ConstraintStore _constraintStore;

    public SaveFileTool(IZemaxSession session, ConstraintStore constraintStore)
    {
        _session = session;
        _constraintStore = constraintStore;
    }

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

            var savedPath = filePath ?? _session.CurrentFilePath;

            // Save constraints sidecar alongside the Zemax file
            if (!string.IsNullOrEmpty(savedPath))
                _constraintStore.SaveToFile(savedPath);

            return new SaveFileResult(
                Success: true,
                Error: null,
                FilePath: savedPath
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
