namespace ZemaxMCP.Core.Models;

/// <summary>
/// Represents solve data for a surface property cell
/// </summary>
public record SolveData
{
    /// <summary>
    /// The solve type (Fixed, Variable, Pickup, MarginalRayHeight, etc.)
    /// </summary>
    public string SolveType { get; init; } = "Fixed";

    // Pickup parameters
    public int? PickupSurface { get; init; }
    public int? PickupColumn { get; init; }
    public double? ScaleFactor { get; init; }
    public double? Offset { get; init; }

    // Ray solve parameters
    public double? Height { get; init; }
    public double? PupilZone { get; init; }
    public int? Wavelength { get; init; }

    // Edge thickness parameters
    public double? Thickness { get; init; }
    public double? RadialHeight { get; init; }

    // Position solve
    public double? Position { get; init; }

    // F/# solve
    public double? FNumber { get; init; }

    // Center of curvature solve
    public int? ReferenceSurface { get; init; }

    // Material solve parameters
    public string? Catalog { get; init; }
    public string? MaterialName { get; init; }
    public double? IndexOffset { get; init; }
}

/// <summary>
/// Contains solve information for all properties of a surface
/// </summary>
public record SurfaceSolveInfo
{
    public int SurfaceNumber { get; init; }
    public SolveData? Radius { get; init; }
    public SolveData? Thickness { get; init; }
    public SolveData? Conic { get; init; }
    public SolveData? SemiDiameter { get; init; }
    public SolveData? Material { get; init; }
    public Dictionary<int, SolveData> Parameters { get; init; } = new();
}
