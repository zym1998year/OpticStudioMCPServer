using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class SaveMeritFunctionFileTool
{
    private readonly IZemaxSession _session;

    public SaveMeritFunctionFileTool(IZemaxSession session) => _session = session;

    public record SaveMeritFunctionFileResult(
        bool Success,
        string? Error,
        string? FilePath,
        int NumberOfOperands);

    [McpServerTool(Name = "zemax_save_merit_function_file")]
    [Description("Save the current merit function to a file (.MF format)")]
    public async Task<SaveMeritFunctionFileResult> ExecuteAsync(
        [Description("Full path to save the merit function file (.MF extension)")] string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new SaveMeritFunctionFileResult(
                    Success: false,
                    Error: "File path is required",
                    FilePath: null,
                    NumberOfOperands: 0);
            }

            // Ensure .MF extension
            if (!filePath.EndsWith(".MF", StringComparison.OrdinalIgnoreCase))
            {
                filePath = filePath + ".MF";
            }

            return await _session.ExecuteAsync("SaveMeritFunctionFile",
                new Dictionary<string, object?> { ["filePath"] = filePath },
                system =>
            {
                var mfe = system.MFE;
                var numberOfOperands = mfe.NumberOfOperands;

                mfe.SaveMeritFunction(filePath);

                return new SaveMeritFunctionFileResult(
                    Success: true,
                    Error: null,
                    FilePath: filePath,
                    NumberOfOperands: numberOfOperands);
            });
        }
        catch (Exception ex)
        {
            return new SaveMeritFunctionFileResult(
                Success: false,
                Error: ex.Message,
                FilePath: null,
                NumberOfOperands: 0);
        }
    }
}
