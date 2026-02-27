using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Configuration;

[McpServerToolType]
public class AddConfigurationOperandTool
{
    private readonly IZemaxSession _session;

    public AddConfigurationOperandTool(IZemaxSession session) => _session = session;

    public record AddConfigurationOperandResult(
        bool Success,
        string? Error,
        int Row,
        string OperandType,
        int NumberOfOperands
    );

    [McpServerTool(Name = "zemax_add_configuration_operand")]
    [Description("Add a configuration operand to the multi-configuration editor")]
    public async Task<AddConfigurationOperandResult> ExecuteAsync(
        [Description("Operand type (e.g., THIC, CURV, CONI, PRAM, MOFF)")] string operandType,
        [Description("Row to insert at (0 to append)")] int insertAt = 0,
        [Description("Parameter 1 (surface number for most operands)")] int param1 = 0,
        [Description("Parameter 2")] int param2 = 0,
        [Description("Parameter 3")] int param3 = 0)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["operandType"] = operandType,
                ["insertAt"] = insertAt,
                ["param1"] = param1,
                ["param2"] = param2,
                ["param3"] = param3
            };

            var result = await _session.ExecuteAsync("AddConfigurationOperand", parameters, system =>
            {
                var mce = system.MCE;

                ZOSAPI.Editors.MCE.IMCERow row;
                if (insertAt > 0 && insertAt <= mce.NumberOfOperands)
                {
                    row = mce.InsertNewOperandAt(insertAt);
                }
                else
                {
                    row = mce.AddOperand();
                }

                // Parse and set operand type
                if (Enum.TryParse<ZOSAPI.Editors.MCE.MultiConfigOperandType>(
                    operandType, true, out var opType))
                {
                    row.ChangeType(opType);
                }
                else
                {
                    throw new ArgumentException($"Invalid configuration operand type: {operandType}");
                }

                // Set parameters
                row.Param1 = param1;
                row.Param2 = param2;
                row.Param3 = param3;

                return new AddConfigurationOperandResult(
                    Success: true,
                    Error: null,
                    Row: row.OperandNumber,
                    OperandType: operandType,
                    NumberOfOperands: mce.NumberOfOperands
                );
            });
            return result;
        }
        catch (Exception ex)
        {
            return new AddConfigurationOperandResult(false, ex.Message, 0, operandType, 0);
        }
    }
}
