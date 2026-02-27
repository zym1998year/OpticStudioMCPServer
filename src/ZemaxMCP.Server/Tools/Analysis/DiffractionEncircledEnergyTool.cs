using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class DiffractionEncircledEnergyTool
{
    private readonly IZemaxSession _session;

    public DiffractionEncircledEnergyTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_diffraction_encircled_energy")]
    [Description("Calculate FFT diffraction encircled energy as a function of radial distance from the reference point. Returns the fraction of total energy enclosed within a given radius for each field point, plus the diffraction limit curve. Useful for evaluating image quality and comparing to the Airy disk.")]
    public async Task<DiffractionEncircledEnergyData> ExecuteAsync(
        [Description("Sampling (1-6, higher = more accurate). 1=32x32, 2=64x64, 3=128x128, 4=256x256, 5=512x512, 6=1024x1024")] int sampling = 3,
        [Description("Use dashes for data? If true, uses dashes; if false uses center of reference field")] bool useDashes = false)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["sampling"] = sampling,
                ["useDashes"] = useDashes
            };

            return await _session.ExecuteAsync("DiffractionEncircledEnergy", parameters, system =>
            {
                var analysis = system.Analyses.New_DiffractionEncircledEnergy();
                try
                {
                    var settings = analysis.GetSettings() as ZOSAPI.Analysis.Settings.EncircledEnergy.IAS_DiffractionEncircledEnergy;
                    if (settings != null)
                    {
                        settings.SampleSize = MapSampling(sampling);
                        settings.UseDashes = useDashes;
                    }

                    analysis.ApplyAndWaitForCompletion();

                    var tempFile = Path.Combine(Path.GetTempPath(), $"zemax_dee_{Guid.NewGuid():N}.txt");
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
            return new DiffractionEncircledEnergyData { Success = false, Error = ex.Message };
        }
    }

    private static ZOSAPI.Analysis.SampleSizes MapSampling(int sampling) => sampling switch
    {
        1 => ZOSAPI.Analysis.SampleSizes.S_32x32,
        2 => ZOSAPI.Analysis.SampleSizes.S_64x64,
        3 => ZOSAPI.Analysis.SampleSizes.S_128x128,
        4 => ZOSAPI.Analysis.SampleSizes.S_256x256,
        5 => ZOSAPI.Analysis.SampleSizes.S_512x512,
        6 => ZOSAPI.Analysis.SampleSizes.S_1024x1024,
        _ => ZOSAPI.Analysis.SampleSizes.S_128x128
    };

    private static DiffractionEncircledEnergyData ParseTextFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        string surface = "", wavelength = "", reference = "", distanceUnits = "";
        var fields = new List<DiffractionEncircledEnergyFieldData>();

        string currentLabel = "";
        double currentFieldDeg = -1;
        double refX = 0, refY = 0;
        var distances = new List<double>();
        var fractions = new List<double>();
        bool inData = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Header metadata
            if (trimmed.StartsWith("Surface:", StringComparison.OrdinalIgnoreCase))
            {
                surface = trimmed.Substring("Surface:".Length).Trim();
                continue;
            }

            if (trimmed.StartsWith("Wavelength:", StringComparison.OrdinalIgnoreCase))
            {
                wavelength = trimmed.Substring("Wavelength:".Length).Trim();
                continue;
            }

            if (trimmed.StartsWith("Reference:", StringComparison.OrdinalIgnoreCase))
            {
                reference = trimmed.Substring("Reference:".Length).Trim();
                continue;
            }

            if (trimmed.StartsWith("Distance units", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("are", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) distanceUnits = trimmed.Substring(idx + 3).Trim().TrimEnd('.');
                continue;
            }

            // "Diff. Limit" or "Field: X.XX (deg)"
            if (trimmed.StartsWith("Diff. Limit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Field:", StringComparison.OrdinalIgnoreCase))
            {
                // Save previous block
                if (distances.Count > 0)
                {
                    fields.Add(new DiffractionEncircledEnergyFieldData
                    {
                        Label = currentLabel,
                        FieldValueDeg = currentFieldDeg,
                        ReferenceX = refX,
                        ReferenceY = refY,
                        RadialDistances = distances.ToArray(),
                        Fractions = fractions.ToArray(),
                        DataPoints = distances.Count
                    });
                }

                distances = new List<double>();
                fractions = new List<double>();
                refX = 0;
                refY = 0;
                inData = false;

                if (trimmed.StartsWith("Diff. Limit", StringComparison.OrdinalIgnoreCase))
                {
                    currentLabel = "Diffraction Limit";
                    currentFieldDeg = -1;
                }
                else
                {
                    currentLabel = trimmed;
                    currentFieldDeg = ParseFieldValue(trimmed);
                }
                continue;
            }

            // "Reference Coordinates: X =    5.203E-06 Y =    5.203E-06"
            if (trimmed.StartsWith("Reference Coordinates:", StringComparison.OrdinalIgnoreCase))
            {
                ParseReferenceCoordinates(trimmed, out refX, out refY);
                continue;
            }

            // Column header: "Radial distance              Fraction"
            var lower = trimmed.ToLowerInvariant();
            if (!inData && lower.Contains("radial distance") && lower.Contains("fraction"))
            {
                inData = true;
                continue;
            }

            if (!inData) continue;

            // Parse data row
            var values = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 2 &&
                double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double dist) &&
                double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double frac))
            {
                distances.Add(dist);
                fractions.Add(frac);
            }
        }

        // Save last block
        if (distances.Count > 0)
        {
            fields.Add(new DiffractionEncircledEnergyFieldData
            {
                Label = currentLabel,
                FieldValueDeg = currentFieldDeg,
                ReferenceX = refX,
                ReferenceY = refY,
                RadialDistances = distances.ToArray(),
                Fractions = fractions.ToArray(),
                DataPoints = distances.Count
            });
        }

        return new DiffractionEncircledEnergyData
        {
            Success = true,
            Surface = surface,
            Wavelength = wavelength,
            Reference = reference,
            DistanceUnits = distanceUnits,
            Fields = fields.ToArray()
        };
    }

    private static double ParseFieldValue(string line)
    {
        // "Field: 10.00 (deg)"
        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;
        }
        return 0;
    }

    private static void ParseReferenceCoordinates(string line, out double x, out double y)
    {
        x = 0;
        y = 0;
        // "Reference Coordinates: X =    5.203E-06 Y =    5.203E-06"
        var upper = line.ToUpperInvariant();
        int xIdx = upper.IndexOf("X =");
        int yIdx = upper.IndexOf("Y =");
        if (xIdx >= 0 && yIdx >= 0)
        {
            var xPart = line.Substring(xIdx + 3, yIdx - xIdx - 3).Trim();
            var yPart = line.Substring(yIdx + 3).Trim();
            double.TryParse(xPart, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
            double.TryParse(yPart, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        }
    }
}
