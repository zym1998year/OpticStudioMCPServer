using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class RmsSpotTool
{
    private readonly IZemaxSession _session;

    public RmsSpotTool(IZemaxSession session) => _session = session;

    public record RmsSpotResult(
        bool Success,
        string? Error,
        double RmsSpotRadius,
        double Hx,
        double Hy,
        int Wavelength,
        string Reference,
        string Method
    );

    [McpServerTool(Name = "zemax_rms_spot")]
    [Description("Calculate RMS spot size for a field point")]
    public async Task<RmsSpotResult> ExecuteAsync(
        [Description("Normalized field x coordinate")] double hx = 0,
        [Description("Normalized field y coordinate")] double hy = 0,
        [Description("Wavelength number (0 for polychromatic)")] int wavelength = 0,
        [Description("Reference: 'centroid' or 'chief'")] string reference = "centroid",
        [Description("Sampling rings (for Gaussian quadrature) or grid size")] int sampling = 4,
        [Description("Use rectangular grid (true) or Gaussian quadrature (false)")] bool useGrid = false)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["hx"] = hx, ["hy"] = hy,
                ["wavelength"] = wavelength,
                ["reference"] = reference,
                ["sampling"] = sampling,
                ["useGrid"] = useGrid
            };

            var result = await _session.ExecuteAsync("RmsSpot", parameters, system =>
            {
                var mfe = system.MFE;
                var row = mfe.AddOperand();

                // Select operand type based on reference and method
                var operandType = (reference.ToLower(), useGrid) switch
                {
                    ("centroid", false) => ZOSAPI.Editors.MFE.MeritOperandType.RSCE,
                    ("centroid", true) => ZOSAPI.Editors.MFE.MeritOperandType.RSRE,
                    ("chief", false) => ZOSAPI.Editors.MFE.MeritOperandType.RSCH,
                    ("chief", true) => ZOSAPI.Editors.MFE.MeritOperandType.RSRH,
                    _ => ZOSAPI.Editors.MFE.MeritOperandType.RSCE
                };

                row.ChangeType(operandType);

                // Configure operand
                row.GetCellAt(2).IntegerValue = sampling;
                row.GetCellAt(3).IntegerValue = wavelength;
                row.GetCellAt(4).DoubleValue = hx;
                row.GetCellAt(5).DoubleValue = hy;

                mfe.CalculateMeritFunction();
                var rmsValue = row.Value;

                mfe.RemoveOperandAt(row.OperandNumber);

                return new RmsSpotResult(
                    Success: true,
                    Error: null,
                    RmsSpotRadius: rmsValue,
                    Hx: hx,
                    Hy: hy,
                    Wavelength: wavelength,
                    Reference: reference,
                    Method: useGrid ? "Rectangular Grid" : "Gaussian Quadrature"
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new RmsSpotResult(false, ex.Message, 0, hx, hy, wavelength, reference, "");
        }
    }
}
