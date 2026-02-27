using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Documentation;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class OperandHelpTool
{
    private readonly OperandDatabase _operandDb;

    public OperandHelpTool(OperandDatabase operandDb) => _operandDb = operandDb;

    public record ParameterInfo(string Name, string Description, string DefaultValue);

    public record OperandHelpResult(
        bool Found,
        string? Name,
        string? Description,
        string? Category,
        List<ParameterInfo>? Parameters,
        string? Example,
        List<string>? RelatedOperands
    );

    [McpServerTool(Name = "zemax_operand_help")]
    [Description("Get detailed help for a specific optimization operand")]
    public Task<OperandHelpResult> ExecuteAsync(
        [Description("Operand type (e.g., EFFL, MTFT, RSCE)")] string operandType)
    {
        var operand = _operandDb.GetOperand(operandType);

        if (operand == null)
        {
            return Task.FromResult(new OperandHelpResult(
                Found: false,
                Name: null,
                Description: $"Operand '{operandType}' not found. " +
                    "Use zemax_search_operands to find valid operand types.",
                Category: null,
                Parameters: null,
                Example: null,
                RelatedOperands: null
            ));
        }

        return Task.FromResult(new OperandHelpResult(
            Found: true,
            Name: operand.Name,
            Description: operand.Description,
            Category: operand.Category,
            Parameters: operand.Parameters.Select(p =>
                new ParameterInfo(p.Name, p.Description, p.DefaultValue)).ToList(),
            Example: operand.Example,
            RelatedOperands: operand.RelatedOperands
        ));
    }
}
