using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class FieldCurvatureDistortionTool
{
    private readonly IZemaxSession _session;

    public FieldCurvatureDistortionTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_field_curvature_distortion")]
    [Description("Calculate field curvature (tangential and sagittal focus shift) and distortion as a function of field angle for each system wavelength. Returns focus shift in mm and distortion in percent.")]
    public async Task<FieldCurvatureDistortionData> ExecuteAsync(
        [Description("Distortion type: 'f_tan_theta' (default) or 'f_theta'")] string distortionType = "f_tan_theta")
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["distortionType"] = distortionType
            };

            return await _session.ExecuteAsync("FieldCurvatureDistortion", parameters, system =>
            {
                var analysis = system.Analyses.New_FieldCurvatureAndDistortion();
                try
                {
                    var settings = analysis.GetSettings() as ZOSAPI.Analysis.Settings.Aberrations.IAS_FieldCurvatureAndDistortion;
                    if (settings != null)
                    {
                        settings.Distortion = distortionType?.ToLowerInvariant() == "f_theta"
                            ? ZOSAPI.Analysis.Settings.Aberrations.Distortions.F_Theta
                            : ZOSAPI.Analysis.Settings.Aberrations.Distortions.F_TanTheta;
                    }

                    analysis.ApplyAndWaitForCompletion();

                    var tempFile = Path.Combine(Path.GetTempPath(), $"zemax_fcd_{Guid.NewGuid():N}.txt");
                    try
                    {
                        analysis.GetResults().GetTextFile(tempFile);
                        return ParseTextFile(tempFile);
                    }
                    finally
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }
                finally
                {
                    analysis.Close();
                }
            });
        }
        catch (Exception ex)
        {
            return new FieldCurvatureDistortionData { Success = false, Error = ex.Message };
        }
    }

    private static FieldCurvatureDistortionData ParseTextFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        string distortionType = "", shiftUnits = "", heightUnits = "", distortionUnits = "";
        double maxField = 0, maxDistortion = 0;
        var wavelengthBlocks = new List<FieldCurvatureWavelengthData>();

        // Current block state
        double currentWavelength = 0, currentFocalLength = 0;
        var fieldAngles = new List<double>();
        var tanShifts = new List<double>();
        var sagShifts = new List<double>();
        var realHeights = new List<double>();
        var refHeights = new List<double>();
        var distortions = new List<double>();
        bool inData = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.StartsWith("Distortion Type", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf(':');
                if (idx >= 0) distortionType = trimmed.Substring(idx + 1).Trim();
                continue;
            }

            if (trimmed.StartsWith("Shift units", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("are", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) shiftUnits = trimmed.Substring(idx + 3).Trim().TrimEnd('.');
                continue;
            }

            if (trimmed.StartsWith("Height units", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("are", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) heightUnits = trimmed.Substring(idx + 3).Trim().TrimEnd('.');
                continue;
            }

            if (trimmed.StartsWith("Distortion units", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("are", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) distortionUnits = trimmed.Substring(idx + 3).Trim().TrimEnd('.');
                continue;
            }

            if (trimmed.StartsWith("Maximum Field", StringComparison.OrdinalIgnoreCase))
            {
                maxField = ParseValueBeforeUnit(trimmed);
                continue;
            }

            if (trimmed.StartsWith("Maximum distortion", StringComparison.OrdinalIgnoreCase))
            {
                maxDistortion = ParseValueAfterEquals(trimmed);
                continue;
            }

            // "Data for wavelength : 0.486100 µm."
            if (trimmed.StartsWith("Data for wavelength", StringComparison.OrdinalIgnoreCase))
            {
                // Save previous block if any
                if (inData && fieldAngles.Count > 0)
                {
                    wavelengthBlocks.Add(BuildBlock(currentWavelength, currentFocalLength,
                        fieldAngles, tanShifts, sagShifts, realHeights, refHeights, distortions));
                }

                currentWavelength = ParseColonValue(trimmed);
                currentFocalLength = 0;
                fieldAngles = new List<double>();
                tanShifts = new List<double>();
                sagShifts = new List<double>();
                realHeights = new List<double>();
                refHeights = new List<double>();
                distortions = new List<double>();
                inData = false;
                continue;
            }

            if (trimmed.StartsWith("Distortion focal length", StringComparison.OrdinalIgnoreCase))
            {
                currentFocalLength = ParseEqualsValue(trimmed);
                continue;
            }

            // Detect column header row
            if (trimmed.StartsWith("Y Angle", StringComparison.OrdinalIgnoreCase))
            {
                inData = true;
                continue;
            }

            if (!inData) continue;

            // Parse data row: 6 columns, last one may end with " %"
            var cleanLine = trimmed.Replace("%", "").Trim();
            var values = cleanLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 6 &&
                double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double angle) &&
                double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double tanS) &&
                double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double sagS) &&
                double.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double realH) &&
                double.TryParse(values[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double refH) &&
                double.TryParse(values[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double dist))
            {
                fieldAngles.Add(angle);
                tanShifts.Add(tanS);
                sagShifts.Add(sagS);
                realHeights.Add(realH);
                refHeights.Add(refH);
                distortions.Add(dist);
            }
        }

        // Save last block
        if (fieldAngles.Count > 0)
        {
            wavelengthBlocks.Add(BuildBlock(currentWavelength, currentFocalLength,
                fieldAngles, tanShifts, sagShifts, realHeights, refHeights, distortions));
        }

        return new FieldCurvatureDistortionData
        {
            Success = true,
            DistortionType = distortionType,
            ShiftUnits = shiftUnits,
            HeightUnits = heightUnits,
            DistortionUnits = distortionUnits,
            MaximumFieldDeg = maxField,
            MaximumDistortionPercent = maxDistortion,
            WavelengthData = wavelengthBlocks.ToArray()
        };
    }

    private static FieldCurvatureWavelengthData BuildBlock(
        double wavelength, double focalLength,
        List<double> fieldAngles, List<double> tanShifts, List<double> sagShifts,
        List<double> realHeights, List<double> refHeights, List<double> distortions)
    {
        return new FieldCurvatureWavelengthData
        {
            Wavelength = wavelength,
            DistortionFocalLength = focalLength,
            FieldAnglesDeg = fieldAngles.ToArray(),
            TangentialShift = tanShifts.ToArray(),
            SagittalShift = sagShifts.ToArray(),
            RealHeight = realHeights.ToArray(),
            ReferenceHeight = refHeights.ToArray(),
            DistortionPercent = distortions.ToArray(),
            DataPoints = fieldAngles.Count
        };
    }

    private static double ParseColonValue(string line)
    {
        int idx = line.LastIndexOf(':');
        if (idx < 0) return 0;
        var part = line.Substring(idx + 1).Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (part.Length > 0 && double.TryParse(part[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            return val;
        return 0;
    }

    private static double ParseEqualsValue(string line)
    {
        int idx = line.LastIndexOf('=');
        if (idx < 0) return ParseColonValue(line);
        var part = line.Substring(idx + 1).Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (part.Length > 0 && double.TryParse(part[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            return val;
        return 0;
    }

    private static double ParseValueBeforeUnit(string line)
    {
        // "Maximum Field is 14.000 Degrees."
        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            var clean = parts[i].TrimEnd('.', '%');
            if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;
        }
        return 0;
    }

    private static double ParseValueAfterEquals(string line)
    {
        // "Maximum distortion = 0.9401%"
        int idx = line.IndexOf('=');
        if (idx < 0) return 0;
        var part = line.Substring(idx + 1).Trim().TrimEnd('%').Trim();
        if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            return val;
        return 0;
    }
}
