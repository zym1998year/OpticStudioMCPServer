using System.ComponentModel;
using ModelContextProtocol.Server;
using ZOSAPI.Tools.Optimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class GlobalSearchTool
{
    private readonly IZemaxSession _session;

    public GlobalSearchTool(IZemaxSession session) => _session = session;

    public record GlobalSearchResult(
        bool Success,
        string? Error,
        double InitialMerit,
        double BestMerit,
        double Improvement,
        int SolutionsSaved,
        string Algorithm,
        double RuntimeSeconds,
        string TerminationReason
    );

    public record SolutionInfo(
        int SolutionNumber,
        double MeritFunction
    );

    [McpServerTool(Name = "zemax_global_search")]
    [Description("Run global optimization on the current optical system. Supports glass substitution when surfaces have MaterialSubstitute solve set.")]
    public async Task<GlobalSearchResult> ExecuteAsync(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of CPU cores to use (0 for all available)")] int cores = 0,
        [Description("Number of solutions to save: 10, 20, 50, or 100")] int solutionsToSave = 10,
        [Description("Maximum runtime in seconds (0 for no limit - will run until cancelled)")] double timeoutSeconds = 60)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["algorithm"] = algorithm,
                ["cores"] = cores,
                ["solutionsToSave"] = solutionsToSave,
                ["timeoutSeconds"] = timeoutSeconds
            };

            var result = await _session.ExecuteAsync("GlobalSearch", parameters, system =>
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

                var initialMerit = mfe.CalculateMeritFunction();

                var globalOpt = system.Tools?.OpenGlobalOptimization();
                if (globalOpt == null)
                {
                    throw new InvalidOperationException("Failed to open Global Optimization tool");
                }

                try
                {
                    // Set algorithm
                    globalOpt.Algorithm = algorithm.ToUpper() switch
                    {
                        "DLS" => OptimizationAlgorithm.DampedLeastSquares,
                        "ORTHOGONAL" => OptimizationAlgorithm.OrthogonalDescent,
                        _ => OptimizationAlgorithm.DampedLeastSquares
                    };

                    // Set number of cores (0 means use MaxCores)
                    if (cores > 0)
                    {
                        globalOpt.NumberOfCores = Math.Min(cores, globalOpt.MaxCores);
                    }
                    else
                    {
                        globalOpt.NumberOfCores = globalOpt.MaxCores;
                    }

                    // Set number of solutions to save
                    globalOpt.NumberToSave = solutionsToSave switch
                    {
                        <= 10 => OptimizationSaveCount.Save_10,
                        <= 20 => OptimizationSaveCount.Save_20,
                        <= 50 => OptimizationSaveCount.Save_50,
                        _ => OptimizationSaveCount.Save_100
                    };

                    string terminationReason;
                    double actualRuntime = timeoutSeconds;

                    if (timeoutSeconds > 0)
                    {
                        var runStatus = globalOpt.RunAndWaitWithTimeout(timeoutSeconds);
                        terminationReason = runStatus.ToString();
                    }
                    else
                    {
                        // Run without timeout (automatic termination)
                        globalOpt.RunAndWaitForCompletion();
                        terminationReason = "Completed";
                    }

                    // Get best merit function (solution 1 is the best)
                    var bestMerit = globalOpt.CurrentMeritFunction(1);

                    // Count how many valid solutions we have
                    int validSolutions = 0;
                    for (int i = 1; i <= solutionsToSave; i++)
                    {
                        var merit = globalOpt.CurrentMeritFunction(i);
                        if (merit > 0 && merit < double.MaxValue)
                        {
                            validSolutions++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    return new GlobalSearchResult(
                        Success: true,
                        Error: null,
                        InitialMerit: initialMerit,
                        BestMerit: bestMerit,
                        Improvement: initialMerit - bestMerit,
                        SolutionsSaved: validSolutions,
                        Algorithm: algorithm,
                        RuntimeSeconds: actualRuntime,
                        TerminationReason: terminationReason
                    );
                }
                finally
                {
                    globalOpt.Close();
                }
            });

            return result;
        }
        catch (Exception ex)
        {
            return new GlobalSearchResult(false, ex.Message, 0, 0, 0, 0, algorithm, 0, "Error");
        }
    }
}
