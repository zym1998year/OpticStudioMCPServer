using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class SpotDiagramTool
{
    private readonly IZemaxSession _session;

    public SpotDiagramTool(IZemaxSession session)
    {
        _session = session;
    }

    [McpServerTool(Name = "zemax_spot_diagram")]
    [Description("Get spot size analysis using RMS spot operands")]
    public async Task<SpotDiagramData> ExecuteAsync(
        [Description("Field number (1-indexed)")] int field = 1,
        [Description("Wavelength number (0 for polychromatic)")] int wavelength = 0,
        [Description("Number of rings for Gaussian quadrature (1-6)")] int rings = 3)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["field"] = field,
                ["wavelength"] = wavelength,
                ["rings"] = rings
            };

            var result = await _session.ExecuteAsync("SpotDiagram", parameters, system =>
            {
                var mfe = system.MFE;
                var fields = system.SystemData.Fields;

                // Get normalized field coordinates (Hx, Hy)
                // RSCE/RSRE expect normalized fields (0-1), not raw angles/heights
                var fieldObj = fields.GetField(field);
                double maxFieldValue = 0;
                for (int i = 1; i <= fields.NumberOfFields; i++)
                {
                    var f = fields.GetField(i);
                    maxFieldValue = Math.Max(maxFieldValue, Math.Max(Math.Abs(f.X), Math.Abs(f.Y)));
                }
                var hx = maxFieldValue > 0 ? fieldObj.X / maxFieldValue : 0.0;
                var hy = maxFieldValue > 0 ? fieldObj.Y / maxFieldValue : 0.0;

                // Add RSCE operand for RMS spot size (centroid reference)
                var rowRms = mfe.AddOperand();
                rowRms.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.RSCE);
                rowRms.GetCellAt(2).IntegerValue = rings;      // Rings
                rowRms.GetCellAt(3).IntegerValue = wavelength; // Wave (0 = polychromatic)
                rowRms.GetCellAt(4).DoubleValue = hx;          // Hx
                rowRms.GetCellAt(5).DoubleValue = hy;          // Hy

                // Add RSRE operand for geometric spot size comparison
                var rowGeo = mfe.AddOperand();
                rowGeo.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.RSRE);
                rowGeo.GetCellAt(2).IntegerValue = rings;
                rowGeo.GetCellAt(3).IntegerValue = wavelength;
                rowGeo.GetCellAt(4).DoubleValue = hx;
                rowGeo.GetCellAt(5).DoubleValue = hy;

                mfe.CalculateMeritFunction();

                var rmsSpot = rowRms.Value;
                var geoSpot = rowGeo.Value;
                var rowNumRms = rowRms.OperandNumber;
                var rowNumGeo = rowGeo.OperandNumber;

                // Clean up
                mfe.RemoveOperandAt(rowNumGeo);
                mfe.RemoveOperandAt(rowNumRms);

                return new SpotDiagramData
                {
                    Success = true,
                    RmsSpotSizeX = rmsSpot,
                    RmsSpotSizeY = rmsSpot,
                    RmsSpotRadius = rmsSpot,
                    GeoSpotSizeX = geoSpot,
                    GeoSpotSizeY = geoSpot,
                    GeoSpotRadius = geoSpot,
                    CentroidX = 0,
                    CentroidY = 0,
                    AiryRadius = 0,
                    Field = field,
                    Wavelength = wavelength,
                    DataDescription = $"RMS Spot (centroid): {rmsSpot:F4}, RMS Spot (rectangular): {geoSpot:F4}"
                };
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SpotDiagramData
            {
                Success = false,
                Error = ex.Message,
                Field = field,
                Wavelength = wavelength
            };
        }
    }
}
