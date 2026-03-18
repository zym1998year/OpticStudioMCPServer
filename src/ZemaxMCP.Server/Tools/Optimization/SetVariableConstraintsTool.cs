using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class SetVariableConstraintsTool
{
    private readonly IZemaxSession _session;
    private readonly ConstraintStore _constraintStore;

    public SetVariableConstraintsTool(IZemaxSession session, ConstraintStore constraintStore)
    {
        _session = session;
        _constraintStore = constraintStore;
    }

    public record ConstraintInput(
        int VariableNumber,
        string Constraint,
        double? Min,
        double? Max
    );

    public record SetConstraintsResult(
        bool Success,
        string? Error,
        int ConstraintsSet
    );

    [McpServerTool(Name = "zemax_set_variable_constraints")]
    [Description("Set min/max bounds on one or more variables for constrained optimization. Variables are identified by variable number from zemax_get_variables. Constraints are stored in memory for the session.")]
    public async Task<SetConstraintsResult> ExecuteAsync(
        [Description("JSON array of constraints: [{\"VariableNumber\": 1, \"Constraint\": \"MinAndMax\", \"Min\": -10, \"Max\": 10}]. Constraint values: Unconstrained, MinAndMax, MinOnly, MaxOnly")] string constraints)
    {
        try
        {
            var inputs = JsonSerializer.Deserialize<ConstraintInput[]>(constraints, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (inputs == null || inputs.Length == 0)
                return new SetConstraintsResult(false, "No constraints provided.", 0);

            var result = await _session.ExecuteAsync("SetVariableConstraints", null, system =>
            {
                // Scan current variables to validate variable numbers and get composite keys
                var scanner = new VariableScanner();
                var variables = scanner.ScanVariables(system);
                var varLookup = variables.ToDictionary(v => v.VariableNumber);

                int constraintsSet = 0;

                foreach (var input in inputs)
                {
                    if (!varLookup.TryGetValue(input.VariableNumber, out var variable))
                        throw new ArgumentException($"Variable number {input.VariableNumber} not found. Run zemax_get_variables to see available variables.");

                    if (!Enum.TryParse<ConstraintType>(input.Constraint, ignoreCase: true, out var constraintType))
                        throw new ArgumentException($"Invalid constraint type '{input.Constraint}'. Valid values: Unconstrained, MinAndMax, MinOnly, MaxOnly");

                    double min = input.Min ?? 0;
                    double max = input.Max ?? 0;

                    // Validate min/max
                    if (constraintType == ConstraintType.MinAndMax)
                    {
                        if (input.Min == null || input.Max == null)
                            throw new ArgumentException($"Variable {input.VariableNumber}: MinAndMax requires both Min and Max values.");
                        if (min >= max)
                            throw new ArgumentException($"Variable {input.VariableNumber}: Min ({min}) must be less than Max ({max}).");
                    }
                    else if (constraintType == ConstraintType.MinOnly && input.Min == null)
                    {
                        throw new ArgumentException($"Variable {input.VariableNumber}: MinOnly requires a Min value.");
                    }
                    else if (constraintType == ConstraintType.MaxOnly && input.Max == null)
                    {
                        throw new ArgumentException($"Variable {input.VariableNumber}: MaxOnly requires a Max value.");
                    }

                    _constraintStore.SetConstraint(variable.CompositeKey, constraintType, min, max);
                    constraintsSet++;
                }

                return new SetConstraintsResult(
                    Success: true,
                    Error: null,
                    ConstraintsSet: constraintsSet
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SetConstraintsResult(false, ex.Message, 0);
        }
    }
}
