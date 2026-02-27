using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetSurfaceTool
{
    private readonly IZemaxSession _session;

    public SetSurfaceTool(IZemaxSession session) => _session = session;

    public record SetSurfaceResult(
        bool Success,
        string? Error,
        Surface UpdatedSurface
    );

    [McpServerTool(Name = "zemax_set_surface")]
    [Description("Modify properties of a surface in the lens data editor")]
    public async Task<SetSurfaceResult> ExecuteAsync(
        [Description("Surface number to modify")] int surfaceNumber,
        [Description("Radius of curvature")] double? radius = null,
        [Description("Thickness to next surface")] double? thickness = null,
        [Description("Material/glass name")] string? material = null,
        [Description("Semi-diameter")] double? semiDiameter = null,
        [Description("Conic constant")] double? conic = null,
        [Description("Surface comment")] string? comment = null,
        [Description("Set as stop surface")] bool? isStop = null,
        [Description("Make radius variable")] bool? radiusVariable = null,
        [Description("Make thickness variable")] bool? thicknessVariable = null,
        [Description("Make conic variable")] bool? conicVariable = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber,
                ["radius"] = radius,
                ["thickness"] = thickness,
                ["material"] = material,
                ["semiDiameter"] = semiDiameter,
                ["conic"] = conic,
                ["comment"] = comment,
                ["isStop"] = isStop,
                ["radiusVariable"] = radiusVariable,
                ["thicknessVariable"] = thicknessVariable,
                ["conicVariable"] = conicVariable
            };

            var result = await _session.ExecuteAsync("SetSurface", parameters, system =>
            {
                var lde = system.LDE;

                if (surfaceNumber < 0 || surfaceNumber >= lde.NumberOfSurfaces)
                {
                    throw new ArgumentException(
                        $"Invalid surface number: {surfaceNumber}. " +
                        $"Valid range: 0-{lde.NumberOfSurfaces - 1}");
                }

                var surface = lde.GetSurfaceAt(surfaceNumber);

                if (radius.HasValue)
                    surface.Radius = radius.Value;

                if (thickness.HasValue)
                    surface.Thickness = thickness.Value;

                if (!string.IsNullOrEmpty(material))
                    surface.Material = material;

                if (semiDiameter.HasValue)
                    surface.SemiDiameter = semiDiameter.Value;

                if (conic.HasValue)
                    surface.Conic = conic.Value;

                if (!string.IsNullOrEmpty(comment))
                    surface.Comment = comment;

                if (isStop.HasValue && isStop.Value)
                    surface.IsStop = true;

                // Set solve status for variables
                if (radiusVariable.HasValue && radiusVariable.Value)
                {
                    surface.RadiusCell.MakeSolveVariable();
                }

                if (thicknessVariable.HasValue && thicknessVariable.Value)
                {
                    surface.ThicknessCell.MakeSolveVariable();
                }

                if (conicVariable.HasValue && conicVariable.Value)
                {
                    surface.ConicCell.MakeSolveVariable();
                }

                return new SetSurfaceResult(
                    Success: true,
                    Error: null,
                    UpdatedSurface: new Surface
                    {
                        Number = surfaceNumber,
                        Comment = surface.Comment,
                        Radius = surface.Radius,
                        Thickness = surface.Thickness,
                        Material = surface.Material,
                        SemiDiameter = surface.SemiDiameter,
                        Conic = surface.Conic,
                        SurfaceType = surface.Type.ToString(),
                        IsStop = surface.IsStop
                    }
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SetSurfaceResult(
                Success: false,
                Error: ex.Message,
                UpdatedSurface: new Surface { Number = surfaceNumber }
            );
        }
    }
}
