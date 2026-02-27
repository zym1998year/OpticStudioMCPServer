namespace ZemaxMCP.Core.Models;

public record LensSystem
{
    public string? FilePath { get; init; }
    public string Title { get; init; } = "";
    public string Notes { get; init; } = "";
    public int NumberOfSurfaces { get; init; }
    public List<Surface> Surfaces { get; init; } = new();
    public List<Field> Fields { get; init; } = new();
    public List<Wavelength> Wavelengths { get; init; } = new();
    public ApertureData Aperture { get; init; } = new();
    public string Units { get; init; } = "mm";
    public int NumberOfConfigurations { get; init; } = 1;
    public int CurrentConfiguration { get; init; } = 1;
}

public record Field
{
    public int Number { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Weight { get; init; } = 1.0;
    public double VDX { get; init; }
    public double VDY { get; init; }
    public double VCX { get; init; }
    public double VCY { get; init; }
}

public record Wavelength
{
    public int Number { get; init; }
    public double Value { get; init; }
    public double Weight { get; init; } = 1.0;
    public bool IsPrimary { get; init; }
}

public record ApertureData
{
    public ApertureType Type { get; init; }
    public double Value { get; init; }
}

public enum ApertureType
{
    EntrancePupilDiameter,
    ImageSpaceFNumber,
    ObjectSpaceNA,
    FloatByStopSize,
    ParaxialWorkingFNumber,
    ObjectConeSemiAngle
}
