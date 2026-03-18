using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class MultistartOptimizeTool
{
    private readonly IZemaxSession _session;
    private readonly ConstraintStore _constraintStore;

    public MultistartOptimizeTool(IZemaxSession session, ConstraintStore constraintStore)
    {
        _session = session;
        _constraintStore = constraintStore;
    }

    public record MultistartOptimizeResult(
        bool Success,
        string? Error,
        double InitialMerit,
        double PostInitialLmMerit,
        double FinalMerit,
        int TrialsRun,
        int TrialsAccepted,
        string Message
    );

    [McpServerTool(Name = "zemax_multistart_optimize")]
    [Description("Custom MCP-implemented multistart optimizer. Each trial: randomizes continuous variables within bounds, randomly substitutes glasses on MaterialSubstitute surfaces, then runs a short LM optimization. Keeps the best result. Set variable constraints first with zemax_set_variable_constraints. NOT a built-in Zemax optimizer.")]
    public async Task<MultistartOptimizeResult> ExecuteAsync(
        [Description("Number of random restart trials (default 100)")] int maxTrials = 100,
        [Description("LM iterations per trial (default 50)")] int lmIterationsPerTrial = 50,
        [Description("Full LM iterations from current starting point before trials begin (default 200)")] int initialLmIterations = 200,
        [Description("Percentage of bound range to randomize around current best, e.g. 10 = ±10% of bound range. For unconstrained variables, uses ±percent of current value. (default 10.0)")] double randomizationPercent = 10.0,
        [Description("Initial damping parameter mu (default 1e-3)")] double initialMu = 1e-3,
        [Description("Finite difference step size delta (default 1e-7)")] double delta = 1e-7,
        [Description("Use Broyden rank-1 Jacobian updates (default true)")] bool useBroydenUpdate = true,
        [Description("Maximum auto-restarts for the initial LM run (default 0)")] int maxRestarts = 0)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["maxTrials"] = maxTrials,
                ["lmIterationsPerTrial"] = lmIterationsPerTrial,
                ["initialLmIterations"] = initialLmIterations,
                ["randomizationPercent"] = randomizationPercent,
                ["initialMu"] = initialMu,
                ["delta"] = delta,
                ["useBroydenUpdate"] = useBroydenUpdate,
                ["maxRestarts"] = maxRestarts
            };

            var result = await _session.ExecuteAsync("MultistartOptimize", parameters, system =>
            {
                var scanner = new VariableScanner();
                var meritReader = new MeritFunctionReader();
                var multistartOptimizer = new MultistartOptimizer(meritReader);

                var variables = scanner.ScanVariables(system);
                _constraintStore.ApplyConstraints(variables);

                var substituteMaterials = scanner.ScanMaterials(system);
                // Filter to only MaterialSubstitute surfaces with available glasses
                substituteMaterials = substituteMaterials
                    .Where(m => m.SolveType == ZOSAPI.Editors.SolveType.MaterialSubstitute
                                && m.SubstituteGlasses != null
                                && m.SubstituteGlasses.Length > 1)
                    .ToList();

                var msResult = multistartOptimizer.Optimize(
                    system, variables, substituteMaterials,
                    maxTrials, lmIterationsPerTrial, initialLmIterations,
                    randomizationPercent, initialMu, delta,
                    useBroydenUpdate, maxRestarts);

                return new MultistartOptimizeResult(
                    Success: msResult.Success,
                    Error: msResult.Success ? null : msResult.Message,
                    InitialMerit: msResult.InitialMerit,
                    PostInitialLmMerit: msResult.PostInitialLmMerit,
                    FinalMerit: msResult.FinalMerit,
                    TrialsRun: msResult.TrialsRun,
                    TrialsAccepted: msResult.TrialsAccepted,
                    Message: msResult.Message
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new MultistartOptimizeResult(false, ex.Message, 0, 0, 0, 0, 0, $"Error: {ex.Message}");
        }
    }
}
