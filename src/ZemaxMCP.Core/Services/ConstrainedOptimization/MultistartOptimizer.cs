using ZemaxMCP.Core.Models;
using ZOSAPI;

namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

public class MultistartOptimizer
{
    private readonly MeritFunctionReader _meritReader;

    public MultistartOptimizer(MeritFunctionReader meritReader)
    {
        _meritReader = meritReader ?? throw new ArgumentNullException(nameof(meritReader));
    }

    public MultistartResult Optimize(
        IOpticalSystem system,
        List<OptVariable> variables,
        List<MaterialInfo> substituteMaterials,
        int maxTrials = 100,
        int lmIterationsPerTrial = 50,
        int initialLmIterations = 200,
        double randomizationPercent = 5.0,
        double initialMu = 1e-3,
        double delta = 1e-7,
        bool useBroydenUpdate = true,
        int maxRestarts = 0,
        bool constrainedOnly = false,
        double glassSubstitutionProbability = 0.5,
        int progressInterval = 0,
        bool skipInitialLm = false,
        Action<int, double>? onImprovement = null,
        Action<int, int, double, int>? onProgress = null,
        Action? onInitialLmComplete = null,
        Action<int, int, double>? onInitialLmProgress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new MultistartResult();
        var rng = new Random();

        try
        {
            // Evaluate initial merit
            double initialMerit = system.MFE.CalculateMeritFunction();
            result.InitialMerit = initialMerit;

            var lmOptimizer = new LMOptimizer(_meritReader);
            double postLmMerit;

            if (skipInitialLm)
            {
                // Resume mode: skip initial LM, use current state as baseline
                postLmMerit = initialMerit;
            }
            else
            {
                // Phase 1: Run initial full LM optimization
                var lmResult = lmOptimizer.Optimize(
                    system, variables, initialLmIterations, initialMu, delta,
                    useBroydenUpdate: useBroydenUpdate, maxRestarts: maxRestarts,
                    onIterationProgress: onInitialLmProgress);
                postLmMerit = lmResult.FinalMerit;
            }

            result.PostInitialLmMerit = postLmMerit;
            onInitialLmComplete?.Invoke();

            cancellationToken.ThrowIfCancellationRequested();

            // Phase 2: Save baseline state
            var bestState = CaptureState(system, variables, substituteMaterials, postLmMerit);

            // Phase 3: Multistart trials
            int trialsAccepted = 0;
            double fraction = randomizationPercent / 100.0;

            for (int trial = 1; trial <= maxTrials; trial++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Randomize continuous variables within bounds
                RandomizeVariables(system, variables, bestState.VariableValues, fraction, rng, constrainedOnly);

                // Randomly substitute glass on one MaterialSubstitute surface (based on probability)
                if (substituteMaterials.Count > 0 && rng.NextDouble() < glassSubstitutionProbability)
                {
                    RandomizeOneGlass(system, substituteMaterials, rng);
                }

                // Run short LM re-optimization
                var trialResult = lmOptimizer.Optimize(
                    system, variables, lmIterationsPerTrial, initialMu, delta,
                    useBroydenUpdate: useBroydenUpdate, maxRestarts: 0);

                if (trialResult.FinalMerit < bestState.Merit)
                {
                    bestState = CaptureState(system, variables, substituteMaterials, trialResult.FinalMerit);
                    trialsAccepted++;
                    onImprovement?.Invoke(trial, trialResult.FinalMerit);
                }
                else
                {
                    RestoreState(system, variables, substituteMaterials, bestState);
                    system.MFE.CalculateMeritFunction();
                }

                // Progress reporting via callback
                onProgress?.Invoke(trial, maxTrials, bestState.Merit, trialsAccepted);

                // Legacy stderr progress reporting
                if (progressInterval > 0 && trial % progressInterval == 0)
                {
                    Console.Error.WriteLine($"[Multistart] Trial {trial}/{maxTrials} | Best merit: {bestState.Merit:F6} | Accepted: {trialsAccepted}");
                }
            }

            result.TrialsRun = maxTrials;
            result.TrialsAccepted = trialsAccepted;
            result.FinalMerit = bestState.Merit;
            result.SubstituteMaterialsFound = substituteMaterials.Count;
            result.Success = true;
            var glassInfo = substituteMaterials.Count > 0
                ? $" Glass substitution active on {substituteMaterials.Count} surface(s)."
                : " No MaterialSubstitute surfaces found.";
            result.Message = $"Multistart optimization completed. {trialsAccepted}/{maxTrials} trials accepted.{glassInfo} " +
                             $"Merit: {result.InitialMerit:F6} -> {result.PostInitialLmMerit:F6} (initial LM) -> {result.FinalMerit:F6} (multistart)";
        }
        catch (OperationCanceledException)
        {
            // Cancelled by user - restore best state and report partial results
            RestoreBestEffort(system, variables, substituteMaterials, result);
            result.Success = true;
            result.Message = $"Multistart optimization cancelled after trial {result.TrialsRun}. " +
                             $"Merit: {result.InitialMerit:F6} -> {result.FinalMerit:F6}";
        }
        catch (Exception ex)
        {
            RestoreBestEffort(system, variables, substituteMaterials, result);
            result.Success = false;
            result.Message = $"Multistart optimization failed: {ex.Message}";
        }

        return result;
    }

