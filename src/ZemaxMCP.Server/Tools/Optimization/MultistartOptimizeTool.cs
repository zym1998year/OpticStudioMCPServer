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
    private readonly MultistartState _multistartState;

    public MultistartOptimizeTool(IZemaxSession session, ConstraintStore constraintStore, MultistartState multistartState)
    {
        _session = session;
        _constraintStore = constraintStore;
        _multistartState = multistartState;
    }

    public record MultistartOptimizeResult(
        bool Success,
        string? Error,
        string Message
    );

    [McpServerTool(Name = "zemax_multistart_optimize")]
    [Description("Start a non-blocking multistart optimization. Returns immediately — use zemax_multistart_status to poll progress and zemax_multistart_stop to cancel. Each trial: randomizes continuous variables within bounds, optionally substitutes glass, then runs a short LM optimization. Keeps the best result. Auto-saves on each improvement. Set variable constraints first with zemax_set_variable_constraints.")]
    public MultistartOptimizeResult Execute(
        [Description("Number of random restart trials (default 100)")] int maxTrials = 100,
        [Description("LM iterations per trial (default 50)")] int lmIterationsPerTrial = 50,
        [Description("Full LM iterations from current starting point before trials begin (default 200)")] int initialLmIterations = 200,
        [Description("Percentage of bound range to randomize around current best, e.g. 5 = ±5% of bound range. (default 5.0)")] double randomizationPercent = 5.0,
        [Description("Initial damping parameter mu (default 1e-3)")] double initialMu = 1e-3,
        [Description("Finite difference step size delta (default 1e-7)")] double delta = 1e-7,
        [Description("Use Broyden rank-1 Jacobian updates (default true)")] bool useBroydenUpdate = true,
        [Description("Maximum auto-restarts for the initial LM run (default 0)")] int maxRestarts = 0,
        [Description("Only randomize constrained variables (with min/max bounds), leaving unconstrained variables at their current best values (default false)")] bool constrainedOnly = false,
        [Description("Probability (0.0-1.0) that glass substitution occurs on a given trial. (default 0.5)")] double glassSubstitutionProbability = 0.5,
        [Description("Log progress to stderr every N trials. 0 = no progress logging. (default 0)")] int progressInterval = 0,
        [Description("Resume from previous run: skip initial LM, continue accumulating trials. (default false)")] bool resume = false)
    {
        if (_multistartState.IsRunning)
        {
            return new MultistartOptimizeResult(false, "Optimization already running",
                $"Multistart optimization is already running (trial {_multistartState.CurrentTrial}/{_multistartState.MaxTrials}). " +
                "Use zemax_multistart_status to check progress or zemax_multistart_stop to cancel.");
        }

        // Determine if we should skip initial LM (resume mode with prior state)
        bool skipInitialLm = resume && _multistartState.HasState;

        // If not resuming, reset state for a fresh run
        if (!skipInitialLm)
            _multistartState.Reset();

        // Set up save folder
        var currentFile = _session.CurrentFilePath;
        if (!string.IsNullOrEmpty(currentFile))
        {
            if (string.IsNullOrEmpty(_multistartState.SaveFolder))
            {
                var dir = Path.GetDirectoryName(currentFile)!;
                var name = Path.GetFileNameWithoutExtension(currentFile);
                var saveFolder = Path.Combine(dir, $"{name}_multistart");
                Directory.CreateDirectory(saveFolder);
                _multistartState.SaveFolder = saveFolder;
            }
        }

        var cancellationToken = _multistartState.CreateCancellationToken();
        int priorTrialsRun = _multistartState.TotalTrialsRun;
        int priorTrialsAccepted = _multistartState.TotalTrialsAccepted;

        _multistartState.SetRunning(maxTrials);

        // Launch optimization on background thread
        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("MultistartOptimize",
                    new Dictionary<string, object?>
                    {
                        ["maxTrials"] = maxTrials,
                        ["constrainedOnly"] = constrainedOnly,
                        ["resume"] = resume
                    },
                    system =>
                    {
                        var scanner = new VariableScanner();
                        var meritReader = new MeritFunctionReader();
                        var multistartOptimizer = new MultistartOptimizer(meritReader);

                        var variables = scanner.ScanVariables(system);
                        _constraintStore.ApplyConstraints(variables);

                        var substituteMaterials = scanner.ScanMaterials(system)
                            .Where(m => m.SolveType == ZOSAPI.Editors.SolveType.MaterialSubstitute
                                        && m.SubstituteGlasses != null
                                        && m.SubstituteGlasses.Length > 1)
                            .ToList();

                        // Auto-save callback
                        Action<int, double> onImprovement = (trial, merit) =>
                        {
                            _multistartState.BestMerit = merit;
                            _multistartState.SaveCount++;

                            if (!string.IsNullOrEmpty(_multistartState.SaveFolder))
                            {
                                var totalTrial = priorTrialsRun + trial;
                                var meritStr = merit.ToString("F6").Replace(".", "p");
                                var saveName = $"best_t{totalTrial}_mf{meritStr}.zmx";
                                var savePath = Path.Combine(_multistartState.SaveFolder, saveName);

                                try
                                {
                                    system.SaveAs(savePath);
                                    _constraintStore.SaveToFile(savePath);
                                }
                                catch { /* best effort save */ }
                            }
                        };

                        // Progress callback - updates MultistartState every trial
                        Action<int, int, double, int> onProgress = (trial, total, bestMerit, accepted) =>
                        {
                            _multistartState.UpdateProgress(trial, total, bestMerit, priorTrialsAccepted + accepted);
                        };

                        Action onInitialLmComplete = () =>
                        {
                            _multistartState.SetInitialLmComplete();
                        };

                        Action<int, int, double> onInitialLmProgress = (iteration, maxIter, merit) =>
                        {
                            _multistartState.UpdateInitialLmProgress(iteration, maxIter, merit);
                        };

                        var msResult = multistartOptimizer.Optimize(
                            system, variables, substituteMaterials,
                            maxTrials, lmIterationsPerTrial, initialLmIterations,
                            randomizationPercent, initialMu, delta,
                            useBroydenUpdate, maxRestarts, constrainedOnly,
                            glassSubstitutionProbability, progressInterval,
                            skipInitialLm: skipInitialLm,
                            onImprovement: onImprovement,
                            onProgress: onProgress,
                            onInitialLmComplete: onInitialLmComplete,
                            onInitialLmProgress: onInitialLmProgress,
                            cancellationToken: cancellationToken);

                        // Update persistent state
                        if (!skipInitialLm)
                            _multistartState.InitialMerit = msResult.InitialMerit;

                        _multistartState.TotalTrialsRun = priorTrialsRun + msResult.TrialsRun;
                        _multistartState.TotalTrialsAccepted = priorTrialsAccepted + msResult.TrialsAccepted;
                        _multistartState.BestMerit = msResult.FinalMerit;
                        _multistartState.InitialLmDone = true;

                        // Save final best state
                        if (!string.IsNullOrEmpty(_multistartState.SaveFolder))
                        {
                            try
                            {
                                var finalPath = Path.Combine(_multistartState.SaveFolder, "best_current.zmx");
                                system.SaveAs(finalPath);
                                _constraintStore.SaveToFile(finalPath);
                            }
                            catch { /* best effort */ }
                        }

                        _multistartState.SetCompleted();
                    }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _multistartState.SetCompleted("Cancelled by user");
            }
            catch (Exception ex)
            {
                _multistartState.SetCompleted($"Error: {ex.Message}");
            }
        });

        _multistartState.SetBackgroundTask(task);

        string resumeNote = skipInitialLm ? " (resuming, skipping initial LM)" : "";
        return new MultistartOptimizeResult(true, null,
            $"Multistart optimization started{resumeNote}: {maxTrials} trials, " +
            $"constrainedOnly={constrainedOnly}. " +
            "Use zemax_multistart_status to check progress, zemax_multistart_stop to cancel.");
    }
}
