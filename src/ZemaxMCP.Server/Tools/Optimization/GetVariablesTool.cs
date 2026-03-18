using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class GetVariablesTool
{
    private readonly IZemaxSession _session;
    private readonly ConstraintStore _constraintStore;

    public GetVariablesTool(IZemaxSession session, ConstraintStore constraintStore)
    {
        _session = session;
        _constraintStore = constraintStore;
    }

    public record VariableInfo(
        int VariableNumber,
        string Description,
        string Type,
        int SurfaceNumber,
        int ParameterNumber,
        int FieldNumber,
        int ConfigOperandRow,
        int ConfigColumn,
        double Value,
        string Constraint,
        double? Min,
        double? Max
    );

    public record GetVariablesResult(
        bool Success,
        string? Error,
        int VariableCount,
        VariableInfo[] Variables
    );

    [McpServerTool(Name = "zemax_get_variables")]
    [Description("Scan the current optical system for all variables (Variable solves) and return them with their current constraint settings. Use this before zemax_set_variable_constraints to identify variable numbers.")]
    public async Task<GetVariablesResult> ExecuteAsync()
    {
        try
        {
            var result = await _session.ExecuteAsync("GetVariables", null, system =>
            {
                var scanner = new VariableScanner();
                var variables = scanner.ScanVariables(system);

                // Apply any stored constraints
                _constraintStore.ApplyConstraints(variables);

                var infos = variables.Select(v =>
                {
                    double? min = v.Constraint is ConstraintType.MinAndMax or ConstraintType.MinOnly ? v.Min : null;
                    double? max = v.Constraint is ConstraintType.MinAndMax or ConstraintType.MaxOnly ? v.Max : null;

                    return new VariableInfo(
                        v.VariableNumber,
                        v.Description,
                        v.Type.ToString(),
                        v.SurfaceNumber,
                        v.ParameterNumber,
                        v.FieldNumber,
                        v.ConfigOperandRow,
                        v.ConfigColumn,
                        v.Value,
                        v.Constraint.ToString(),
                        min,
                        max
                    );
                }).ToArray();

                return new GetVariablesResult(
                    Success: true,
                    Error: null,
                    VariableCount: infos.Length,
                    Variables: infos
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new GetVariablesResult(false, ex.Message, 0, Array.Empty<VariableInfo>());
        }
    }
}
