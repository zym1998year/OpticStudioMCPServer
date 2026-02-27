using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class LongitudinalAberrationTool
{
    private readonly IZemaxSession _session;

    public LongitudinalAberrationTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_longitudinal_aberration")]
    [Description("Calculate longitudinal aberration (focus shift) as a function of pupil position for each wavelength. Returns aberration in millimeters for each relative pupil coordinate at each system wavelength.")]
    public async Task<LongitudinalAberrationData> ExecuteAsync()
    {
        try
        {
            return await _session.ExecuteAsync("LongitudinalAberration", null, system =>
            {
                var analysis = system.Analyses.New_LongitudinalAberration();
                try
                {
                    analysis.ApplyAndWaitForCompletion();

                    var tempFile = Path.Combine(Path.GetTempPath(), $"zemax_longab_{Guid.NewGuid():N}.txt");
                    try
                    {
                        analysis.GetResults().GetTextFile(tempFile);
                        return ParseLongitudinalTextFile(tempFile);
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
            return new LongitudinalAberrationData { Success = false, Error = ex.Message };
        }
    }

    private static LongitudinalAberrationData ParseLongitudinalTextFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        string units = "";
        double[]? wavelengths = null;
        var relPupils = new List<double>();
        var aberrationColumns = new List<List<double>>();
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

            // Detect header row: "Rel. Pupil   0.4861   0.5876   0.6563"
            if (trimmed.StartsWith("Rel.", StringComparison.OrdinalIgnoreCase) &&
                trimmed.IndexOf("Pupil", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                // Skip "Rel." and "Pupil", rest are wavelengths
                var waveList = new List<double>();
                for (int j = 2; j < parts.Length; j++)
                {
                    if (double.TryParse(parts[j], NumberStyles.Float, CultureInfo.InvariantCulture, out double w))
                        waveList.Add(w);
                }
                wavelengths = waveList.ToArray();
                for (int j = 0; j < wavelengths.Length; j++)
                    aberrationColumns.Add(new List<double>());
                inData = true;
                continue;
            }

            if (!inData || wavelengths == null) continue;

            var values = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 1 + wavelengths.Length &&
                double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double pupil))
            {
                relPupils.Add(pupil);
                for (int j = 0; j < wavelengths.Length; j++)
                {
                    if (j + 1 < values.Length &&
                        double.TryParse(values[j + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ab))
                        aberrationColumns[j].Add(ab);
                    else
                        aberrationColumns[j].Add(0);
                }
            }
        }

        return new LongitudinalAberrationData
        {
            Success = true,
            Units = units,
            Wavelengths = wavelengths ?? [],
            RelativePupils = relPupils.ToArray(),
            Aberrations = aberrationColumns.Select(c => c.ToArray()).ToArray(),
            DataPoints = relPupils.Count
        };
    }
}
