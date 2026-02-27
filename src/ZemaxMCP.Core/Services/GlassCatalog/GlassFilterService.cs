using ZemaxMCP.Core.Models;

namespace ZemaxMCP.Core.Services.GlassCatalog;

public record GlassFilterCriteria
{
    // Filter 1: Preferred Only
    public bool? PreferredOnly { get; init; }

    // Filter 2: Distance Radius (weighted Nd/Vd/dPgF distance)
    public double? DistanceRadius { get; init; }
    public double Wn { get; init; } = 1.0;
    public double Wa { get; init; } = 1E-04;
    public double Wp { get; init; } = 1E+02;
    public double NdTarget { get; init; } = 1.5168;
    public double VdTarget { get; init; } = 64.17;
    public double DPgFTarget { get; init; } = 0.0;

    // Filter 3: BK7 Relative Cost
    public double? MaxCost { get; init; }

    // Filter 4: Nd Range
    public double? NdMin { get; init; }
    public double? NdMax { get; init; }

    // Filter 5: Vd Range
    public double? VdMin { get; init; }
    public double? VdMax { get; init; }

    // Filter 6: DPgF Range
    public double? DPgFMin { get; init; }
    public double? DPgFMax { get; init; }

    // Filter 7: TCE Range
    public double? TCEMin { get; init; }
    public double? TCEMax { get; init; }

    // Filter 8/9: Wavelength Coverage
    public double? MinWavelengthCoverage { get; init; }
    public double? MaxWavelengthCoverage { get; init; }

    // Filter 10: Melt Frequency
    public int? MaxMeltFrequency { get; init; }
}

public static class GlassFilterService
{
    public static List<GlassEntry> Apply(IEnumerable<GlassEntry> glasses, GlassFilterCriteria criteria)
    {
        return glasses.Where(g => PassesAllFilters(g, criteria)).ToList();
    }

    private static bool PassesAllFilters(GlassEntry g, GlassFilterCriteria c)
    {
        // Filter 1: Preferred Only
        if (c.PreferredOnly == true && g.Status != 1)
            return false;

        // Filter 2: Distance Radius
        if (c.DistanceRadius.HasValue)
        {
            double dNd = g.Nd - c.NdTarget;
            double dVd = g.Vd - c.VdTarget;
            double dPgF = g.DPgF - c.DPgFTarget;
            double d = Math.Sqrt(c.Wn * dNd * dNd + c.Wa * dVd * dVd + c.Wp * dPgF * dPgF);
            if (d > c.DistanceRadius.Value)
                return false;
        }

        // Filter 3: BK7 Relative Cost
        if (c.MaxCost.HasValue)
        {
            if (g.RelativeCost <= 0 || g.RelativeCost > c.MaxCost.Value)
                return false;
        }

        // Filter 4: Nd Range
        if (c.NdMin.HasValue && g.Nd < c.NdMin.Value)
            return false;
        if (c.NdMax.HasValue && g.Nd > c.NdMax.Value)
            return false;

        // Filter 5: Vd Range
        if (c.VdMin.HasValue && g.Vd < c.VdMin.Value)
            return false;
        if (c.VdMax.HasValue && g.Vd > c.VdMax.Value)
            return false;

        // Filter 6: DPgF Range
        if (c.DPgFMin.HasValue && g.DPgF < c.DPgFMin.Value)
            return false;
        if (c.DPgFMax.HasValue && g.DPgF > c.DPgFMax.Value)
            return false;

        // Filter 7: TCE Range
        if (c.TCEMin.HasValue)
        {
            if (g.TCE < 0 || g.TCE < c.TCEMin.Value)
                return false;
        }
        if (c.TCEMax.HasValue)
        {
            if (g.TCE < 0 || g.TCE > c.TCEMax.Value)
                return false;
        }

        // Filter 8: Min Wavelength Coverage (glass must cover down to this value)
        if (c.MinWavelengthCoverage.HasValue)
        {
            if (g.MinWavelength < 0 || g.MinWavelength > c.MinWavelengthCoverage.Value)
                return false;
        }

        // Filter 9: Max Wavelength Coverage (glass must cover up to this value)
        if (c.MaxWavelengthCoverage.HasValue)
        {
            if (g.MaxWavelength < 0 || g.MaxWavelength < c.MaxWavelengthCoverage.Value)
                return false;
        }

        // Filter 10: Melt Frequency
        if (c.MaxMeltFrequency.HasValue)
        {
            if (g.MeltFrequency < 1 || g.MeltFrequency > c.MaxMeltFrequency.Value)
                return false;
        }

        return true;
    }
}
