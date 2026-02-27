namespace ZemaxMCP.Core.Models;

public record SeidelCoefficientsData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public double Wavelength { get; init; }
    public double PetzvalRadius { get; init; }
    public double OpticalInvariant { get; init; }
    public double ChiefRaySlopeObject { get; init; }
    public double ChiefRaySlopeImage { get; init; }
    public double MarginalRaySlopeObject { get; init; }
    public double MarginalRaySlopeImage { get; init; }
    public SeidelSurfaceRow[]? SurfaceCoefficients { get; init; }
    public SeidelSurfaceRow? Total { get; init; }
    public SeidelWavefrontSummary? WavefrontSummary { get; init; }
}

public record SeidelSurfaceRow
{
    public string Surface { get; init; } = "";
    public double S1_SPHA { get; init; }
    public double S2_COMA { get; init; }
    public double S3_ASTI { get; init; }
    public double S4_FCUR { get; init; }
    public double S5_DIST { get; init; }
    public double CL_CLA { get; init; }
    public double CT_CTR { get; init; }
}

public record SeidelWavefrontSummary
{
    public double W040 { get; init; }
    public double W131 { get; init; }
    public double W222 { get; init; }
    public double W220P { get; init; }
    public double W311 { get; init; }
    public double W020 { get; init; }
    public double W111 { get; init; }
    public double W220S { get; init; }
    public double W220M { get; init; }
    public double W220T { get; init; }
}
