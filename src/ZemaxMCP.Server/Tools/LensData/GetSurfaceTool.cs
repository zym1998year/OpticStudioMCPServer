using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;
using ZemaxMCP.Server.Tools.Base;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class GetSurfaceTool
{
    private readonly IZemaxSession _session;

    public GetSurfaceTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_get_surface")]
    [Description("Get detailed data for a specific surface")]
    public async Task<Surface> ExecuteAsync(
        [Description("Surface number (0 for object, -1 for image)")] int surfaceNumber)
    {
        return await _session.ExecuteAsync("GetSurface",
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

            var row = lde.GetSurfaceAt(surfNum);

            return new Surface
            {
                Number = surfNum,
                Comment = row.Comment,
                Radius = row.Radius.SanitizeRadius(),
                Thickness = row.Thickness.Sanitize(),
                Material = row.Material,
                SemiDiameter = row.SemiDiameter.Sanitize(),
                Conic = row.Conic.Sanitize(),
                SurfaceType = row.Type.ToString(),
                IsStop = row.IsStop
            };
        });
    }
}
