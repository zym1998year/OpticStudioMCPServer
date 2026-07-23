using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Tools.RayTrace;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class RayTraceExtendedTool
{
    private readonly IZemaxSession _session;
    public RayTraceExtendedTool(IZemaxSession session) => _session = session;
    public record Result(bool Success, string? Error = null, double X = 0, double Y = 0, double Z = 0, double L = 0, double M = 0, double N = 0, double OpticalPathLength = 0, double Intensity = 0, int ErrorCode = 0, int VignetteCode = 0, int SurfaceNumber = 0, bool RayValid = false, bool RayClear = false);

    [McpServerTool(Name = "zemax_ray_trace_extended")]
    [Description("Trace a normalized real ray and return intercept, direction, intensity, error and vignette codes.")]
    public async Task<Result> ExecuteAsync(double hx = 0, double hy = 0, double px = 0, double py = 0, int wavelength = 1, int surface = 0)
    {
        try { return await _session.ExecuteAsync("RayTraceExtended", new Dictionary<string, object?> { ["hx"]=hx,["hy"]=hy,["px"]=px,["py"]=py,["wavelength"]=wavelength,["surface"]=surface }, system =>
        {
            var target = surface == 0 ? system.LDE.NumberOfSurfaces - 1 : surface;
            var ray = system.Tools.OpenBatchRayTrace();
            try
            {
                var ok = ray.SingleRayNormUnpol(RaysType.Real, target, wavelength, hx, hy, px, py, true, out var error, out var vignette, out var x, out var y, out var z, out var l, out var m, out var n, out _, out _, out _, out var opd, out var intensity);
                return new Result(ok, ok ? null : $"Ray trace failed (error code: {error}).", x,y,z,l,m,n,opd,intensity,error,vignette,target,ok && error == 0,ok && error == 0 && vignette == 0);
            }
            finally { ray.Close(); }
        }); }
        catch (Exception ex) { return new Result(false, ex.Message, SurfaceNumber: surface); }
    }
}
