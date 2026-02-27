namespace ZemaxMCP.Core.Models;

public record LateralColorData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string Units { get; init; } = "";
    public double MaximumFieldDeg { get; init; }
    public double ShortWavelength { get; init; }
    public double LongWavelength { get; init; }
    public double[]? RelativeFields { get; init; }
    public double[]? LateralColor { get; init; }
    public int DataPoints { get; init; }
}

public record LongitudinalAberrationData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string Units { get; init; } = "";
    public double[]? Wavelengths { get; init; }
    public double[]? RelativePupils { get; init; }
    public double[][]? Aberrations { get; init; }
    public int DataPoints { get; init; }
}

public record ChromaticFocalShiftData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string WavelengthUnits { get; init; } = "";
    public string ShiftUnits { get; init; } = "";
    public double PupilZone { get; init; }
    public double MaximumFocalShiftRange { get; init; }
    public double DiffractionLimitedRange { get; init; }
    public double[]? Wavelengths { get; init; }
    public double[]? Shifts { get; init; }
    public int DataPoints { get; init; }
}

public record FieldCurvatureDistortionData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string DistortionType { get; init; } = "";
    public string ShiftUnits { get; init; } = "";
    public string HeightUnits { get; init; } = "";
    public string DistortionUnits { get; init; } = "";
    public double MaximumFieldDeg { get; init; }
    public double MaximumDistortionPercent { get; init; }
    public FieldCurvatureWavelengthData[]? WavelengthData { get; init; }
}

public record FieldCurvatureWavelengthData
{
    public double Wavelength { get; init; }
    public double DistortionFocalLength { get; init; }
    public double[]? FieldAnglesDeg { get; init; }
    public double[]? TangentialShift { get; init; }
    public double[]? SagittalShift { get; init; }
    public double[]? RealHeight { get; init; }
    public double[]? ReferenceHeight { get; init; }
    public double[]? DistortionPercent { get; init; }
    public int DataPoints { get; init; }
}

public record FftMtfVsFieldData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public double MaximumFieldDeg { get; init; }
    public string WavelengthRange { get; init; } = "";
    public FftMtfVsFieldFrequencyData[]? FrequencyData { get; init; }
}

public record FftMtfVsFieldFrequencyData
{
    public double SpatialFrequency { get; init; }
    public double[]? RelativeFields { get; init; }
    public double[]? Tangential { get; init; }
    public double[]? Sagittal { get; init; }
    public int DataPoints { get; init; }
}

public record DiffractionEncircledEnergyData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string Surface { get; init; } = "";
    public string Wavelength { get; init; } = "";
    public string Reference { get; init; } = "";
    public string DistanceUnits { get; init; } = "";
    public DiffractionEncircledEnergyFieldData[]? Fields { get; init; }
}

public record DiffractionEncircledEnergyFieldData
{
    public string Label { get; init; } = "";
    public double FieldValueDeg { get; init; }
    public double ReferenceX { get; init; }
    public double ReferenceY { get; init; }
    public double[]? RadialDistances { get; init; }
    public double[]? Fractions { get; init; }
    public int DataPoints { get; init; }
}

public record GeometricEncircledEnergyData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string Surface { get; init; } = "";
    public string Wavelength { get; init; } = "";
    public string Reference { get; init; } = "";
    public string DistanceUnits { get; init; } = "";
    public bool ScaledByDiffractionLimit { get; init; }
    public GeometricEncircledEnergyFieldData[]? Fields { get; init; }
}

public record GeometricEncircledEnergyFieldData
{
    public string Label { get; init; } = "";
    public double FieldValueDeg { get; init; }
    public double ReferenceX { get; init; }
    public double ReferenceY { get; init; }
    public double[]? RadialDistances { get; init; }
    public double[]? Fractions { get; init; }
    public int DataPoints { get; init; }
}

public record RelativeIlluminationData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public double Wavelength { get; init; }
    public string FieldUnits { get; init; } = "";
    public double[]? FieldValues { get; init; }
    public double[]? RelativeIllumination { get; init; }
    public double[]? EffectiveFNumber { get; init; }
    public int DataPoints { get; init; }
}

public record RayFanData
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string Units { get; init; } = "";
    public string Surface { get; init; } = "";
    public RayFanFieldData[]? Fields { get; init; }
}

public record RayFanFieldData
{
    public int FieldNumber { get; init; }
    public double FieldValueDeg { get; init; }
    public RayFanCurveData? Tangential { get; init; }
    public RayFanCurveData? Sagittal { get; init; }
}

public record RayFanCurveData
{
    public double[]? Wavelengths { get; init; }
    public double[]? PupilCoordinates { get; init; }
    /// <summary>Aberration[wavelengthIndex][pupilIndex]</summary>
    public double[][]? Aberration { get; init; }
    public int DataPoints { get; init; }
}