    private void RandomizeVariables(IOpticalSystem system, List<OptVariable> variables,
        double[] bestValues, double fraction, Random rng, bool constrainedOnly)
    {
        for (int i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            double bestVal = bestValues[i];
            double range;

            if (v.Constraint == ConstraintType.Unconstrained)
            {
                // Skip unconstrained variables if constrainedOnly is set
                if (constrainedOnly) continue;

                // For unconstrained variables, use ±fraction of current value
                range = Math.Abs(bestVal) * fraction;
                if (range < 1e-10) range = fraction; // fallback for zero values
            }
            else
            {
                // Use fraction of the bound range
                range = (v.UpperBound - v.LowerBound) * fraction;
            }

            double offset = (rng.NextDouble() * 2.0 - 1.0) * range;
            double newVal = bestVal + offset;

            // Clamp to bounds
            newVal = Math.Max(v.LowerBound, Math.Min(v.UpperBound, newVal));

            ZosVariableAccessor.SetVariableValue(system, v, newVal);
            v.Value = newVal;
        }
    }

    private static void RandomizeOneGlass(IOpticalSystem system, List<MaterialInfo> materials, Random rng)
    {
        // Filter to eligible surfaces
        var eligible = new List<MaterialInfo>();
        foreach (var mat in materials)
        {
            if (mat.SolveType == ZOSAPI.Editors.SolveType.MaterialSubstitute
                && mat.SubstituteGlasses != null && mat.SubstituteGlasses.Length > 1)
            {
                eligible.Add(mat);
            }
        }

        if (eligible.Count == 0)
            return;

        // Pick one random surface to substitute
        var chosen = eligible[rng.Next(eligible.Count)];
        string currentGlass = ZosVariableAccessor.GetGlassMaterial(system, chosen.SurfaceIndex);
        string? newGlass = PickRandomDifferentGlass(rng, chosen.SubstituteGlasses, currentGlass);
        if (newGlass != null)
        {
            ZosVariableAccessor.SetGlassMaterial(system, chosen.SurfaceIndex, newGlass);
        }
    }

    private static string? PickRandomDifferentGlass(Random rng, string[] glasses, string currentGlass)
    {
        if (glasses.Length <= 1)
            return null;

        for (int i = 0; i < 10; i++)
        {
            string candidate = glasses[rng.Next(glasses.Length)];
            if (!string.Equals(candidate, currentGlass, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        for (int i = 0; i < glasses.Length; i++)
        {
            if (!string.Equals(glasses[i], currentGlass, StringComparison.OrdinalIgnoreCase))
                return glasses[i];
        }

        return null;
    }

    private static DesignState CaptureState(IOpticalSystem system, List<OptVariable> variables,
        List<MaterialInfo> substituteMaterials, double merit)
    {
        var state = new DesignState
        {
            Merit = merit,
            VariableValues = new double[variables.Count],
            GlassAssignments = new string[substituteMaterials.Count]
        };

        for (int i = 0; i < variables.Count; i++)
            state.VariableValues[i] = ZosVariableAccessor.GetVariableValue(system, variables[i]);

        for (int i = 0; i < substituteMaterials.Count; i++)
            state.GlassAssignments[i] = ZosVariableAccessor.GetGlassMaterial(system, substituteMaterials[i].SurfaceIndex);

        return state;
    }

    private static void RestoreState(IOpticalSystem system, List<OptVariable> variables,
        List<MaterialInfo> substituteMaterials, DesignState state)
    {
        for (int i = 0; i < substituteMaterials.Count; i++)
            ZosVariableAccessor.SetGlassMaterial(system, substituteMaterials[i].SurfaceIndex, state.GlassAssignments[i]);

        for (int i = 0; i < variables.Count; i++)
        {
            ZosVariableAccessor.SetVariableValue(system, variables[i], state.VariableValues[i]);
            variables[i].Value = state.VariableValues[i];
        }
    }

    private static void RestoreBestEffort(IOpticalSystem system, List<OptVariable> variables,
        List<MaterialInfo> substituteMaterials, MultistartResult result)
    {
        try
        {
            double merit = system.MFE.CalculateMeritFunction();
            result.FinalMerit = merit;
        }
        catch { /* best effort */ }
    }

    private class DesignState
    {
        public double[] VariableValues { get; set; } = Array.Empty<double>();
        public string[] GlassAssignments { get; set; } = Array.Empty<string>();
        public double Merit { get; set; }
    }
}
