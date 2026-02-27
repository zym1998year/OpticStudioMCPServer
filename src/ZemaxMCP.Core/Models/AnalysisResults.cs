namespace ZemaxMCP.Core.Models;

public record SpotDiagramData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public double RmsSpotSizeX { get; init; }
    public double RmsSpotSizeY { get; init; }
    public double RmsSpotRadius { get; init; }
    public double GeoSpotSizeX { get; init; }
    public double GeoSpotSizeY { get; init; }
    public double GeoSpotRadius { get; init; }
    public double CentroidX { get; init; }
    public double CentroidY { get; init; }
    public double AiryRadius { get; init; }
    public int Field { get; init; }
    public int Wavelength { get; init; }
    public string DataDescription { get; init; } = "";
}

public record MtfFieldData
{
    public string FieldLabel { get; init; } = "";
    public int FieldNumber { get; init; }
    public double[]? Frequencies { get; init; }
    public double[]? TangentialMtf { get; init; }
    public double[]? SagittalMtf { get; init; }
    public int DataPoints { get; init; }
}

public record MtfData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public MtfFieldData[]? Fields { get; init; }
    public double[]? DiffractionLimitFrequencies { get; init; }
    public double[]? DiffractionLimitTangential { get; init; }
    public double[]? DiffractionLimitSagittal { get; init; }
    public double MaxFrequency { get; init; }
    public int Wavelength { get; init; }
    public int TotalFields { get; init; }
    public int DataPoints { get; init; }
}

public record RayTraceResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double L { get; init; }  // direction cosine x
    public double M { get; init; }  // direction cosine y
    public double N { get; init; }  // direction cosine z
    public double OpticalPathLength { get; init; }
    public int SurfaceNumber { get; init; }
    public bool RayValid { get; init; }
}

public record WavefrontData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public double PeakToValley { get; init; }
    public double Rms { get; init; }
    public double Strehl { get; init; }
    public int Field { get; init; }
    public int Wavelength { get; init; }
}

public record SeidelAberrations
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public double SphericalW040 { get; init; }
    public double ComaW131 { get; init; }
    public double AstigmatismW222 { get; init; }
    public double FieldCurvatureW220 { get; init; }
    public double DistortionW311 { get; init; }
    public double ChromaticLateral { get; init; }
    public double ChromaticAxial { get; init; }
    public int Surface { get; init; }
    public int Wavelength { get; init; }
}

public record LayoutData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? FilePath { get; init; }
    public string? Message { get; init; }
}

public record CardinalPoints
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public double EffectiveFocalLength { get; init; }
    public double BackFocalLength { get; init; }
    public double FrontFocalLength { get; init; }
    public double EntrancePupilPosition { get; init; }
    public double EntrancePupilDiameter { get; init; }
    public double ExitPupilPosition { get; init; }
    public double ExitPupilDiameter { get; init; }
    public double ImageDistance { get; init; }
    public double ObjectDistance { get; init; }
    public double Magnification { get; init; }
    public int Wavelength { get; init; }
}
