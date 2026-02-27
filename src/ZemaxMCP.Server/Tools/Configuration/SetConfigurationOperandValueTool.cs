using System.ComponentModel;
using ModelContextProtocol.Server;
using ZOSAPI.Editors.MCE;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Configuration;

[McpServerToolType]
public class SetConfigurationOperandValueTool
{
    private readonly IZemaxSession _session;

    public SetConfigurationOperandValueTool(IZemaxSession session) => _session = session;

    public record SetConfigurationOperandValueResult(
        bool Success,
        string? Error,
        ConfigurationValue? NewValue
    );

    [McpServerTool(Name = "zemax_set_configuration_operand_value")]
    [Description("Set the value or pickup solve for a configuration operand")]
    public async Task<SetConfigurationOperandValueResult> ExecuteAsync(
        [Description("Operand row number (1-indexed)")] int operandRow,
        [Description("Configuration number (1-indexed)")] int configurationNumber,
        [Description("Value to set (use this OR pickup parameters)")] double? value = null,
        [Description("Set as pickup from this configuration number")] int? pickupConfig = null,
        [Description("Scale factor for pickup")] double? scaleFactor = null,
        [Description("Offset for pickup")] double? offset = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["operandRow"] = operandRow,
                ["configurationNumber"] = configurationNumber,
                ["value"] = value,
                ["pickupConfig"] = pickupConfig
            };

            var result = await _session.ExecuteAsync("SetConfigurationOperandValue", parameters, system =>
            {
                var mce = system.MCE;

                if (operandRow < 1 || operandRow > mce.NumberOfOperands)
                {
                    throw new ArgumentException(
                        $"Invalid operand row: {operandRow}. Valid range: 1-{mce.NumberOfOperands}");
                }

                if (configurationNumber < 1 || configurationNumber > mce.NumberOfConfigurations)
                {
                    throw new ArgumentException(
                        $"Invalid configuration: {configurationNumber}. Valid range: 1-{mce.NumberOfConfigurations}");
                }

                var row = mce.GetOperandAt(operandRow);
                var cell = row.GetOperandCell(configurationNumber);

                // Use dynamic to handle MCE cell operations that may vary by API version
                dynamic dynCell = cell;

                if (pickupConfig.HasValue)
                {
                    // Set as pickup solve
                    if (pickupConfig.Value < 1 || pickupConfig.Value > mce.NumberOfConfigurations)
                    {
                        throw new ArgumentException(
                            $"Invalid pickup configuration: {pickupConfig.Value}. Valid range: 1-{mce.NumberOfConfigurations}");
                    }

                    try
                    {
                        dynCell.MakeSolvePickup(
                            pickupConfig.Value,
                            scaleFactor ?? 1.0,
                            offset ?? 0.0);
                    }
                    catch
                    {
                        throw new InvalidOperationException("Pickup solves are not supported for MCE cells in this API version");
                    }
                }
                else if (value.HasValue)
                {
                    // Set as fixed value
                    cell.MakeSolveFixed();
                    cell.DoubleValue = value.Value;
                }
                else
                {
                    throw new ArgumentException("Either 'value' or 'pickupConfig' must be provided");
                }

                // Get the updated value
                var solveData = cell.GetSolveData();
                var newValue = new ConfigurationValue
                {
                    ConfigurationNumber = configurationNumber,
                    Value = cell.DoubleValue,
                    SolveType = solveData.Type.ToString()
                };

                if (solveData.Type == ZOSAPI.Editors.SolveType.ConfigPickup)
                {
                    try
                    {
                        dynamic dynSolveData = solveData;
                        newValue = newValue with
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

                return new SetConfigurationOperandValueResult(
                    Success: true,
                    Error: null,
                    NewValue: newValue
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SetConfigurationOperandValueResult(false, ex.Message, null);
        }
    }
}
