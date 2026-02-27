using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class RemoveOperandTool
{
    private readonly IZemaxSession _session;

    public RemoveOperandTool(IZemaxSession session) => _session = session;

    public record RemoveOperandResult(
        bool Success,
        string? Error,
        int RemovedRow,
        int RemainingOperands
    );

    [McpServerTool(Name = "zemax_remove_operand")]
    [Description("Remove an operand from the merit function")]
    public async Task<RemoveOperandResult> ExecuteAsync(
        [Description("Row number to remove (1-indexed)")] int row)
    {
        try
        {
            var result = await _session.ExecuteAsync("RemoveOperand",
                new Dictionary<string, object?> { ["row"] = row },
                system =>
            {
                var mfe = system.MFE;

                if (row < 1 || row > mfe.NumberOfOperands)
                {
                    return new RemoveOperandResult(
                        Success: false,
                        Error: $"Invalid row number: {row}. Valid range: 1-{mfe.NumberOfOperands}",
                        RemovedRow: row,
                        RemainingOperands: mfe.NumberOfOperands
                    );
                }

                mfe.RemoveOperandAt(row);

                return new RemoveOperandResult(
                    Success: true,
                    Error: null,
                    RemovedRow: row,
                    RemainingOperands: mfe.NumberOfOperands
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new RemoveOperandResult(false, ex.Message, row, 0);
        }
    }
}
