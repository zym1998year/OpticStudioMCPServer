using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class RemoveSurfaceTool
{
    private readonly IZemaxSession _session;

    public RemoveSurfaceTool(IZemaxSession session) => _session = session;

    public record RemoveSurfaceResult(
        bool Success,
        string? Error = null,
        int RemovedSurfaceNumber = 0,
        int TotalSurfaces = 0);

    [McpServerTool(Name = "zemax_remove_surface")]
    [Description(
        "Delete a surface from the Lens Data Editor at the specified surface number. "
        + "Object (0) and Image (last) surfaces cannot be deleted. "
        + "Useful to undo an accidental insert without rebuilding the system.")]
    public async Task<RemoveSurfaceResult> ExecuteAsync(
        [Description("Surface number to remove (cannot be 0 Object or last Image)")] int surfaceNumber)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber
            };

            return await _session.ExecuteAsync("RemoveSurface", parameters, system =>
            {
                var lde = system.LDE;
                int totalBefore = lde.NumberOfSurfaces;

                if (surfaceNumber < 0 || surfaceNumber >= totalBefore)
                    return new RemoveSurfaceResult(false,
                        Error: $"Invalid surface number: {surfaceNumber}. Valid range: 1-{totalBefore - 2} (0=Object, {totalBefore - 1}=Image not removable).");

                if (surfaceNumber == 0)
                    return new RemoveSurfaceResult(false,
                        Error: "Object surface (0) cannot be removed.");

                if (surfaceNumber == totalBefore - 1)
                    return new RemoveSurfaceResult(false,
                        Error: $"Image surface ({surfaceNumber}) cannot be removed.");

                bool ok = lde.RemoveSurfaceAt(surfaceNumber);
                int totalAfter = lde.NumberOfSurfaces;

                if (!ok || totalAfter != totalBefore - 1)
                    return new RemoveSurfaceResult(false,
                        Error: $"RemoveSurfaceAt returned false or surface count unchanged (before={totalBefore}, after={totalAfter}).",
                        RemovedSurfaceNumber: surfaceNumber,
                        TotalSurfaces: totalAfter);

                return new RemoveSurfaceResult(
                    Success: true,
                    RemovedSurfaceNumber: surfaceNumber,
                    TotalSurfaces: totalAfter);
            });
        }
        catch (Exception ex)
        {
            return new RemoveSurfaceResult(false, Error: ex.Message);
        }
    }
}
