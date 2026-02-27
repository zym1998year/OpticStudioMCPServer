using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class LateralColorTool
{
    private readonly IZemaxSession _session;

    public LateralColorTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_lateral_color")]
    [Description("Calculate lateral color (chromatic aberration in the image plane) as a function of field. Returns lateral color in µm for each relative field position using real ray tracing between the short and long wavelengths.")]
    public async Task<LateralColorData> ExecuteAsync()
    {
        try
        {
            return await _session.ExecuteAsync("LateralColor", null, system =>
            {
                var analysis = system.Analyses.New_LateralColor();
                try
                {
                    analysis.ApplyAndWaitForCompletion();

                    var tempFile = Path.Combine(Path.GetTempPath(), $"zemax_latcolor_{Guid.NewGuid():N}.txt");
                    try
                    {
                        analysis.GetResults().GetTextFile(tempFile);
                        return ParseLateralColorTextFile(tempFile);
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
            return new LateralColorData { Success = false, Error = ex.Message };
        }
    }

    private static LateralColorData ParseLateralColorTextFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        string units = "";
        double maxField = 0, shortWave = 0, longWave = 0;
        var relFields = new List<double>();
        var latColor = new List<double>();
        bool inData = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.StartsWith("Units are", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("are", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) units = trimmed.Substring(idx + 3).Trim().TrimEnd('.');
                continue;
            }

            if (trimmed.StartsWith("Maximum Field", StringComparison.OrdinalIgnoreCase))
            {
                maxField = ParseColonValue(trimmed);
                continue;
            }

            // Parse wavelengths - first occurrence is short, second is long
            if (trimmed.StartsWith("Short Wavelength", StringComparison.OrdinalIgnoreCase))
            {
                shortWave = ParseColonValue(trimmed);
                continue;
            }
            if (trimmed.StartsWith("Long Wavelength", StringComparison.OrdinalIgnoreCase))
            {
                longWave = ParseColonValue(trimmed);
                continue;
            }

            // Detect column header
            if (trimmed.StartsWith("Rel.", StringComparison.OrdinalIgnoreCase) &&
                trimmed.IndexOf("Lateral Color", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                inData = true;
                continue;
            }

            if (!inData) continue;

            var values = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 2 &&
                double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double rf) &&
                double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lc))
            {
                relFields.Add(rf);
                latColor.Add(lc);
            }
        }

        // Handle case where both wavelengths appear as "Short Wavelength" in text output
        // The second one is actually the long wavelength
        if (longWave == 0 && shortWave > 0)
        {
            // Re-parse to get both wavelength values
            bool foundFirst = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.IndexOf("Wavelength", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    trimmed.Contains(":") &&
                    !trimmed.StartsWith("Maximum", StringComparison.OrdinalIgnoreCase))
                {
                    var val = ParseColonValue(trimmed);
                    if (val > 0)
                    {
                        if (!foundFirst) { shortWave = val; foundFirst = true; }
                        else { longWave = val; break; }
                    }
                }
            }
        }

        return new LateralColorData
        {
            Success = true,
            Units = units,
            MaximumFieldDeg = maxField,
            ShortWavelength = shortWave,
            LongWavelength = longWave,
            RelativeFields = relFields.ToArray(),
            LateralColor = latColor.ToArray(),
            DataPoints = relFields.Count
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
