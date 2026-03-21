using MathNet.Numerics.LinearAlgebra;
using ZemaxMCP.Core.Models;
using ZOSAPI;

namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

public class LMOptimizer
{
    private readonly MeritFunctionReader _meritReader;

    public LMOptimizer(MeritFunctionReader meritReader)
    {
        _meritReader = meritReader ?? throw new ArgumentNullException(nameof(meritReader));
    }

    public OptimizationResult Optimize(
        IOpticalSystem system,
        List<OptVariable> variables,
        int maxIterations = 200,
        double initialMu = 1e-3,
        double delta = 1e-7,
        double gradientTolerance = 1e-10,
        double stepTolerance = 1e-10,
        double functionTolerance = 1e-10,
        bool useBroydenUpdate = false,
        int maxRestarts = 0,
        Action<int, int, double>? onIterationProgress = null)
    {
        var result = new OptimizationResult();
        double[]? x = null;
        int nParams = variables.Count;
        int iterations = 0;
        int restarts = 0;
        double cost = 0;
        List<MeritRow>? meritRows = null;
        int effectiveMaxRestarts = useBroydenUpdate ? maxRestarts : 0;

        try
        {
            if (nParams == 0)
            {
                result.Success = false;
                result.Message = "No variables to optimize.";
                return result;
            }

            x = new double[nParams];
            double[] lower = new double[nParams];
            double[] upper = new double[nParams];

            for (int i = 0; i < nParams; i++)
            {
                x[i] = ZosVariableAccessor.GetVariableValue(system, variables[i]);
                variables[i].Value = x[i];
                variables[i].StartingValue = x[i];
                lower[i] = variables[i].LowerBound;
                upper[i] = variables[i].UpperBound;
            }

            // Compute initial residuals
            system.MFE.CalculateMeritFunction();
            meritRows = _meritReader.ReadMeritRows(system);
            int nResiduals = meritRows.Count;

            if (nResiduals == 0)
            {
                result.Success = false;
                result.Message = "No merit function rows with non-zero weight.";
                return result;
            }

            double[] residuals = ComputeResiduals(meritRows);
            cost = DotProduct(residuals, residuals);
            result.InitialMerit = Math.Sqrt(cost / SumWeights(meritRows));

            double mu = initialMu;

            bool runAgain = true;
            while (runAgain)
            {
                runAgain = false;
                double[,]? J = null;
                bool needFullJacobian = true;
                bool exitedEarly = false;

                int iterationsThisRun = 0;
                int remainingIterations = maxIterations - iterations;
                if (remainingIterations <= 0)
                    break;

                for (int iter = 0; iter < remainingIterations; iter++)
                {
                    iterations++;
                    iterationsThisRun++;

                    if (needFullJacobian)
                    {
                        J = ComputeJacobian(system, variables, x, residuals, nResiduals, nParams, delta);
                        needFullJacobian = false;
                    }

                    // Compute J^T * J and J^T * r
                    var JtJ = new double[nParams, nParams];
                    var Jtr = new double[nParams];
                    double gradNorm = 0;

                    for (int i = 0; i < nParams; i++)
                    {
                        for (int j = 0; j < nParams; j++)
                        {
                            double sum = 0;
                            for (int k = 0; k < nResiduals; k++)
                                sum += J![k, i] * J[k, j];
                            JtJ[i, j] = sum;
                        }

                        double rsum = 0;
                        for (int k = 0; k < nResiduals; k++)
                            rsum += J![k, i] * residuals[k];
                        Jtr[i] = rsum;
                        gradNorm += Jtr[i] * Jtr[i];
                    }
                    gradNorm = Math.Sqrt(gradNorm);

                    if (gradNorm < gradientTolerance)
                    {
                        exitedEarly = true;
                        break;
                    }

                    // Try to find a good step with adaptive damping
                    bool stepAccepted = false;
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        var A = Matrix<double>.Build.DenseOfArray(JtJ);
                        for (int i = 0; i < nParams; i++)
                            A[i, i] += mu * Math.Max(JtJ[i, i], 1e-6);
                        var b = Vector<double>.Build.DenseOfArray(Jtr);

                        Vector<double> step;
                        try { step = A.Solve(-b); }
                        catch { mu *= 10; continue; }

                        // Check step size convergence
                        double stepNorm = 0;
                        double xNorm = 0;
                        for (int i = 0; i < nParams; i++)
                        {
                            stepNorm += step[i] * step[i];
                            xNorm += x[i] * x[i];
                        }
                        if (Math.Sqrt(stepNorm) < stepTolerance * (Math.Sqrt(xNorm) + stepTolerance))
                        {
                            exitedEarly = true;
                            stepAccepted = true;
                            break;
                        }

                        // Apply step with bounds clamping
                        var xNew = new double[nParams];
                        for (int i = 0; i < nParams; i++)
                            xNew[i] = Math.Max(lower[i], Math.Min(upper[i], x[i] + step[i]));

                        for (int i = 0; i < nParams; i++)
                            ZosVariableAccessor.SetVariableValue(system, variables[i], xNew[i]);

                        system.MFE.CalculateMeritFunction();
                        var newRows = _meritReader.ReadMeritRows(system);
                        double[] newResiduals = ComputeResiduals(newRows);
                        double newCost = DotProduct(newResiduals, newResiduals);

                        if (newCost < cost)
                        {
                            // Broyden rank-1 update
                            if (useBroydenUpdate)
                            {
                                double[] dx = new double[nParams];
                                for (int i = 0; i < nParams; i++)
                                    dx[i] = xNew[i] - x[i];

                                double dxTdx = DotProduct(dx, dx);
                                if (dxTdx > 0)
                                {
                                    double[] dr = new double[nResiduals];
                                    for (int i = 0; i < nResiduals; i++)
                                    {
                                        double Jdx = 0;
                                        for (int p = 0; p < nParams; p++)
                                            Jdx += J![i, p] * dx[p];
                                        dr[i] = (newResiduals[i] - residuals[i]) - Jdx;
                                    }

                                    for (int i = 0; i < nResiduals; i++)
                                        for (int p = 0; p < nParams; p++)
                                            J![i, p] += dr[i] * dx[p] / dxTdx;
                                }
                            }
                            else
                            {
                                needFullJacobian = true;
                            }

                            x = xNew;
                            residuals = newResiduals;
                            meritRows = newRows;

                            if (Math.Abs(cost - newCost) < functionTolerance * cost && iterationsThisRun > 1)
                            {
                                cost = newCost;
                                exitedEarly = true;
                                stepAccepted = true;
                                break;
                            }

                            cost = newCost;
                            mu *= 0.3333;
                            mu = Math.Max(mu, 1e-15);
                            stepAccepted = true;

                            // Report iteration progress
                            double iterMerit = SumWeights(newRows) > 0 ? Math.Sqrt(cost / SumWeights(newRows)) : 0;
                            onIterationProgress?.Invoke(iterations, maxIterations, iterMerit);

                            break;
                        }
                        else
                        {
                            mu *= 3.0;
                            mu = Math.Min(mu, 1e15);

                            for (int i = 0; i < nParams; i++)
                                ZosVariableAccessor.SetVariableValue(system, variables[i], x[i]);
                        }
                    }

                    if (!stepAccepted || exitedEarly)
                    {
                        exitedEarly = true;
                        break;
                    }
                }

                // Auto-restart with fresh Jacobian if Broyden exited early
                if (exitedEarly && restarts < effectiveMaxRestarts && iterations < maxIterations)
                {
                    restarts++;
                    mu = initialMu;
                    runAgain = true;
                }
            }

            // Push final values
            for (int i = 0; i < nParams; i++)
            {
                ZosVariableAccessor.SetVariableValue(system, variables[i], x[i]);
                variables[i].Value = x[i];
            }
            system.MFE.CalculateMeritFunction();

            double finalMerit = Math.Sqrt(cost / SumWeights(meritRows));
            result.FinalMerit = finalMerit;
            result.Iterations = iterations;
            result.Success = true;
            result.Restarts = restarts;
            result.Message = $"Optimization completed ({iterations} iter{(effectiveMaxRestarts > 0 ? $", {restarts} restart{(restarts != 1 ? "s" : "")}" : "")}). Merit: {result.InitialMerit:F6} -> {result.FinalMerit:F6}";
        }
        catch (Exception ex)
        {
            RestoreValues(system, variables, x, nParams);
            double sw = meritRows != null ? SumWeights(meritRows) : 1.0;
            result.FinalMerit = sw > 0 ? Math.Sqrt(cost / sw) : 0;
            result.Iterations = iterations;
            result.Restarts = restarts;
            result.Success = false;
            result.Message = $"Optimization failed ({iterations} iter): {ex.Message}. Merit: {result.InitialMerit:F6} -> {result.FinalMerit:F6}";
        }

