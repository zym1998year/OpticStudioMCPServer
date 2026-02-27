using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class GeometricMtfVsFieldTool
{
    private readonly IZemaxSession _session;

    public GeometricMtfVsFieldTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_geometric_mtf_vs_field")]
    [Description("Calculate polychromatic Geometric MTF as a function of Y field height for up to 6 spatial frequencies. Returns tangential and sagittal modulation versus relative field for each frequency. Uses ray-based (geometric) MTF calculation.")]
    public async Task<FftMtfVsFieldData> ExecuteAsync(
        [Description("Spatial frequency 1 in cycles/mm")] double frequency1 = 10,
        [Description("Spatial frequency 2 in cycles/mm (0 to skip)")] double frequency2 = 0,
        [Description("Spatial frequency 3 in cycles/mm (0 to skip)")] double frequency3 = 0,
        [Description("Spatial frequency 4 in cycles/mm (0 to skip)")] double frequency4 = 0,
        [Description("Spatial frequency 5 in cycles/mm (0 to skip)")] double frequency5 = 0,
        [Description("Spatial frequency 6 in cycles/mm (0 to skip)")] double frequency6 = 0)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["frequency1"] = frequency1,
                ["frequency2"] = frequency2,
                ["frequency3"] = frequency3,
                ["frequency4"] = frequency4,
                ["frequency5"] = frequency5,
                ["frequency6"] = frequency6
            };

            return await _session.ExecuteAsync("GeometricMtfVsField", parameters, system =>
            {
                var analysis = system.Analyses.New_GeometricMtfvsField();
                try
                {
                    var settings = analysis.GetSettings() as ZOSAPI.Analysis.Settings.Mtf.IAS_GeometricMtfvsField;
                    if (settings != null)
                    {
                        settings.Freq_1 = frequency1;
                        settings.Freq_2 = frequency2;
                        settings.Freq_3 = frequency3;
                        settings.Freq_4 = frequency4;
                        settings.Freq_5 = frequency5;
                        settings.Freq_6 = frequency6;
                    }

                    analysis.ApplyAndWaitForCompletion();

                    var tempFile = Path.Combine(Path.GetTempPath(), $"zemax_geomtfvsfield_{Guid.NewGuid():N}.txt");
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
            return new FftMtfVsFieldData { Success = false, Error = ex.Message };
        }
    }

    private static FftMtfVsFieldData ParseTextFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        double maxField = 0;
        string wavelengthRange = "";
        var freqBlocks = new List<FftMtfVsFieldFrequencyData>();

        double currentFreq = 0;
        var relFields = new List<double>();
        var tanValues = new List<double>();
        var sagValues = new List<double>();
        bool inData = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.StartsWith("Maximum Y field", StringComparison.OrdinalIgnoreCase))
            {
                maxField = ParseValueFromLine(trimmed);
                continue;
            }

            if (trimmed.StartsWith("Data for", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("\u00B5m"))
            {
                wavelengthRange = trimmed;
                continue;
            }

            if (trimmed.StartsWith("Data for spatial frequency", StringComparison.OrdinalIgnoreCase))
            {
                if (inData && relFields.Count > 0)
                {
                    freqBlocks.Add(new FftMtfVsFieldFrequencyData
                    {
                        SpatialFrequency = currentFreq,
                        RelativeFields = relFields.ToArray(),
                        Tangential = tanValues.ToArray(),
                        Sagittal = sagValues.ToArray(),
                        DataPoints = relFields.Count
                    });
                }

                currentFreq = ParseValueAfterColon(trimmed);
                relFields = new List<double>();
                tanValues = new List<double>();
                sagValues = new List<double>();
                inData = false;
                continue;
            }

            var lower = trimmed.ToLowerInvariant();
            if (!inData && lower.Contains("relative field") &&
                lower.Contains("tangential") && lower.Contains("sagittal"))
            {
                inData = true;
                continue;
            }

            if (!inData) continue;

            var values = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 3 &&
                double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double rf) &&
                double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double tan) &&
                double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double sag))
            {
                relFields.Add(rf);
                tanValues.Add(tan);
                sagValues.Add(sag);
            }
        }

        if (relFields.Count > 0)
        {
            freqBlocks.Add(new FftMtfVsFieldFrequencyData
            {
                SpatialFrequency = currentFreq,
                RelativeFields = relFields.ToArray(),
                Tangential = tanValues.ToArray(),
                Sagittal = sagValues.ToArray(),
                DataPoints = relFields.Count
            });
        }

        return new FftMtfVsFieldData
        {
            Success = true,
            MaximumFieldDeg = maxField,
            WavelengthRange = wavelengthRange,
            FrequencyData = freqBlocks.ToArray()
        };
    }

    private static double ParseValueAfterColon(string line)
    {
        int idx = line.LastIndexOf(':');
        if (idx < 0) return 0;
        var parts = line.Substring(idx + 1).Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            return val;
        return 0;
    }

    private static double ParseValueFromLine(string line)
    {
        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            var clean = parts[i].TrimEnd('.', '%');
            if (double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;
        }
        return 0;
    }
}
