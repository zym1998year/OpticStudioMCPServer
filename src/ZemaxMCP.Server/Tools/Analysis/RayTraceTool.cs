using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;
using ZOSAPI.Tools.RayTrace;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class RayTraceTool
{
    private readonly IZemaxSession _session;

    public RayTraceTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_ray_trace")]
    [Description("Trace a ray through the optical system using batch ray tracing")]
    public async Task<RayTraceResult> ExecuteAsync(
        [Description("Normalized field x coordinate (-1 to 1)")] double hx = 0,
        [Description("Normalized field y coordinate (-1 to 1)")] double hy = 0,
        [Description("Normalized pupil x coordinate (-1 to 1)")] double px = 0,
        [Description("Normalized pupil y coordinate (-1 to 1)")] double py = 0,
        [Description("Wavelength number")] int wavelength = 1,
        [Description("Surface to trace to (0 for image)")] int surface = 0)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["hx"] = hx, ["hy"] = hy,
                ["px"] = px, ["py"] = py,
                ["wavelength"] = wavelength,
                ["surface"] = surface
            };

            var result = await _session.ExecuteAsync("RayTrace", parameters, system =>
            {
                var surf = surface == 0
                    ? system.LDE.NumberOfSurfaces - 1
                    : surface;

                var batchRay = system.Tools.OpenBatchRayTrace();
                try
                {
                    // SingleRayNormUnpol returns all ray data via out parameters
                    bool success = batchRay.SingleRayNormUnpol(
                        RaysType.Real, surf, wavelength,
                        hx, hy, px, py,
                        true, // calcOPD
                        out int errorCode, out int vignetteCode,
                        out double xo, out double yo, out double zo,
                        out double lo, out double mo, out double no,
                        out double l2o, out double m2o, out double n2o,
                        out double opd, out double intensity);

                    return new RayTraceResult
                    {
                        Success = success,
                        Error = success ? null : $"Ray trace failed (error code: {errorCode})",
                        X = xo,
                        Y = yo,
                        Z = zo,
                        L = lo,
                        M = mo,
                        N = no,
                        OpticalPathLength = opd,
                        SurfaceNumber = surf,
                        RayValid = success && errorCode == 0
                    };
                }
                finally
                {
                    batchRay.Close();
                }
            });

            return result;
        }
        catch (Exception ex)
        {
            return new RayTraceResult
            {
                Success = false,
                Error = ex.Message,
                SurfaceNumber = surface,
                RayValid = false
            };
        }
    }
}