        return result;
    }

    private double[,] ComputeJacobian(IOpticalSystem system, List<OptVariable> variables,
        double[] x, double[] residuals, int nResiduals, int nParams, double delta)
    {
        var J = new double[nResiduals, nParams];
        for (int p = 0; p < nParams; p++)
        {
            double orig = x[p];
            double h = Math.Max(delta, Math.Abs(orig) * delta);

            ZosVariableAccessor.SetVariableValue(system, variables[p], orig + h);
            system.MFE.CalculateMeritFunction();
            var perturbedRows = _meritReader.ReadMeritRows(system);
            double[] perturbedResiduals = ComputeResiduals(perturbedRows);

            for (int i = 0; i < nResiduals; i++)
                J[i, p] = (perturbedResiduals[i] - residuals[i]) / h;

            ZosVariableAccessor.SetVariableValue(system, variables[p], orig);
        }
        return J;
    }

    private static void RestoreValues(IOpticalSystem system, List<OptVariable> variables, double[]? x, int nParams)
    {
        if (x == null) return;
        try
        {
            for (int i = 0; i < nParams; i++)
            {
                ZosVariableAccessor.SetVariableValue(system, variables[i], x[i]);
                variables[i].Value = x[i];
            }
            system.MFE.CalculateMeritFunction();
        }
        catch { /* best effort */ }
    }

    private static double[] ComputeResiduals(List<MeritRow> rows)
    {
        var residuals = new double[rows.Count];
        for (int i = 0; i < rows.Count; i++)
            residuals[i] = Math.Sqrt(rows[i].Weight) * (rows[i].Value - rows[i].Target);
        return residuals;
    }

    private static double SumWeights(List<MeritRow> rows)
    {
        double sum = 0;
        for (int i = 0; i < rows.Count; i++)
            sum += rows[i].Weight;
        return sum > 0 ? sum : 1.0;
    }

    private static double DotProduct(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }
}
