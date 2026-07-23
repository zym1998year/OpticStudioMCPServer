using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class GetGlobalMatrixTool
{
    private readonly IZemaxSession _session;
    public GetGlobalMatrixTool(IZemaxSession session) => _session = session;
    public record Result(bool Success, string? Error = null, int SurfaceNumber = 0, double[][]? Rotation = null, double[]? Origin = null);

    [McpServerTool(Name = "zemax_get_global_matrix")]
    [Description("Get a sequential surface local-to-global rotation matrix and global vertex origin.")]
    public async Task<Result> ExecuteAsync([Description("Surface number")] int surfaceNumber)
    {
        try { return await _session.ExecuteAsync("GetGlobalMatrix", new Dictionary<string, object?> { ["surfaceNumber"] = surfaceNumber }, system =>
        {
            var lde = system.LDE;
            if (surfaceNumber < 0 || surfaceNumber >= lde.NumberOfSurfaces) return new Result(false, $"Invalid surface number: {surfaceNumber}.", surfaceNumber);
            if (!lde.GetGlobalMatrix(surfaceNumber, out var r11, out var r12, out var r13, out var r21, out var r22, out var r23, out var r31, out var r32, out var r33, out var x, out var y, out var z))
                return new Result(false, "OpticStudio could not calculate the global matrix.", surfaceNumber);
            return new Result(true, SurfaceNumber: surfaceNumber, Rotation: new[] { new[] { r11,r12,r13 }, new[] { r21,r22,r23 }, new[] { r31,r32,r33 } }, Origin: new[] { x,y,z });
        }); }
        catch (Exception ex) { return new Result(false, ex.Message, surfaceNumber); }
    }
}
