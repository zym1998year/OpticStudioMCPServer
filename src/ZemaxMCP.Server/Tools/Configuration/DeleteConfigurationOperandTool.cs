using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Configuration;

[McpServerToolType]
public class DeleteConfigurationOperandTool
{
    private readonly IZemaxSession _session;

    public DeleteConfigurationOperandTool(IZemaxSession session) => _session = session;

    public record DeleteConfigurationOperandResult(
        bool Success,
        string? Error,
        int DeletedRow,
        int NumberOfOperands
    );

    [McpServerTool(Name = "zemax_delete_configuration_operand")]
    [Description("Delete a configuration operand from the multi-configuration editor")]
    public async Task<DeleteConfigurationOperandResult> ExecuteAsync(
        [Description("Row number to delete (1-indexed)")] int row)
    {
        if (row < 1)
        {
            return new DeleteConfigurationOperandResult(false, "Row number must be at least 1", 0, 0);
        }

        try
        {
            var result = await _session.ExecuteAsync("DeleteConfigurationOperand",
                new Dictionary<string, object?> { ["row"] = row },
                system =>
            {
                var mce = system.MCE;

                if (row > mce.NumberOfOperands)
                {
                    return new DeleteConfigurationOperandResult(
                        Success: false,
                        Error: $"Row {row} does not exist. MCE has {mce.NumberOfOperands} operands.",
                        DeletedRow: 0,
                        NumberOfOperands: mce.NumberOfOperands
                    );
                }

                mce.RemoveOperandAt(row);

                return new DeleteConfigurationOperandResult(
                    Success: true,
                    Error: null,
                    DeletedRow: row,
                    NumberOfOperands: mce.NumberOfOperands
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new DeleteConfigurationOperandResult(false, ex.Message, 0, 0);
        }
    }
}
