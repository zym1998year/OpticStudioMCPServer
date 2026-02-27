using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;
using ZemaxMCP.Server.Tools.Base;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class CardinalPointsTool
{
    private readonly IZemaxSession _session;

    public CardinalPointsTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_cardinal_points")]
    [Description("Get cardinal points (focal lengths, principal planes, etc.) of the optical system")]
    public async Task<CardinalPoints> ExecuteAsync(
        [Description("Wavelength number")] int wavelength = 1)
    {
        try
        {
            var result = await _session.ExecuteAsync("CardinalPoints",
                new Dictionary<string, object?> { ["wavelength"] = wavelength },
                system =>
            {
                var mfe = system.MFE;
                var lde = system.LDE;

                // Add operands - keep direct references (same pattern as RayTraceTool)
                var rowEffl = mfe.AddOperand();
                rowEffl.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.EFFL);
                rowEffl.GetCellAt(3).IntegerValue = wavelength;

                var rowEnpp = mfe.AddOperand();
                rowEnpp.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.ENPP);

                var rowEpdi = mfe.AddOperand();
                rowEpdi.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.EPDI);

                var rowExpp = mfe.AddOperand();
                rowExpp.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.EXPP);

                var rowExpd = mfe.AddOperand();
                rowExpd.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.EXPD);

                var rowPmag = mfe.AddOperand();
                rowPmag.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.PMAG);
                rowPmag.GetCellAt(3).IntegerValue = wavelength;

                mfe.CalculateMeritFunction();

                // Read values directly from row references (sanitize for JSON serialization)
                var effl = rowEffl.Value.Sanitize();
                var enpp = rowEnpp.Value.Sanitize();
                var epdi = rowEpdi.Value.Sanitize();
                var expp = rowExpp.Value.Sanitize();
                var expd = rowExpd.Value.Sanitize();
                var pmag = rowPmag.Value.Sanitize();

                // Compute BFL and FFL from surface thicknesses
                int numSurfaces = lde.NumberOfSurfaces;
                int lastLensSurface = numSurfaces - 2; // surface before image

                // Back focal length = thickness from last surface to image
                double bfl = lde.GetSurfaceAt(lastLensSurface).Thickness.Sanitize();

                // Front focal length = -(EFL + ENPP - distance from surf 1 to entrance pupil)
                // For object at infinity: FFL = ENPP - EFL (measured from surface 1)
                double ffl = (enpp - effl).Sanitize();

                // Object distance (surface 0 thickness)
                double objDist = lde.GetSurfaceAt(0).Thickness.Sanitize();

                // Image distance = BFL
                double imgDist = bfl;

                // Clean up - remove in reverse order using operand numbers
                var rowNums = new[]
                {
                    rowPmag.OperandNumber,
                    rowExpd.OperandNumber,
                    rowExpp.OperandNumber,
                    rowEpdi.OperandNumber,
                    rowEnpp.OperandNumber,
                    rowEffl.OperandNumber
                };

                foreach (var num in rowNums)
                {
                    mfe.RemoveOperandAt(num);
                }

                return new CardinalPoints
                {
                    Success = true,
                    EffectiveFocalLength = effl,
                    BackFocalLength = bfl,
                    FrontFocalLength = ffl,
                    EntrancePupilPosition = enpp,
                    EntrancePupilDiameter = epdi,
                    ExitPupilPosition = expp,
                    ExitPupilDiameter = expd,
                    ImageDistance = imgDist,
                    ObjectDistance = objDist,
                    Magnification = pmag,
                    Wavelength = wavelength
                };
            });

            return result;
        }
        catch (Exception ex)
        {
            return new CardinalPoints
            {
                Success = false,
                Error = ex.Message,
                Wavelength = wavelength
            };
        }
    }
}
