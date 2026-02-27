using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class ChromaticFocalShiftTool
{
    private readonly IZemaxSession _session;

    public ChromaticFocalShiftTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_chromatic_focal_shift")]
    [Description("Calculate chromatic focal shift (longitudinal chromatic aberration) as a function of wavelength. Shows how the paraxial focus position shifts across the wavelength range. Returns wavelength vs focal shift data, plus the maximum focal shift range and diffraction-limited range.")]
    public async Task<ChromaticFocalShiftData> ExecuteAsync()
    {
        try
        {
            return await _session.ExecuteAsync("ChromaticFocalShift", null, system =>
            {
                var analysis = system.Analyses.New_FocalShiftDiagram();
                try
                {
                    analysis.ApplyAndWaitForCompletion();

                    var tempFile = Path.Combine(Path.GetTempPath(), $"zemax_cfs_{Guid.NewGuid():N}.txt");
                    try
                    {
                        analysis.GetResults().GetTextFile(tempFile);
                        return ParseChromaticFocalShiftTextFile(tempFile);
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
            return new ChromaticFocalShiftData { Success = false, Error = ex.Message };
        }
    }

    private static ChromaticFocalShiftData ParseChromaticFocalShiftTextFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        string wavelengthUnits = "", shiftUnits = "";
        double pupilZone = 0, maxRange = 0, dlRange = 0;
        var wavelengths = new List<double>();
        var shifts = new List<double>();
        bool inData = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.StartsWith("Wavelength units", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("are", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) wavelengthUnits = trimmed.Substring(idx + 3).Trim().TrimEnd('.');
                continue;
            }

            if (trimmed.StartsWith("Shift units", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("are", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) shiftUnits = trimmed.Substring(idx + 3).Trim().TrimEnd('.');
                continue;
            }

            if (trimmed.StartsWith("Pupil Zone", StringComparison.OrdinalIgnoreCase))
            {
                pupilZone = ParseColonValue(trimmed);
                continue;
            }

            if (trimmed.StartsWith("Maximum Focal Shift", StringComparison.OrdinalIgnoreCase))
            {
                maxRange = ParseColonValue(trimmed);
                continue;
            }

            if (trimmed.StartsWith("Diffraction Limited", StringComparison.OrdinalIgnoreCase))
            {
                dlRange = ParseColonValue(trimmed);
                continue;
            }

            // Detect column header
            if (trimmed.IndexOf("Wavelength", StringComparison.OrdinalIgnoreCase) >= 0 &&
                trimmed.IndexOf("Shift", StringComparison.OrdinalIgnoreCase) >= 0 &&
                trimmed.IndexOf("units", StringComparison.OrdinalIgnoreCase) < 0 &&
                trimmed.IndexOf("Range", StringComparison.OrdinalIgnoreCase) < 0)
            {
                inData = true;
                continue;
            }

            if (!inData) continue;

            var values = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 2 &&
                double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double wl) &&
                double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double shift))
            {
                wavelengths.Add(wl);
                shifts.Add(shift);
            }
        }

        return new ChromaticFocalShiftData
        {
            Success = true,
            WavelengthUnits = wavelengthUnits,
            ShiftUnits = shiftUnits,
            PupilZone = pupilZone,
            MaximumFocalShiftRange = maxRange,
            DiffractionLimitedRange = dlRange,
            Wavelengths = wavelengths.ToArray(),
            Shifts = shifts.ToArray(),
            DataPoints = wavelengths.Count
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
}
