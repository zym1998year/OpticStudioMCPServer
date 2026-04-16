using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Analysis;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class ExportAnalysisTool
{
    private readonly IZemaxSession _session;

    public ExportAnalysisTool(IZemaxSession session) => _session = session;

    public record ExportResult(
        bool Success,
        string? Error = null,
        string? AnalysisType = null,
        string? ImagePath = null,
        string? TextPath = null);

    [McpServerTool(Name = "zemax_export_analysis")]
    [Description(
        "Run any Zemax analysis and export the result as an image (BMP) and/or text file. "
        + "Supported types: 'StandardSpot', 'MatrixSpot', 'FftMtf', 'GeometricMtf', "
        + "'FftPsf', 'HuygensPsf', 'GeometricImageAnalysis', 'RayFan', 'OpdFan', "
        + "'WavefrontMap', 'SeidelDiagram', 'FieldCurvature', 'LongitudinalAberration', "
        + "'LateralColor', 'FocalShiftDiagram', 'Draw2D', 'Draw3D'. "
        + "Aliases accepted: 'spot', 'mtf', 'psf', 'ima', 'layout', etc.")]
    public async Task<ExportResult> ExecuteAsync(
        [Description("Analysis type name (e.g., 'StandardSpot', 'FftMtf', 'Draw2D', 'ima')")] string analysisType,
        [Description("File path for the exported image (.bmp)")] string imagePath,
        [Description("Optional file path for text data (.txt)")] string? textPath = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["analysisType"] = analysisType,
                ["imagePath"] = imagePath,
                ["textPath"] = textPath
            };

            return await _session.ExecuteAsync("ExportAnalysis", parameters, system =>
            {
                if (!TryParseAnalysisType(analysisType, out var analysisId))
                {
                    return new ExportResult(false,
                        Error: $"Unknown analysis type: '{analysisType}'. "
                            + "Use names like 'StandardSpot', 'FftMtf', 'Draw2D', etc.");
                }

                var analysis = system.Analyses.New_Analysis(analysisId);
                analysis.ApplyAndWaitForCompletion();

                var results = analysis.GetResults();

                // Export BMP from data grid (ZOSAPI has no built-in image export in standalone mode)
                string? actualImagePath = null;
                EnsureDirectory(imagePath);
                if (AnalysisBmpHelper.TryExportBmp(results, imagePath))
                {
                    actualImagePath = imagePath;
                }

                // Export text data
                string? actualTextPath = null;
                if (!string.IsNullOrEmpty(textPath))
                {
                    EnsureDirectory(textPath);
                    results.GetTextFile(textPath);
                    if (File.Exists(textPath))
                        actualTextPath = textPath;
                }

                // If no BMP was generated and no text was requested, export text to imagePath as fallback
                if (actualImagePath == null && actualTextPath == null)
                {
                    var fallbackPath = Path.ChangeExtension(imagePath, ".txt");
                    analysis.ToFile(fallbackPath, false, false);
                }

                analysis.Close();

                return new ExportResult(
                    Success: true,
                    AnalysisType: analysisType,
                    ImagePath: actualImagePath,
                    TextPath: actualTextPath);
            });
        }
        catch (Exception ex)
        {
            return new ExportResult(false, Error: ex.Message);
        }
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static bool TryParseAnalysisType(string name, out AnalysisIDM result)
    {
        if (Enum.TryParse(name, ignoreCase: true, out result))
            return true;

        var key = name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
        result = key switch
        {
            "standardspot" or "spotdiagram" or "spot" => AnalysisIDM.StandardSpot,
            "matrixspot" => AnalysisIDM.MatrixSpot,
            "fftmtf" or "mtf" => AnalysisIDM.FftMtf,
            "geometricmtf" or "geomtf" => AnalysisIDM.GeometricMtf,
            "fftpsf" or "psf" => AnalysisIDM.FftPsf,
            "huygenspsf" => AnalysisIDM.HuygensPsf,
            "geometricimageanalysis" or "ima" or "gia" => AnalysisIDM.GeometricImageAnalysis,
            "rayfan" or "transverseray" => AnalysisIDM.RayFan,
            "opdfan" or "opd" => AnalysisIDM.OpticalPathFan,
            "wavefrontmap" or "wavefront" => AnalysisIDM.WavefrontMap,
            "seidel" or "seideldiagram" => AnalysisIDM.SeidelDiagram,
            "fieldcurvature" or "distortion" => AnalysisIDM.FieldCurvatureAndDistortion,
            "longitudinalaberration" => AnalysisIDM.LongitudinalAberration,
            "lateralcolor" => AnalysisIDM.LateralColor,
            "focalshift" or "chromaticfocalshift" or "focalshiftdiagram" => AnalysisIDM.FocalShiftDiagram,
            "draw2d" or "layout2d" or "layout" => AnalysisIDM.Draw2D,
            "draw3d" or "layout3d" => AnalysisIDM.Draw3D,
            "fftmtfvsfield" or "mtfvsfield" => AnalysisIDM.FftMtfvsField,
            "fftthroughfocusmtf" or "throughfocusmtf" => AnalysisIDM.FftThroughFocusMtf,
            "relativeillumination" => AnalysisIDM.RelativeIllumination,
            "interferogram" => AnalysisIDM.Interferogram,
            _ => (AnalysisIDM)(-1),
        };

        return (int)result != -1;
    }
}
