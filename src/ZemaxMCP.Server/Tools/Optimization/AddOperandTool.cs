using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZemaxMCP.Documentation;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class AddOperandTool
{
    private readonly IZemaxSession _session;
    private readonly OperandDatabase _operandDb;

    public AddOperandTool(IZemaxSession session, OperandDatabase operandDb)
    {
        _session = session;
        _operandDb = operandDb;
    }

    public record AddOperandResult(
        bool Success,
        string? Error,
        int Row,
        string OperandType,
        double Value,
        double Target,
        double Weight,
        string? OperandDescription
    );

    [McpServerTool(Name = "zemax_add_operand")]
    [Description("Add an optimization operand to the merit function")]
    public async Task<AddOperandResult> ExecuteAsync(
        [Description("Operand type (e.g., EFFL, MTFT, RSCE)")] string operandType,
        [Description("Target value")] double target = 0,
        [Description("Weight")] double weight = 1,
        [Description("Row to insert at (0 to append)")] int insertAt = 0,
        [Description("Int1 parameter (meaning depends on operand)")] int? int1 = null,
        [Description("Int2 parameter (meaning depends on operand)")] int? int2 = null,
        [Description("Data1 parameter")] double? data1 = null,
        [Description("Data2 parameter")] double? data2 = null,
        [Description("Data3 parameter")] double? data3 = null,
        [Description("Data4 parameter")] double? data4 = null,
        [Description("Data5 parameter")] double? data5 = null,
        [Description("Data6 parameter")] double? data6 = null)
    {
        // Validate operand type
        var operandDef = _operandDb.GetOperand(operandType);
        if (operandDef == null)
        {
            var suggestions = _operandDb.SearchOperands(operandType, 3);
            var suggestText = suggestions.Any()
                ? $"Did you mean: {string.Join(", ", suggestions.Select(s => s.Operand.Name))}"
                : "Use zemax_search_operands to find valid operand types.";

            return new AddOperandResult(
                Success: false,
                Error: $"Unknown operand type: {operandType}. {suggestText}",
                Row: 0,
                OperandType: operandType,
                Value: 0,
                Target: target,
                Weight: weight,
                OperandDescription: null
            );
        }

        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["operandType"] = operandType,
                ["target"] = target,
                ["weight"] = weight,
                ["insertAt"] = insertAt,
                ["int1"] = int1,
                ["int2"] = int2,
                ["data1"] = data1,
                ["data2"] = data2,
                ["data3"] = data3,
                ["data4"] = data4,
                ["data5"] = data5,
                ["data6"] = data6
            };

            var result = await _session.ExecuteAsync("AddOperand", parameters, system =>
            {
                if (system == null)
                {
                    throw new InvalidOperationException("Optical system is not available");
                }

                var mfe = system.MFE;
                if (mfe == null)
                {
                    throw new InvalidOperationException("Merit Function Editor is not available");
                }

                ZOSAPI.Editors.MFE.IMFERow row;
                if (insertAt > 0 && insertAt <= mfe.NumberOfOperands)
                {
                    row = mfe.InsertNewOperandAt(insertAt);
                }
                else
                {
                    row = mfe.AddOperand();
                }

                // Parse and set operand type
                if (Enum.TryParse<ZOSAPI.Editors.MFE.MeritOperandType>(
                    operandType, true, out var opType))
                {
                    row.ChangeType(opType);
                }
                else
                {
                    throw new ArgumentException($"Invalid operand type: {operandType}");
                }

                // Set parameters
                if (int1.HasValue) row.GetCellAt(2).IntegerValue = int1.Value;
                if (int2.HasValue) row.GetCellAt(3).IntegerValue = int2.Value;
                if (data1.HasValue) row.GetCellAt(4).DoubleValue = data1.Value;
                if (data2.HasValue) row.GetCellAt(5).DoubleValue = data2.Value;
                if (data3.HasValue) row.GetCellAt(6).DoubleValue = data3.Value;
                if (data4.HasValue) row.GetCellAt(7).DoubleValue = data4.Value;
                if (data5.HasValue) row.GetCellAt(8).DoubleValue = data5.Value;
                if (data6.HasValue) row.GetCellAt(9).DoubleValue = data6.Value;

                row.Target = target;
                row.Weight = weight;

                // Calculate to get initial value
                mfe.CalculateMeritFunction();

                return new AddOperandResult(
                    Success: true,
                    Error: null,
                    Row: row.OperandNumber,
                    OperandType: operandType,
                    Value: row.Value,
                    Target: row.Target,
                    Weight: row.Weight,
                    OperandDescription: operandDef?.Description
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new AddOperandResult(
                Success: false,
                Error: ex.Message,
                Row: 0,
                OperandType: operandType,
                Value: 0,
                Target: target,
                Weight: weight,
                OperandDescription: operandDef?.Description
            );
        }
    }
}
