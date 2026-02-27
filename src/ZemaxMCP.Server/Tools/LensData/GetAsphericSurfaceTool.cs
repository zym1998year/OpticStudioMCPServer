using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZemaxMCP.Server.Tools.Base;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class GetAsphericSurfaceTool
{
    private readonly IZemaxSession _session;

    public GetAsphericSurfaceTool(IZemaxSession session) => _session = session;

    public record AsphericCoefficient(
        int Order,
        string Name,
        double Value,
        bool IsVariable
    );

    public record AsphericSurfaceData(
        bool Success,
        string? Error,
        int SurfaceNumber,
        string SurfaceType,
        bool IsAspheric,
        string Comment,
        double Radius,
        string RadiusSolve,
        double Thickness,
        string ThicknessSolve,
        string? Material,
        double SemiDiameter,
        double Conic,
        string ConicSolve,
        List<AsphericCoefficient> Coefficients
    );

    [McpServerTool(Name = "zemax_get_aspheric_surface")]
    [Description("Get detailed data for an aspheric surface including all Even Asphere coefficients (α₁ through α₈). Works on any surface - returns IsAspheric=false for non-aspheric surfaces.")]
    public async Task<AsphericSurfaceData> ExecuteAsync(
        [Description("Surface number to read (0 for object, -1 for image)")] int surfaceNumber)
    {
        try
        {
            return await _session.ExecuteAsync("GetAsphericSurface",
                new Dictionary<string, object?> { ["surfaceNumber"] = surfaceNumber },
                system =>
            {
                var lde = system.LDE;
                var surfNum = surfaceNumber;

                if (surfaceNumber == -1)
                {
                    surfNum = lde.NumberOfSurfaces - 1;
                }

                if (surfNum < 0 || surfNum >= lde.NumberOfSurfaces)
                {
                    throw new ArgumentException(
                        $"Invalid surface number: {surfaceNumber}. Valid range: 0-{lde.NumberOfSurfaces - 1}");
                }

                var surface = lde.GetSurfaceAt(surfNum);
                var isAspheric = surface.Type == SurfaceType.EvenAspheric;

                var coefficients = new List<AsphericCoefficient>();
                var orders = new[] { 2, 4, 6, 8, 10, 12, 14, 16 };
                var names = new[] { "α₁ (r²)", "α₂ (r⁴)", "α₃ (r⁶)", "α₄ (r⁸)",
                                    "α₅ (r¹⁰)", "α₆ (r¹²)", "α₇ (r¹⁴)", "α₈ (r¹⁶)" };

                // Read coefficients if surface is Even Asphere
                if (isAspheric)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        var cell = surface.GetSurfaceCell(SurfaceColumn.Par1 + i);
                        if (cell != null)
                        {
                            coefficients.Add(new AsphericCoefficient(
                                Order: orders[i],
                                Name: names[i],
                                Value: cell.DoubleValue,
                                IsVariable: cell.Solve == ZOSAPI.Editors.SolveType.Variable
                            ));
                        }
                    }
                }

                return new AsphericSurfaceData(
                    Success: true,
                    Error: null,
                    SurfaceNumber: surfNum,
                    SurfaceType: surface.Type.ToString(),
                    IsAspheric: isAspheric,
                    Comment: surface.Comment ?? "",
                    Radius: surface.Radius.SanitizeRadius(),
                    RadiusSolve: GetSystemDataTool.MapSolveType(surface.RadiusCell.Solve),
                    Thickness: surface.Thickness.Sanitize(),
                    ThicknessSolve: GetSystemDataTool.MapSolveType(surface.ThicknessCell.Solve),
                    Material: surface.Material,
                    SemiDiameter: surface.SemiDiameter.Sanitize(),
                    Conic: surface.Conic.Sanitize(),
                    ConicSolve: GetSystemDataTool.MapSolveType(surface.ConicCell.Solve),
                    Coefficients: coefficients
                );
            });
        }
        catch (Exception ex)
        {
            return new AsphericSurfaceData(
                Success: false,
                Error: ex.Message,
                SurfaceNumber: surfaceNumber,
                SurfaceType: "Unknown",
                IsAspheric: false,
                Comment: "",
                Radius: 0,
                RadiusSolve: "",
                Thickness: 0,
                ThicknessSolve: "",
                Material: null,
                SemiDiameter: 0,
                Conic: 0,
                ConicSolve: "",
                Coefficients: new List<AsphericCoefficient>()
            );
        }
    }
}
