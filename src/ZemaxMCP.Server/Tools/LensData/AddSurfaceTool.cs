using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class AddSurfaceTool
{
    private readonly IZemaxSession _session;

    public AddSurfaceTool(IZemaxSession session) => _session = session;

    public record AddSurfaceResult(
        bool Success,
        string? Error,
        int SurfaceNumber,
        int TotalSurfaces
    );

    [McpServerTool(Name = "zemax_add_surface")]
    [Description("Add a new surface to the lens system")]
    public async Task<AddSurfaceResult> ExecuteAsync(
        [Description("Position to insert (0 to append before image)")] int insertAt = 0,
        [Description("Radius of curvature (0 for flat/infinite)")] double radius = 0,
        [Description("Thickness to next surface")] double thickness = 0,
        [Description("Material/glass name (empty for air)")] string? material = null,
        [Description("Surface comment")] string? comment = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["insertAt"] = insertAt,
                ["radius"] = radius,
                ["thickness"] = thickness,
                ["material"] = material,
                ["comment"] = comment
            };

            var result = await _session.ExecuteAsync("AddSurface", parameters, system =>
            {
                var lde = system.LDE;
                var insertPosition = insertAt > 0 ? insertAt : lde.NumberOfSurfaces - 1;

                // Insert new surface
                var newSurf = lde.InsertNewSurfaceAt(insertPosition);
                var surfNum = insertPosition;

                // Set properties (radius of 0 means flat/infinity in Zemax)
                if (radius != 0)
                    newSurf.Radius = radius;
                newSurf.Thickness = thickness;

                if (!string.IsNullOrEmpty(material))
                    newSurf.Material = material;

                if (!string.IsNullOrEmpty(comment))
                    newSurf.Comment = comment;

                return new AddSurfaceResult(
                    Success: true,
                    Error: null,
                    SurfaceNumber: surfNum,
                    TotalSurfaces: lde.NumberOfSurfaces
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new AddSurfaceResult(false, ex.Message, 0, 0);
        }
    }
}
