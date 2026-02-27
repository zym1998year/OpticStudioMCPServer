using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class LoadMeritFunctionFileTool
{
    private readonly IZemaxSession _session;

    public LoadMeritFunctionFileTool(IZemaxSession session) => _session = session;

    public record LoadMeritFunctionFileResult(
        bool Success,
        string? Error,
        string? FilePath,
        int NumberOfOperands,
        double? InitialMerit);

    [McpServerTool(Name = "zemax_load_merit_function_file")]
    [Description("Load a merit function from a file (.MF format)")]
    public async Task<LoadMeritFunctionFileResult> ExecuteAsync(
        [Description("Full path to the merit function file to load")] string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new LoadMeritFunctionFileResult(
                    Success: false,
                    Error: "File path is required",
                    FilePath: null,
                    NumberOfOperands: 0,
                    InitialMerit: null);
            }

            if (!File.Exists(filePath))
            {
                return new LoadMeritFunctionFileResult(
                    Success: false,
                    Error: $"File not found: {filePath}",
                    FilePath: null,
                    NumberOfOperands: 0,
                    InitialMerit: null);
            }

            return await _session.ExecuteAsync("LoadMeritFunctionFile",
                new Dictionary<string, object?> { ["filePath"] = filePath },
                system =>
            {
                var mfe = system.MFE;

                mfe.LoadMeritFunction(filePath);

                var numberOfOperands = mfe.NumberOfOperands;
                var initialMerit = mfe.CalculateMeritFunction();

                return new LoadMeritFunctionFileResult(
                    Success: true,
                    Error: null,
                    FilePath: filePath,
                    NumberOfOperands: numberOfOperands,
                    InitialMerit: initialMerit);
            });
        }
        catch (Exception ex)
        {
            return new LoadMeritFunctionFileResult(
                Success: false,
                Error: ex.Message,
                FilePath: null,
                NumberOfOperands: 0,
                InitialMerit: null);
        }
    }
}
