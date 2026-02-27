using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class GeometricEncircledEnergyTool
{
    private readonly IZemaxSession _session;

    public GeometricEncircledEnergyTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_geometric_encircled_energy")]
    [Description("Calculate geometric encircled energy as a function of radial distance from the reference point using ray tracing. Returns the fraction of total energy enclosed within a given radius for each field point. Useful for evaluating image quality based on geometric ray density.")]
    public async Task<GeometricEncircledEnergyData> ExecuteAsync(
        [Description("Sampling (1-6, higher = more accurate). 1=32x32, 2=64x64, 3=128x128, 4=256x256, 5=512x512, 6=1024x1024")] int sampling = 4,
        [Description("Show diffraction limit curve?")] bool showDiffractionLimit = true,
        [Description("Scale data by diffraction limit?")] bool scaleByDiffractionLimit = false,
        [Description("Use scatter rays instead of grid?")] bool scatterRays = false,
        [Description("Use dashes for data? If true, uses dashes; if false uses center of reference field")] bool useDashes = false)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["sampling"] = sampling,
                ["showDiffractionLimit"] = showDiffractionLimit,
                ["scaleByDiffractionLimit"] = scaleByDiffractionLimit,
                ["scatterRays"] = scatterRays,
                ["useDashes"] = useDashes
            };

            return await _session.ExecuteAsync("GeometricEncircledEnergy", parameters, system =>
            {
                var analysis = system.Analyses.New_GeometricEncircledEnergy();
                try
                {
                    var settings = analysis.GetSettings() as ZOSAPI.Analysis.Settings.EncircledEnergy.IAS_GeometricEncircledEnergy;
                    if (settings != null)
                    {
                        settings.SampleSize = MapSampling(sampling);
                        settings.ShowDiffractionLimit = showDiffractionLimit;
                        settings.ScatterRays = scatterRays;
                        settings.UseDashes = useDashes;
                    }

                    analysis.ApplyAndWaitForCompletion();

                    var tempFile = Path.Combine(Path.GetTempPath(), $"zemax_gee_{Guid.NewGuid():N}.txt");
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
            return new GeometricEncircledEnergyData { Success = false, Error = ex.Message };
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
        _ => ZOSAPI.Analysis.SampleSizes.S_256x256
    };

    private static GeometricEncircledEnergyData ParseTextFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        string surface = "", wavelength = "", reference = "", distanceUnits = "";
        bool scaledByDiffractionLimit = false;
        var fields = new List<GeometricEncircledEnergyFieldData>();

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

            if (trimmed.IndexOf("scaled by diffraction limit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                scaledByDiffractionLimit = true;
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
                    fields.Add(new GeometricEncircledEnergyFieldData
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

            // Reference Coordinates - two formats:
            // "Reference Coordinates: X =    5.203E-06 Y =    5.203E-06"
            // "Reference Coordinates:    1.153E-20    9.226E-20"
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
            fields.Add(new GeometricEncircledEnergyFieldData
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

        return new GeometricEncircledEnergyData
        {
            Success = true,
            Surface = surface,
            Wavelength = wavelength,
            Reference = reference,
            DistanceUnits = distanceUnits,
            ScaledByDiffractionLimit = scaledByDiffractionLimit,
            Fields = fields.ToArray()
        };
    }

    private static double ParseFieldValue(string line)
    {
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

        var afterColon = line.Substring(line.IndexOf(':') + 1).Trim();
        var upper = afterColon.ToUpperInvariant();

        // Format 1: "X =    5.203E-06 Y =    5.203E-06"
        int xIdx = upper.IndexOf("X =");
        int yIdx = upper.IndexOf("Y =");
        if (xIdx >= 0 && yIdx >= 0)
        {
            var xPart = afterColon.Substring(xIdx + 3, yIdx - xIdx - 3).Trim();
            var yPart = afterColon.Substring(yIdx + 3).Trim();
            double.TryParse(xPart, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
            double.TryParse(yPart, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
            return;
        }

        // Format 2: "   1.153E-20    9.226E-20"
        var values = afterColon.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (values.Length >= 2)
        {
            double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
            double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        }
    }
}
