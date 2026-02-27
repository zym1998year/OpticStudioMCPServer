using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;
using ZemaxMCP.Server.Tools.Base;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class GetMeritFunctionTool
{
    private readonly IZemaxSession _session;
    private readonly ILogger<GetMeritFunctionTool> _logger;

    public GetMeritFunctionTool(IZemaxSession session, ILogger<GetMeritFunctionTool> logger)
    {
        _session = session;
        _logger = logger;
    }

    public record GetMeritFunctionResult(
        bool Success,
        string? Error,
        double TotalMerit,
        int NumberOfOperands,
        List<Operand> Operands
    );

    [McpServerTool(Name = "zemax_get_merit_function")]
    [Description("Retrieve the current merit function with all operands")]
    public async Task<GetMeritFunctionResult> ExecuteAsync(
        [Description("Include operand values (recalculates if needed)")] bool includeValues = true,
        [Description("Start row (1-indexed, 0 for all)")] int startRow = 0,
        [Description("End row (0 for all)")] int endRow = 0)
    {
        _logger.LogInformation("GetMeritFunction starting: includeValues={IncludeValues}, startRow={StartRow}, endRow={EndRow}",
            includeValues, startRow, endRow);

        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["includeValues"] = includeValues,
                ["startRow"] = startRow,
                ["endRow"] = endRow
            };

            var result = await _session.ExecuteAsync("GetMeritFunction", parameters, system =>
            {
                if (system == null)
                {
                    throw new InvalidOperationException("Optical system is not available");
                }

                _logger.LogDebug("Inside ExecuteAsync - accessing MFE");
                var mfe = system.MFE;
                if (mfe == null)
                {
                    throw new InvalidOperationException("Merit Function Editor is not available");
                }

                _logger.LogDebug("Getting NumberOfOperands");
                var numberOfOperands = mfe.NumberOfOperands;
                _logger.LogInformation("Merit function has {Count} operands", numberOfOperands);

                // Handle empty merit function
                if (numberOfOperands == 0)
                {
                    _logger.LogInformation("Merit function is empty, returning empty result");
                    return new GetMeritFunctionResult(
                        Success: true,
                        Error: null,
                        TotalMerit: 0,
                        NumberOfOperands: 0,
                        Operands: new List<Operand>()
                    );
                }

                double totalMerit = 0;
                if (includeValues)
                {
                    _logger.LogDebug("Calculating merit function");
                    totalMerit = mfe.CalculateMeritFunction();
                    // Sanitize totalMerit
                    totalMerit = totalMerit.Sanitize();
                    _logger.LogInformation("Total merit = {Merit}", totalMerit);
                }

                var operands = new List<Operand>();
                var start = startRow > 0 ? startRow : 1;
                var end = endRow > 0 ? endRow : numberOfOperands;

                _logger.LogDebug("Reading rows {Start} to {End}", start, end);

                var errors = new List<string>();

                for (int i = start; i <= end && i <= numberOfOperands; i++)
                {
                    try
                    {
                        // Use GetOperandAt instead of GetRowAt - matches working pattern
                        var row = mfe.GetOperandAt(i);
                        if (row == null)
                        {
                            errors.Add($"Row {i}: GetOperandAt returned null");
                            _logger.LogWarning("Row {Row}: GetOperandAt returned null", i);
                            continue;
                        }

                        double target = 0, value = 0, weight = 0;
                        string rowtypename = "Unknown";

                        try { target = row.Target.Sanitize(); }
                        catch (Exception ex) { errors.Add($"Row {i}: Target read failed - {ex.Message}"); }

                        try { value = row.Value.Sanitize(); }
                        catch (Exception ex) { errors.Add($"Row {i}: Value read failed - {ex.Message}"); }

                        try { weight = row.Weight.Sanitize(); }
                        catch (Exception ex) { errors.Add($"Row {i}: Weight read failed - {ex.Message}"); }

                        try { rowtypename = row.RowTypeName ?? "Unknown"; }
                        catch (Exception ex) { errors.Add($"Row {i}: RowTypeName read failed - {ex.Message}"); }

                        int int1 = 0, int2 = 0;
                        double d1 = 0, d2 = 0, d3 = 0, d4 = 0, d5 = 0, d6 = 0;

                        try { int1 = row.GetCellAt(2)?.IntegerValue ?? 0; } catch { }
                        try { int2 = row.GetCellAt(3)?.IntegerValue ?? 0; } catch { }
                        try { d1 = (row.GetCellAt(4)?.DoubleValue ?? 0).Sanitize(); } catch { }
                        try { d2 = (row.GetCellAt(5)?.DoubleValue ?? 0).Sanitize(); } catch { }
                        try { d3 = (row.GetCellAt(6)?.DoubleValue ?? 0).Sanitize(); } catch { }
                        try { d4 = (row.GetCellAt(7)?.DoubleValue ?? 0).Sanitize(); } catch { }
                        try { d5 = (row.GetCellAt(8)?.DoubleValue ?? 0).Sanitize(); } catch { }
                        try { d6 = (row.GetCellAt(9)?.DoubleValue ?? 0).Sanitize(); } catch { }

                        // Calculate contribution with sanitization
                        double contribution = 0;
                        if (weight > 0)
                        {
                            var diff = target - value;
                            contribution = (weight * diff * diff).Sanitize();
                        }

                        operands.Add(new Operand
                        {
                            Row = i,
                            Type = rowtypename,
                            Int1 = int1,
                            Int2 = int2,
                            Data1 = d1,
                            Data2 = d2,
                            Data3 = d3,
                            Data4 = d4,
                            Data5 = d5,
                            Data6 = d6,
                            Target = target,
                            Weight = weight,
                            Value = value,
                            Contribution = contribution
                        });
                    }
                    catch (Exception rowEx)
                    {
                        errors.Add($"Row {i}: Unhandled exception - {rowEx.GetType().Name}: {rowEx.Message}");
                        _logger.LogError(rowEx, "Error reading row {Row}", i);
                    }
                }

                string? errorSummary = errors.Count > 0 ? string.Join("; ", errors.Take(10)) : null;
                if (errorSummary != null)
                {
                    _logger.LogWarning("Errors during merit function read: {Errors}", errorSummary);
                }

                _logger.LogInformation("Successfully read {Count} operands", operands.Count);

                return new GetMeritFunctionResult(
                    Success: true,
                    Error: errorSummary,
                    TotalMerit: totalMerit,
                    NumberOfOperands: numberOfOperands,
                    Operands: operands
                );
            });

            _logger.LogInformation("GetMeritFunction completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            var errorDetails = $"{ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorDetails += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            }
            errorDetails += $" | StackTrace: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";

            _logger.LogError(ex, "GetMeritFunction failed: {Error}", errorDetails);

            return new GetMeritFunctionResult(
                Success: false,
                Error: errorDetails,
                TotalMerit: 0,
                NumberOfOperands: 0,
                Operands: new List<Operand>()
            );
        }
    }
}
