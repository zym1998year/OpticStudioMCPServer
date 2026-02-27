using System;
using System.Collections.Generic;

namespace ZemaxMCP.Server.Tools.Optimization;

/// <summary>
/// Forbes 1988 Gaussian Quadrature pupil sampling parameters.
/// Reference: G.W. Forbes, "Optical system assessment for design: numerical ray tracing
/// in the Gaussian pupil", J. Opt. Soc. Am. A, Vol. 5, No. 11, November 1988, pp. 1943-1956.
///
/// Key insight: Sample in ρ = r²/R² (normalized squared radius) instead of r.
/// This dramatically improves efficiency because aberrations are polynomials in r² and r·cos(θ).
///
/// Efficiency comparison:
/// - Cartesian grid: ~500 rays for 1% accuracy
/// - Andersen's method: ~75 rays for 1% accuracy
/// - Forbes Gaussian in ρ: ~9 rays for 1% accuracy
/// </summary>
public static class ForbesPupilSampling
{
    /// <summary>
    /// Gaussian integration parameters for radial integral (Table 1 from Forbes 1988).
    /// Each entry contains (ρ^(1/2), weight) where ρ = r²/R².
    /// These are derived from zeros of Legendre polynomials.
    /// </summary>
    public static readonly Dictionary<int, (double[] RadialPositions, double[] Weights)> GaussianParameters = new()
    {
        // Nr = 1: 1 ring
        [1] = (
            new[] { 0.70710678 },
            new[] { 0.50000000 }
        ),

        // Nr = 2: 2 rings
        [2] = (
            new[] { 0.45970084, 0.88807383 },
            new[] { 0.25000000, 0.25000000 }
        ),

        // Nr = 3: 3 rings - yields ~1% accuracy with 9 rays (Nr=3, Nθ=3)
        [3] = (
            new[] { 0.33571069, 0.70710678, 0.94196515 },
            new[] { 0.13888889, 0.22222222, 0.13888889 }
        ),

        // Nr = 4: 4 rings
        [4] = (
            new[] { 0.26349923, 0.57446451, 0.81852949, 0.96465961 },
            new[] { 0.08696371, 0.16303629, 0.16303629, 0.08696371 }
        ),

        // Nr = 5: 5 rings
        [5] = (
            new[] { 0.21658734, 0.48038042, 0.70710678, 0.87706023, 0.97626324 },
            new[] { 0.05923172, 0.11965717, 0.14222222, 0.11965717, 0.05923172 }
        ),

        // Nr = 6: 6 rings - yields ~0.001% accuracy with 36 rays (Nr=6, Nθ=6)
        [6] = (
            new[] { 0.18375321, 0.41157661, 0.61700114, 0.78696226, 0.91137517, 0.98297241 },
            new[] { 0.04283112, 0.09019089, 0.11697848, 0.11697848, 0.09019039, 0.04283112 }
        )
    };

    /// <summary>
    /// Radau integration parameters (Table 2 from Forbes 1988).
    /// Includes center point (ρ=0) for cases where axial ray is needed.
    /// </summary>
    public static readonly Dictionary<int, (double[] RadialPositions, double[] Weights)> RadauParameters = new()
    {
        // Nr = 1: center + 1 ring (2 radial positions)
        [1] = (
            new[] { 0.00000000, 0.81649658 },
            new[] { 0.12500000, 0.37500000 }
        ),

        // Nr = 2: center + 2 rings (3 radial positions)
        [2] = (
            new[] { 0.00000000, 0.59586158, 0.91921106 },
            new[] { 0.05555556, 0.25624291, 0.18820153 }
        ),

        // Nr = 3: center + 3 rings (4 radial positions)
        [3] = (
            new[] { 0.00000000, 0.46080423, 0.76846154, 0.95467902 },
            new[] { 0.03125000, 0.16442216, 0.19409673, 0.11023111 }
        ),

        // Nr = 4: center + 4 rings (5 radial positions)
        [4] = (
            new[] { 0.00000000, 0.37384471, 0.64529805, 0.85038637, 0.97102822 },
            new[] { 0.02000000, 0.11155195, 0.15591326, 0.14067801, 0.07185678 }
        ),

        // Nr = 5: center + 5 rings (6 radial positions)
        [5] = (
            new[] { 0.00000000, 0.31390299, 0.55184756, 0.74968339, 0.89553704, 0.97989292 },
            new[] { 0.01388889, 0.07991019, 0.12134680, 0.13023170, 0.10422533, 0.05039710 }
        )
    };

