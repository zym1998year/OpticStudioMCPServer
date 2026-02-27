using System.Globalization;
using ZemaxMCP.Core.Models;

namespace ZemaxMCP.Core.Services.GlassCatalog;

public static class AgfFileParser
{
    public static Dictionary<string, string> DiscoverCatalogs(string glassCatDir)
    {
        var catalogs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(glassCatDir) || !Directory.Exists(glassCatDir))
            return catalogs;

        foreach (var agfFile in Directory.GetFiles(glassCatDir, "*.agf"))
        {
            string catalogName = Path.GetFileNameWithoutExtension(agfFile);
            catalogs[catalogName] = agfFile;
        }

        return catalogs;
    }

    public static List<GlassEntry> ParseCatalog(string agfPath, string catalogName)
    {
        var glasses = new List<GlassEntry>();

        if (!File.Exists(agfPath))
            return glasses;

        string[] lines = File.ReadAllLines(agfPath);
        GlassEntry? current = null;
        int formula = 0;
        double vd = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("NM "))
            {
                if (current != null)
                    glasses.Add(current);

                var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                string name = parts.Length > 1 ? parts[1] : "";
                formula = parts.Length > 2 ? ParseInt(parts[2]) : 0;
                double nd = parts.Length > 4 ? ParseDouble(parts[4]) : 1.5;
                vd = parts.Length > 5 ? ParseDouble(parts[5]) : 50.0;
                int status = parts.Length > 7 ? ParseInt(parts[7]) : 0;
                int meltFreq = parts.Length > 8 ? ParseInt(parts[8]) : -1;

                current = new GlassEntry
                {
                    Name = name,
                    DispersionFormula = formula,
                    Nd = nd,
                    Vd = vd,
                    Status = status,
                    MeltFrequency = meltFreq >= 1 && meltFreq <= 5 ? meltFreq : -1,
                    CatalogName = catalogName
                };
                current.RawLines.Add(line);
            }
            else if (current != null)
            {
                current.RawLines.Add(line);

                if (trimmed.StartsWith("CD "))
                {
                    var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    var coefficients = new double[parts.Length - 1];
                    for (int j = 1; j < parts.Length; j++)
                        coefficients[j - 1] = ParseDouble(parts[j]);

                    current.DispersionCoefficients = coefficients;

                    if (coefficients.Length > 0 && formula > 0)
                        current.DPgF = DispersionCalculator.ComputeDPgF(formula, coefficients, vd);
                }
                else if (trimmed.StartsWith("ED "))
                {
                    var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) current.TCE = ParseDouble(parts[1]);
                    if (parts.Length > 2) current.TCE2 = ParseDouble(parts[2]);
                    if (parts.Length > 3) current.Density = ParseDouble(parts[3]);
                }
                else if (trimmed.StartsWith("TD "))
                {
                    var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    var thermalCoeffs = new double[Math.Min(parts.Length - 1, 7)];
                    for (int j = 1; j < parts.Length && j <= 7; j++)
                        thermalCoeffs[j - 1] = ParseDouble(parts[j]);
                    current.ThermalCoefficients = thermalCoeffs;
                }
                else if (trimmed.StartsWith("LD "))
                {
                    var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) current.MinWavelength = ParseDouble(parts[1]);
                    if (parts.Length > 2) current.MaxWavelength = ParseDouble(parts[2]);
                }
                else if (trimmed.StartsWith("OD "))
                {
                    var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) current.RelativeCost = ParseOdValue(parts[1]);
                    if (parts.Length > 2) current.CR = ParseOdValue(parts[2]);
                    if (parts.Length > 3) current.FR = ParseOdValue(parts[3]);
                    if (parts.Length > 4) current.SR = ParseOdValue(parts[4]);
                    if (parts.Length > 5) current.AR = ParseOdValue(parts[5]);
                    if (parts.Length > 6) current.PR = ParseOdValue(parts[6]);
                }
                else if (trimmed.StartsWith("GC "))
                {
                    current.Comment = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : "";
                }
            }
        }

        if (current != null)
            glasses.Add(current);

        return glasses;
    }

    private static double ParseOdValue(string s)
    {
        if (s is "-" or "_")
            return -1;
        return ParseDouble(s);
    }

    private static double ParseDouble(string s)
    {
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double result);
        return result;
    }

    private static int ParseInt(string s)
    {
        int.TryParse(s, out int result);
        return result;
    }
}
