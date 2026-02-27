using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetAsphericSurfaceTool
{
    private readonly IZemaxSession _session;

    public SetAsphericSurfaceTool(IZemaxSession session) => _session = session;

    public record AsphericCoefficient(int Order, double Value, bool IsVariable);

    public record SetAsphericResult(
        bool Success,
        string? Error,
        int SurfaceNumber,
        string SurfaceType,
        double Radius,
        double Conic,
        string ConicSolve,
        List<AsphericCoefficient> Coefficients
    );

    [McpServerTool(Name = "zemax_set_aspheric_surface")]
    [Description("Convert a surface to Even Asphere type and set aspheric coefficients. The Even Asphere sag equation is: z = c*r²/(1+sqrt(1-(1+k)*c²*r²)) + α₁*r² + α₂*r⁴ + α₃*r⁶ + α₄*r⁸ + ... where c=1/R (curvature), k=conic, r=radial coordinate.")]
    public async Task<SetAsphericResult> ExecuteAsync(
        [Description("Surface number to modify")] int surfaceNumber,
        [Description("2nd order coefficient α₁ (r² term, usually 0)")] double? alpha1 = null,
        [Description("4th order coefficient α₂ (r⁴ term)")] double? alpha2 = null,
        [Description("6th order coefficient α₃ (r⁶ term)")] double? alpha3 = null,
        [Description("8th order coefficient α₄ (r⁸ term)")] double? alpha4 = null,
        [Description("10th order coefficient α₅ (r¹⁰ term)")] double? alpha5 = null,
        [Description("12th order coefficient α₆ (r¹² term)")] double? alpha6 = null,
        [Description("14th order coefficient α₇ (r¹⁴ term)")] double? alpha7 = null,
        [Description("16th order coefficient α₈ (r¹⁶ term)")] double? alpha8 = null,
        [Description("Make α₁ variable for optimization")] bool alpha1Variable = false,
        [Description("Make α₂ variable for optimization")] bool alpha2Variable = false,
        [Description("Make α₃ variable for optimization")] bool alpha3Variable = false,
        [Description("Make α₄ variable for optimization")] bool alpha4Variable = false,
        [Description("Make α₅ variable for optimization")] bool alpha5Variable = false,
        [Description("Make α₆ variable for optimization")] bool alpha6Variable = false,
        [Description("Make α₇ variable for optimization")] bool alpha7Variable = false,
        [Description("Make α₈ variable for optimization")] bool alpha8Variable = false,
        [Description("Set conic constant value")] double? conic = null,
        [Description("Make conic variable for optimization")] bool conicVariable = false)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber,
                ["conic"] = conic,
                ["conicVariable"] = conicVariable
            };

            var result = await _session.ExecuteAsync("SetAsphericSurface", parameters, system =>
            {
                var lde = system.LDE;

                if (surfaceNumber < 0 || surfaceNumber >= lde.NumberOfSurfaces)
                {
                    throw new ArgumentException(
                        $"Invalid surface number: {surfaceNumber}. " +
                        $"Valid range: 0-{lde.NumberOfSurfaces - 1}");
                }

                var surface = lde.GetSurfaceAt(surfaceNumber);

                // Change surface type to Even Asphere if not already
                if (surface.Type != SurfaceType.EvenAspheric)
                {
                    var evenAsphereType = surface.GetSurfaceTypeSettings(SurfaceType.EvenAspheric);
                    surface.ChangeType(evenAsphereType);
                }

                // Set conic if specified
                if (conic.HasValue)
                    surface.Conic = conic.Value;

                // Make conic variable if requested
                if (conicVariable)
                    surface.ConicCell.MakeSolveVariable();

                // Coefficient values and variable flags
                var coefficientValues = new double?[] { alpha1, alpha2, alpha3, alpha4, alpha5, alpha6, alpha7, alpha8 };
                var variableFlags = new[] { alpha1Variable, alpha2Variable, alpha3Variable, alpha4Variable,
                                            alpha5Variable, alpha6Variable, alpha7Variable, alpha8Variable };
                var orders = new[] { 2, 4, 6, 8, 10, 12, 14, 16 };

                var resultCoefficients = new List<AsphericCoefficient>();

                for (int i = 0; i < coefficientValues.Length; i++)
                {
                    var cell = surface.GetSurfaceCell(SurfaceColumn.Par1 + i);
                    if (cell != null)
                    {
                        // Set value if specified
                        if (coefficientValues[i].HasValue)
                        {
                            cell.DoubleValue = coefficientValues[i]!.Value;
                        }

                        // Make variable if requested
                        if (variableFlags[i])
                        {
                            cell.MakeSolveVariable();
                        }

                        // Record the coefficient info
                        resultCoefficients.Add(new AsphericCoefficient(
                            Order: orders[i],
                            Value: cell.DoubleValue,
                            IsVariable: variableFlags[i] || cell.Solve == ZOSAPI.Editors.SolveType.Variable
                        ));
                    }
                }

                return new SetAsphericResult(
                    Success: true,
                    Error: null,
                    SurfaceNumber: surfaceNumber,
                    SurfaceType: surface.Type.ToString(),
                    Radius: surface.Radius,
                    Conic: surface.Conic,
                    ConicSolve: GetSystemDataTool.MapSolveType(surface.ConicCell.Solve),
                    Coefficients: resultCoefficients
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SetAsphericResult(
                Success: false,
                Error: ex.Message,
                SurfaceNumber: surfaceNumber,
                SurfaceType: "Unknown",
                Radius: 0,
                Conic: 0,
                ConicSolve: "",
                Coefficients: new List<AsphericCoefficient>()
            );
        }
    }
}