    /// <summary>
    /// Represents a single pupil sample point with its integration weight.
    /// </summary>
    public record PupilSamplePoint(
        double Px,      // Normalized pupil x coordinate (-1 to 1)
        double Py,      // Normalized pupil y coordinate (-1 to 1)
        double Weight   // Integration weight for this sample point
    );

    /// <summary>
    /// Generates pupil sample points using Forbes' Gaussian quadrature method.
    /// </summary>
    /// <param name="rings">Number of radial rings (1-6)</param>
    /// <param name="arms">Number of angular samples per ring</param>
    /// <param name="useRadau">If true, includes center point (for axial fields)</param>
    /// <returns>List of pupil sample points with integration weights</returns>
    public static List<PupilSamplePoint> GenerateSamplePoints(int rings, int arms, bool useRadau = false)
    {
        rings = Math.Max(1, Math.Min(6, rings));
        arms = Math.Max(1, Math.Min(12, arms));

        var samples = new List<PupilSamplePoint>();

        // Get radial parameters
        var (radialPositions, radialWeights) = useRadau && RadauParameters.ContainsKey(rings)
            ? RadauParameters[rings]
            : GaussianParameters[rings];

        // Angular sampling: θk = (k - 0.5) * π / Nθ for k = 1 to Nθ
        // This is optimal for polynomials in cos(θ)
        for (int j = 0; j < radialPositions.Length; j++)
        {
            double rho = radialPositions[j];
            double radialWeight = radialWeights[j];

            if (rho < 1e-10)
            {
                // Center point (ρ = 0) - only one point needed
                samples.Add(new PupilSamplePoint(0.0, 0.0, radialWeight));
            }
            else
            {
                // Generate angular samples
                for (int k = 1; k <= arms; k++)
                {
                    double theta = (k - 0.5) * Math.PI / arms;
                    double px = rho * Math.Cos(theta);
                    double py = rho * Math.Sin(theta);

                    // Combined weight: radial weight * angular weight
                    // Angular weight for uniform sampling is 2π/Nθ, normalized to sum to 1
                    double angularWeight = 1.0 / arms;
                    double combinedWeight = radialWeight * angularWeight;

                    samples.Add(new PupilSamplePoint(px, py, combinedWeight));
                }
            }
        }

        return samples;
    }

