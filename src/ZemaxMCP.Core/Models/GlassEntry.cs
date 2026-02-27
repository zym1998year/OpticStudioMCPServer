namespace ZemaxMCP.Core.Models;

public class GlassEntry
{
    public string Name { get; set; } = "";
    public string CatalogName { get; set; } = "";
    public int DispersionFormula { get; set; }
    public double Nd { get; set; }
    public double Vd { get; set; }
    public int Status { get; set; }
    public int MeltFrequency { get; set; } = -1;
    public double[] DispersionCoefficients { get; set; } = [];
    public double DPgF { get; set; }

    // ED line
    public double TCE { get; set; } = -1;
    public double TCE2 { get; set; } = -1;
    public double Density { get; set; } = -1;

    // TD line (7 thermal dispersion coefficients)
    public double[]? ThermalCoefficients { get; set; }

    // LD line
    public double MinWavelength { get; set; } = -1;
    public double MaxWavelength { get; set; } = -1;

    // OD line
    public double CR { get; set; } = -1;
    public double FR { get; set; } = -1;
    public double SR { get; set; } = -1;
    public double AR { get; set; } = -1;
    public double PR { get; set; } = -1;

    // Relative cost
    public double RelativeCost { get; set; } = -1;

    // GC line
    public string? Comment { get; set; }

    // Raw AGF lines for lossless re-export
    public List<string> RawLines { get; set; } = [];

    public string StatusText => Status switch
    {
        0 => "Standard",
        1 => "Preferred",
        2 => "Obsolete",
        3 => "Special",
        4 => "Melt",
        _ => "Unknown"
    };
}
