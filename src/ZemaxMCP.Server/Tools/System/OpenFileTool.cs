using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.System;

[McpServerToolType]
public class OpenFileTool
{
    private readonly IZemaxSession _session;
    private readonly ConstraintStore _constraintStore;

    public OpenFileTool(IZemaxSession session, ConstraintStore constraintStore)
    {
        _session = session;
        _constraintStore = constraintStore;
    }

    public record OpenFileResult(
        bool Success,
        string? Error,
        string? FilePath,
        int NumberOfSurfaces,
        string? Title,
        int ConstraintsLoaded
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
                    Title: null,
                    ConstraintsLoaded: 0
                );
            }

            await _session.OpenFileAsync(filePath);

            var result = await _session.ExecuteAsync("OpenFile",
                new Dictionary<string, object?> { ["filePath"] = filePath },
                system =>
            {
                // Load constraints from sidecar file if it exists
                _constraintStore.Clear();
                var systemFile = system.SystemFile;
                int constraintsLoaded = 0;
                if (!string.IsNullOrEmpty(systemFile))
                    constraintsLoaded = _constraintStore.LoadFromFile(systemFile);

                return new OpenFileResult(
                    Success: true,
                    Error: null,
                    FilePath: systemFile,
                    NumberOfSurfaces: system.LDE.NumberOfSurfaces,
                    Title: Path.GetFileNameWithoutExtension(systemFile),
                    ConstraintsLoaded: constraintsLoaded
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
                Title: null,
                ConstraintsLoaded: 0
            );
        }
    }
}
