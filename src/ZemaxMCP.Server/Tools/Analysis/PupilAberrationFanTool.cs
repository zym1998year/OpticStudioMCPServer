using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class PupilAberrationFanTool
{
    private readonly IZemaxSession _session;

    public PupilAberrationFanTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_pupil_aberration_fan")]
    [Description("Calculate entrance pupil aberration fan for all fields and wavelengths. Returns tangential and sagittal fans showing the deviation of the actual ray entrance pupil coordinates from ideal (paraxial) coordinates. Units are percent.")]
    public async Task<RayFanData> ExecuteAsync()
    {
        try
        {
            return await _session.ExecuteAsync("PupilAberrationFan", null, system =>
            {
                var analysis = system.Analyses.New_PupilAberrationFan();
                try
                {
                    analysis.ApplyAndWaitForCompletion();

                    var tempFile = Path.Combine(Path.GetTempPath(), $"zemax_pupilab_{Guid.NewGuid():N}.txt");
                    try
                    {
                        analysis.GetResults().GetTextFile(tempFile);
                        return ParsePupilAberrationTextFile(tempFile);
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
            return new RayFanData { Success = false, Error = ex.Message };
        }
    }

    private static RayFanData ParsePupilAberrationTextFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        string units = "";
        string surface = "";
        var fields = new List<RayFanFieldData>();

        int currentFieldNumber = 0;
        double currentFieldValue = 0;
        string currentFanType = "";
        double[]? wavelengths = null;
        var pupils = new List<double>();
        var aberrationColumns = new List<List<double>>();
        bool inData = false;

        RayFanCurveData? currentTangential = null;
        RayFanCurveData? currentSagittal = null;

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var trimmed = lines[lineIdx].Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.StartsWith("Surface:", StringComparison.OrdinalIgnoreCase))
            {
                surface = trimmed.Substring(8).Trim();
                continue;
            }

            if (trimmed.StartsWith("Units are", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf("are", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) units = trimmed.Substring(idx + 3).Trim().TrimEnd('.');
                continue;
            }

            if (trimmed.IndexOf("fan, field number", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SaveCurrentFan(ref currentTangential, ref currentSagittal, currentFanType,
                    wavelengths, pupils, aberrationColumns);

                bool isTangential = trimmed.StartsWith("Tangential", StringComparison.OrdinalIgnoreCase);
                bool isSagittal = trimmed.StartsWith("Sagittal", StringComparison.OrdinalIgnoreCase);

                int newFieldNumber = ParseFieldNumber(trimmed);
                double newFieldValue = ParseFieldValue(trimmed);

                if (isTangential && currentFieldNumber > 0 && currentFieldNumber != newFieldNumber)
                {
                    fields.Add(new RayFanFieldData
                    {
                        FieldNumber = currentFieldNumber,
                        FieldValueDeg = currentFieldValue,
                        Tangential = currentTangential,
                        Sagittal = currentSagittal
                    });
                    currentTangential = null;
                    currentSagittal = null;
                }

                if (isTangential)
                {
                    currentFieldNumber = newFieldNumber;
                    currentFieldValue = newFieldValue;
                    currentFanType = "Tangential";
                }
                else if (isSagittal)
                {
                    currentFanType = "Sagittal";
                }

                wavelengths = null;
                pupils = new List<double>();
                aberrationColumns = new List<List<double>>();
                inData = false;
                continue;
            }

            if (trimmed.StartsWith("Pupil", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                var waveList = new List<double>();
                for (int j = 1; j < parts.Length; j++)
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
                pupils.Add(pupil);
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

        SaveCurrentFan(ref currentTangential, ref currentSagittal, currentFanType,
            wavelengths, pupils, aberrationColumns);

        if (currentFieldNumber > 0)
        {
            fields.Add(new RayFanFieldData
            {
                FieldNumber = currentFieldNumber,
                FieldValueDeg = currentFieldValue,
                Tangential = currentTangential,
                Sagittal = currentSagittal
            });
        }

        return new RayFanData
        {
            Success = true,
            Units = units,
            Surface = surface,
            Fields = fields.ToArray()
        };
    }

    private static void SaveCurrentFan(
        ref RayFanCurveData? tangential, ref RayFanCurveData? sagittal,
        string fanType, double[]? wavelengths,
        List<double> pupils, List<List<double>> aberrationColumns)
    {
        if (wavelengths == null || pupils.Count == 0) return;

        var curve = new RayFanCurveData
        {
            Wavelengths = wavelengths,
            PupilCoordinates = pupils.ToArray(),
            Aberration = aberrationColumns.Select(c => c.ToArray()).ToArray(),
            DataPoints = pupils.Count
        };

        if (fanType == "Tangential")
            tangential = curve;
        else if (fanType == "Sagittal")
            sagittal = curve;
    }

    private static int ParseFieldNumber(string line)
    {
        var idx = line.IndexOf("number", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;
        var rest = line.Substring(idx + 6).Trim();
        var parts = rest.Split([' ', '='], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && int.TryParse(parts[0], out int num))
            return num;
        return 0;
    }

    private static double ParseFieldValue(string line)
    {
        var idx = line.IndexOf('=');
        if (idx < 0) return 0;
        var rest = line.Substring(idx + 1).Trim();
        var parts = rest.Split([' ', '\t', '('], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            return val;
        return 0;
    }
}