    /// <summary>
    /// Generates pupil sample points exploiting Y-symmetry (for fields with Hx=0).
    /// When the field is only off-axis in Y, OPD(Px, Py) = OPD(-Px, Py), so we only
    /// need to sample the half-pupil with Px >= 0 and double the weights.
    /// Reference: Forbes 1988, Section 3.B on symmetry exploitation.
    /// </summary>
    /// <param name="rings">Number of radial rings (1-6)</param>
    /// <param name="arms">Number of angular samples per half-ring (will sample 0 to π/2)</param>
    /// <param name="useRadau">If true, includes center point</param>
    /// <returns>List of pupil sample points with integration weights (doubled for symmetry)</returns>
    public static List<PupilSamplePoint> GenerateSymmetricSamplePoints(int rings, int arms, bool useRadau = false)
    {
        rings = Math.Max(1, Math.Min(6, rings));
        arms = Math.Max(1, Math.Min(12, arms));

        var samples = new List<PupilSamplePoint>();

        // Get radial parameters
        var (radialPositions, radialWeights) = useRadau && RadauParameters.ContainsKey(rings)
            ? RadauParameters[rings]
            : GaussianParameters[rings];

        // For Y-symmetric fields (Hx=0), we sample only θ in [0, π/2] (Px >= 0, Py >= 0)
        // and double the weights to account for the symmetric half
        for (int j = 0; j < radialPositions.Length; j++)
        {
            double rho = radialPositions[j];
            double radialWeight = radialWeights[j];

            if (rho < 1e-10)
            {
                // Center point (ρ = 0) - only one point needed, no symmetry factor
                samples.Add(new PupilSamplePoint(0.0, 0.0, radialWeight));
            }
            else
            {
                // Generate angular samples in first quadrant only (θ = 0 to π/2)
                // θk = (k - 0.5) * (π/2) / Nθ for k = 1 to Nθ
                for (int k = 1; k <= arms; k++)
                {
                    double theta = (k - 0.5) * Math.PI / (2.0 * arms);
                    double px = rho * Math.Cos(theta);
                    double py = rho * Math.Sin(theta);

                    // Weight is doubled to account for symmetric point at (-Px, Py)
                    double angularWeight = 2.0 / arms;  // Factor of 2 for symmetry
                    double combinedWeight = radialWeight * angularWeight;

                    samples.Add(new PupilSamplePoint(px, py, combinedWeight));
                }
            }
        }

        return samples;
    }

    /// <summary>
    /// Generates pupil sample points for on-axis field (rotationally symmetric).
    /// Only meridional rays are needed for axial field.
    /// </summary>
    public static List<PupilSamplePoint> GenerateAxialSamplePoints(int rings, bool useRadau = false)
    {
        rings = Math.Max(1, Math.Min(6, rings));

        var samples = new List<PupilSamplePoint>();

        var (radialPositions, radialWeights) = useRadau && RadauParameters.ContainsKey(rings)
            ? RadauParameters[rings]
            : GaussianParameters[rings];

        for (int j = 0; j < radialPositions.Length; j++)
        {
            double rho = radialPositions[j];
            double weight = radialWeights[j];

            // For axial field, only need rays along one meridian (Px = ρ, Py = 0)
            samples.Add(new PupilSamplePoint(rho, 0.0, weight));
        }

        return samples;
    }

    /// <summary>
    /// Normalizes weights so they sum to 1.0 for a given set of sample points.
    /// This ensures proper averaging when computing RMS values.
    /// </summary>
    public static void NormalizeWeights(List<PupilSamplePoint> samples)
    {
        double totalWeight = 0;
        foreach (var s in samples)
            totalWeight += s.Weight;

        if (totalWeight > 0)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                samples[i] = s with { Weight = s.Weight / totalWeight };
            }
        }
    }

    /// <summary>
    /// Gets the recommended configuration for a given accuracy target.
    /// </summary>
    /// <param name="targetAccuracyPercent">Desired accuracy (e.g., 1.0 for 1%)</param>
    /// <returns>Recommended (rings, arms) configuration</returns>
    public static (int Rings, int Arms) GetRecommendedConfiguration(double targetAccuracyPercent)
    {
        // From Forbes 1988 Figure 6:
        // - Nr=3, Nθ=3 (9 rays): ~1% accuracy
        // - Nr=4, Nθ=4 (16 rays): ~0.1% accuracy
        // - Nr=5, Nθ=6 (30 rays): ~0.01% accuracy

        if (targetAccuracyPercent >= 10.0)
            return (2, 3);  // 6 rays, ~10% accuracy
        else if (targetAccuracyPercent >= 1.0)
            return (3, 3);  // 9 rays, ~1% accuracy
        else if (targetAccuracyPercent >= 0.1)
            return (4, 4);  // 16 rays, ~0.1% accuracy
        else if (targetAccuracyPercent >= 0.01)
            return (5, 6);  // 30 rays, ~0.01% accuracy
        else
            return (6, 6);  // 36 rays, ~0.001% accuracy
    }
}
