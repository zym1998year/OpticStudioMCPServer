using System.ComponentModel;
using ModelContextProtocol.Server;
using ZOSAPI.Editors.MCE;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Configuration;

[McpServerToolType]
public class GetConfigurationOperandsTool
{
    private readonly IZemaxSession _session;

    public GetConfigurationOperandsTool(IZemaxSession session) => _session = session;

    public record GetConfigurationOperandsResult(
        bool Success,
        string? Error,
        int NumberOfOperands,
        int NumberOfConfigurations,
        List<ConfigurationOperand> Operands
    );

    [McpServerTool(Name = "zemax_get_configuration_operands")]
    [Description("Get all configuration operands from the Multi-Configuration Editor with values across all configurations")]
    public async Task<GetConfigurationOperandsResult> ExecuteAsync(
        [Description("Starting operand row (1-indexed, default 1)")] int startRow = 1,
        [Description("Ending operand row (0 for all)")] int endRow = 0)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["startRow"] = startRow,
                ["endRow"] = endRow
            };

            var result = await _session.ExecuteAsync("GetConfigurationOperands", parameters, system =>
            {
                var mce = system.MCE;
                var numOperands = mce.NumberOfOperands;
                var numConfigs = mce.NumberOfConfigurations;

                var operands = new List<ConfigurationOperand>();

                var start = Math.Max(1, startRow);
                var end = endRow > 0 ? Math.Min(endRow, numOperands) : numOperands;

                for (int rowNum = start; rowNum <= end; rowNum++)
                {
                    var row = mce.GetOperandAt(rowNum);
                    var values = new List<ConfigurationValue>();

                    // Get values for each configuration
                    for (int configNum = 1; configNum <= numConfigs; configNum++)
                    {
                        var cell = row.GetOperandCell(configNum);
                        var solveData = cell.GetSolveData();

                        var configValue = new ConfigurationValue
                        {
                            ConfigurationNumber = configNum,
                            Value = cell.DoubleValue,
                            SolveType = solveData.Type.ToString()
                        };

                        // Try to get pickup info if applicable (using dynamic to handle API variations)
                        if (solveData.Type == ZOSAPI.Editors.SolveType.ConfigPickup)
                        {
                            try
                            {
                                dynamic dynSolveData = solveData;
                                configValue = configValue with
                                {
                                    PickupConfig = (int?)dynSolveData.ConfigPickup_Configuration,
                                    ScaleFactor = (double?)dynSolveData.ConfigPickup_ScaleFactor,
                                    Offset = (double?)dynSolveData.ConfigPickup_Offset
                                };
                            }
                            catch
                            {
                                // Pickup info not available in this API version
                            }
                        }

                        values.Add(configValue);
                    }

                    var operand = new ConfigurationOperand
                    {
                        OperandNumber = rowNum,
                        OperandType = row.Type.ToString(),
                        Param1 = row.Param1,
                        Param2 = row.Param2,
                        Param3 = row.Param3,
                        Values = values
                    };

                    operands.Add(operand);
                }

                return new GetConfigurationOperandsResult(
                    Success: true,
                    Error: null,
                    NumberOfOperands: numOperands,
                    NumberOfConfigurations: numConfigs,
                    Operands: operands
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new GetConfigurationOperandsResult(false, ex.Message, 0, 0, new List<ConfigurationOperand>());
        }
    }
}
