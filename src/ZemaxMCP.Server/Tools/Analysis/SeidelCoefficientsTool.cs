using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class SeidelCoefficientsTool
{
    private readonly IZemaxSession _session;

    public SeidelCoefficientsTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_seidel_coefficients")]
    [Description("Get Seidel (3rd order) aberration coefficients for each surface and the total system. Returns S1 (spherical), S2 (coma), S3 (astigmatism), S4 (field curvature), S5 (distortion), CL (axial chromatic), CT (lateral chromatic), plus wavefront summary.")]
    public async Task<SeidelCoefficientsData> ExecuteAsync(
        [Description("Wavelength number (0 for primary)")] int wavelength = 0)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["wavelength"] = wavelength
            };

            return await _session.ExecuteAsync("SeidelCoefficients", parameters, system =>
            {
                var seidel = system.Analyses.New_SeidelCoefficients();
                try
                {
                    var settings = seidel.GetSettings();
                    if (settings != null && wavelength > 0)
                    {
                        var seidelSettings = settings as ZOSAPI.Analysis.Settings.Aberrations.IAS_SeidelCoefficients;
                        if (seidelSettings != null)
                        {
                            seidelSettings.Wavelength.SetWavelengthNumber(wavelength);
                        }
                    }

                    seidel.ApplyAndWaitForCompletion();

                    var tempFile = Path.Combine(Path.GetTempPath(), $"zemax_seidel_{Guid.NewGuid():N}.txt");
                    try
                    {
                        seidel.GetResults().GetTextFile(tempFile);
                        return ParseSeidelTextFile(tempFile);
                    }
                    finally
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }
                finally
                {
                    seidel.Close();
                }
            });
        }
        catch (Exception ex)
        {
            return new SeidelCoefficientsData
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static SeidelCoefficientsData ParseSeidelTextFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);

        double wavelength = 0, petzval = 0, invariant = 0;
        double chiefObj = 0, chiefImg = 0, margObj = 0, margImg = 0;

        var surfaceRows = new List<SeidelSurfaceRow>();
        SeidelSurfaceRow? totalRow = null;
        SeidelWavefrontSummary? wavefrontSummary = null;

        bool inSeidelCoeffs = false;
        bool inWavefrontSummary = false;
        bool pastSeidelHeader = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            // Parse header values
            if (trimmed.StartsWith("Wavelength", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(':'))
            {
                wavelength = ParseHeaderValue(trimmed);
                continue;
            }
            if (trimmed.StartsWith("Chief Ray Slope, Object", StringComparison.OrdinalIgnoreCase))
            {
                chiefObj = ParseHeaderValue(trimmed);
                continue;
            }
            if (trimmed.StartsWith("Chief Ray Slope, Image", StringComparison.OrdinalIgnoreCase))
            {
                chiefImg = ParseHeaderValue(trimmed);
                continue;
            }
            if (trimmed.StartsWith("Marginal Ray Slope, Object", StringComparison.OrdinalIgnoreCase))
            {
                margObj = ParseHeaderValue(trimmed);
                continue;
            }
            if (trimmed.StartsWith("Marginal Ray Slope, Image", StringComparison.OrdinalIgnoreCase))
            {
                margImg = ParseHeaderValue(trimmed);
                continue;
            }
            if (trimmed.StartsWith("Petzval radius", StringComparison.OrdinalIgnoreCase))
            {
                petzval = ParseHeaderValue(trimmed);
                continue;
            }
            if (trimmed.StartsWith("Optical Invariant", StringComparison.OrdinalIgnoreCase))
            {
                invariant = ParseHeaderValue(trimmed);
                continue;
            }

            // Detect section headers
            if (trimmed.StartsWith("Seidel Aberration Coefficients:", StringComparison.OrdinalIgnoreCase) &&
                trimmed.IndexOf("Waves", StringComparison.OrdinalIgnoreCase) < 0)
            {
                inSeidelCoeffs = true;
                inWavefrontSummary = false;
                pastSeidelHeader = false;
                continue;
            }

            // Stop parsing Seidel section when we hit the next section
            if (inSeidelCoeffs && trimmed.StartsWith("Seidel Aberration Coefficients in Waves", StringComparison.OrdinalIgnoreCase))
            {
                inSeidelCoeffs = false;
                continue;
            }

            if (trimmed.StartsWith("Wavefront Aberration Coefficient Summary", StringComparison.OrdinalIgnoreCase))
            {
                inSeidelCoeffs = false;
                inWavefrontSummary = true;
                continue;
            }

            // Parse Seidel coefficients table
            if (inSeidelCoeffs)
            {
                // Skip the column header line
                if (trimmed.StartsWith("Surf", StringComparison.OrdinalIgnoreCase))
                {
                    pastSeidelHeader = true;
                    continue;
                }

                if (!pastSeidelHeader || string.IsNullOrWhiteSpace(trimmed))
                    continue;

                var row = ParseSeidelRow(trimmed);
                if (row != null)
                {
                    if (row.Surface.Equals("TOT", StringComparison.OrdinalIgnoreCase))
                        totalRow = row;
                    else
                        surfaceRows.Add(row);
                }
            }

            // Parse wavefront summary
            if (inWavefrontSummary)
            {
                if (trimmed.StartsWith("TOT", StringComparison.OrdinalIgnoreCase) && wavefrontSummary == null)
                {
                    var values = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length >= 8)
                    {
                        wavefrontSummary = new SeidelWavefrontSummary
                        {
                            W040 = ParseDouble(values, 1),
                            W131 = ParseDouble(values, 2),
                            W222 = ParseDouble(values, 3),
                            W220P = ParseDouble(values, 4),
                            W311 = ParseDouble(values, 5),
                            W020 = ParseDouble(values, 6),
                            W111 = ParseDouble(values, 7)
                        };
                    }
                }
                // Second TOT line with W220S, W220M, W220T
                else if (trimmed.StartsWith("TOT", StringComparison.OrdinalIgnoreCase) && wavefrontSummary != null)
                {
                    var values = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length >= 4)
                    {
                        wavefrontSummary = wavefrontSummary with
                        {
                            W220S = ParseDouble(values, 1),
                            W220M = ParseDouble(values, 2),
                            W220T = ParseDouble(values, 3)
                        };
                    }
                    inWavefrontSummary = false;
                }
            }
        }

        return new SeidelCoefficientsData
        {
            Success = true,
            Wavelength = wavelength,
            PetzvalRadius = petzval,
            OpticalInvariant = invariant,
            ChiefRaySlopeObject = chiefObj,
            ChiefRaySlopeImage = chiefImg,
            MarginalRaySlopeObject = margObj,
            MarginalRaySlopeImage = margImg,
            SurfaceCoefficients = surfaceRows.ToArray(),
            Total = totalRow,
            WavefrontSummary = wavefrontSummary
        };
    }

    private static double ParseHeaderValue(string line)
    {
        int colonIdx = line.LastIndexOf(':');
        if (colonIdx < 0) return 0;
        var valueStr = line.Substring(colonIdx + 1).Trim().Split(' ')[0];
        if (double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            return val;
        return 0;
    }

    private static SeidelSurfaceRow? ParseSeidelRow(string line)
    {
        var values = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (values.Length < 8) return null;

        return new SeidelSurfaceRow
        {
            Surface = values[0],
            S1_SPHA = ParseDouble(values, 1),
            S2_COMA = ParseDouble(values, 2),
            S3_ASTI = ParseDouble(values, 3),
            S4_FCUR = ParseDouble(values, 4),
            S5_DIST = ParseDouble(values, 5),
            CL_CLA = ParseDouble(values, 6),
            CT_CTR = ParseDouble(values, 7)
        };
    }

    private static double ParseDouble(string[] values, int index)
    {
        if (index < values.Length &&
            double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            return val;
        return 0;
    }
}
