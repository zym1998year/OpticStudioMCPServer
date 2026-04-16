using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Analysis;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class GeometricImageAnalysisTool
{
    private readonly IZemaxSession _session;

    public GeometricImageAnalysisTool(IZemaxSession session) => _session = session;

    public record GiaResult(
        bool Success,
        string? Error = null,
        int Field = 0,
        int Pixels = 0,
        double ImageSize_mm = 0,
        double PeakIrradiance = 0,
        double TotalPower = 0,
        string? SettingsDebugInfo = null,
        string? TextFilePath = null,
        string? ImageFilePath = null,
        double[][]? IrradianceData = null);

    [McpServerTool(Name = "zemax_geometric_image_analysis")]
    [Description(
        "Run Geometric Image Analysis (IMA) on the current optical system. "
        + "Computes the geometric ray-based irradiance distribution at the image plane. "
        + "Can export results as TXT data file and/or BMP image file. "
        + "Returns peak irradiance, total power, and optionally the full 2D irradiance grid. "
        + "All settings parameters are optional — only explicitly passed values will be modified, "
        + "unspecified parameters keep their current/default values.")]
    public async Task<GiaResult> ExecuteAsync(
        [Description("Field number (1-indexed). Null = keep current.")] int? field = null,
        [Description("Wavelength number. Null = keep current (polychromatic).")] int? wavelength = null,
        [Description("Surface number to analyze. Null = keep current (image surface).")] int? surface = null,
        [Description("Number of pixels (square grid). Null = keep current.")] int? pixels = null,
        [Description("Number of rays x1000. Null = keep current.")] int? raysX1000 = null,
        [Description("Image size in mm. Null = keep current.")] double? imageSize = null,
        [Description("Display mode: 'GreyScale', 'FalseColor', 'SpotDiagram', 'CrossX', 'CrossY'. Null = keep current.")] string? showAs = null,
        [Description("Source type: 'Uniform', etc. Null = keep current.")] string? source = null,
        [Description("Reference type: 'ChiefRay', 'Centroid', 'Vertex'. Null = keep current.")] string? reference = null,
        [Description("Parity: 'Even', 'Odd'. Null = keep current.")] string? parity = null,
        [Description("IMA source file name (e.g. 'LETTERF.IMA'). Null = keep current.")] string? file = null,
        [Description("Field size override. Null = keep current.")] double? fieldSize = null,
        [Description("Numerical aperture override. Null = keep current.")] double? na = null,
        [Description("Image rotation in degrees. Null = keep current.")] double? rotation = null,
        [Description("Total watts override. Null = keep current.")] double? totalWatts = null,
        [Description("Row/column number parameter. Null = keep current.")] int? rowColumnNumber = null,
        [Description("Scatter rays toggle. Null = keep current.")] bool? scatterRays = null,
        [Description("Use symbols toggle. Null = keep current.")] bool? useSymbols = null,
        [Description("Use polarization toggle. Null = keep current.")] bool? usePolarization = null,
        [Description("Delete vignetted rays toggle. Null = keep current.")] bool? deleteVignetted = null,
        [Description("Remove vignetting factors toggle. Null = keep current.")] bool? removeVignettingFactors = null,
        [Description("Pixel interpolation toggle. Null = keep current.")] bool? usePixelInterpolation = null,
        [Description("File path to export TXT data (null to skip).")] string? exportTextPath = null,
        [Description("File path to export BMP graphic (null to skip).")] string? exportImagePath = null,
        [Description("Return the full irradiance data grid in the response (can be large).")] bool returnData = false,
        [Description("Return diagnostic information about the IMA settings object.")] bool debugSettings = false,
        [Description("Save the modified settings to IMA.CFG so they persist in desktop OpticStudio.")] bool saveSettings = false)
    {
        try
        {
            return await _session.ExecuteAsync("GeometricImageAnalysis", new Dictionary<string, object?>
            {
                ["field"] = field, ["wavelength"] = wavelength, ["surface"] = surface,
                ["pixels"] = pixels, ["raysX1000"] = raysX1000, ["imageSize"] = imageSize,
                ["showAs"] = showAs, ["source"] = source, ["reference"] = reference,
                ["parity"] = parity, ["file"] = file,
                ["saveSettings"] = saveSettings, ["debugSettings"] = debugSettings
            }, system =>
            {
                var analysis = system.Analyses.New_Analysis(AnalysisIDM.GeometricImageAnalysis);
                string? settingsDebugInfo = null;

                try
                {
                    object imaSettingsObj = analysis.GetSettings();
                    dynamic imaSettings = imaSettingsObj;

                    // --- Field / Wavelength / Surface (interface methods) ---
                    if (field.HasValue)
                        imaSettings.Field.SetFieldNumber(field.Value);
                    if (wavelength.HasValue)
                        imaSettings.Wavelength.SetWavelengthNumber(wavelength.Value);
                    if (surface.HasValue)
                        imaSettings.Surface.SetSurfaceNumber(surface.Value);

                    // --- Simple scalar/boolean properties (only set if explicitly passed) ---
                    TrySetSimpleProperty(imaSettingsObj, "FieldSize", fieldSize, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "NA", na, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "Rotation", rotation, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "TotalWatts", totalWatts, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "RowColumnNumber", rowColumnNumber, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "ScatterRays", scatterRays, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "UseSymbols", useSymbols, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "UsePolarization", usePolarization, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "DeleteVignetted", deleteVignetted, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "RemoveVignettingFactors", removeVignettingFactors, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "UsePixelInterpolation", usePixelInterpolation, debugSettings, ref settingsDebugInfo);
                    TrySetSimpleProperty(imaSettingsObj, "File", file, debugSettings, ref settingsDebugInfo);

                    // --- ImageSize (scalar Double in OpticStudio 2023) ---
                    if (imageSize.HasValue)
                    {
                        TrySetSimpleProperty(imaSettingsObj, "ImageSize", imageSize, debugSettings, ref settingsDebugInfo);
                    }

                    // --- NumberOfPixels (single Int32, square grid) ---
                    if (pixels.HasValue)
                    {
                        TrySetSimpleProperty(imaSettingsObj, "NumberOfPixels", pixels, debugSettings, ref settingsDebugInfo);
                    }

                    // --- RaysX1000 ---
                    if (raysX1000.HasValue)
                    {
                        TrySetSimpleProperty(imaSettingsObj, "RaysX1000", raysX1000, debugSettings, ref settingsDebugInfo);
                    }

                    // --- Enum properties (ShowAs, Source, Reference, Parity) ---
                    TrySetEnumProperty(imaSettingsObj, "ShowAs", showAs, debugSettings, ref settingsDebugInfo);
                    TrySetEnumProperty(imaSettingsObj, "Source", source, debugSettings, ref settingsDebugInfo);
                    TrySetEnumProperty(imaSettingsObj, "Reference", reference, debugSettings, ref settingsDebugInfo);
                    TrySetEnumProperty(imaSettingsObj, "Parity", parity, debugSettings, ref settingsDebugInfo);

                    // Capture settings AFTER all modifications
                    if (debugSettings)
                        settingsDebugInfo = DescribeSettingsObject(imaSettingsObj);

                    // Save modified settings
                    if (saveSettings)
                    {
                        try
                        {
                            var zemaxDataDir = _session.ZemaxDataDir;
                            if (!string.IsNullOrEmpty(zemaxDataDir))
                            {
                                // Save to global IMA.CFG (default for new IMA windows)
                                var imaCfgPath = Path.Combine(zemaxDataDir, "Configs", "IMA.CFG");
                                ((dynamic)imaSettingsObj).SaveTo(imaCfgPath);
                                PatchCfgVersion(imaCfgPath);

                                // Save to per-file .CFG (overrides global for this specific file)
                                var currentFile = _session.CurrentFilePath;
                                if (!string.IsNullOrEmpty(currentFile))
                                {
                                    var perFileCfg = Path.ChangeExtension(currentFile, ".CFG");
                                    ((dynamic)imaSettingsObj).SaveTo(perFileCfg);
                                    PatchCfgVersion(perFileCfg);
                                }
                            }
                            else
                            {
                                ((dynamic)imaSettingsObj).Save();
                            }
                        }
                        catch { /* Save may not be available in all versions */ }
                    }
                }
                catch (Exception ex)
                {
                    if (debugSettings)
                        settingsDebugInfo = $"Settings access failed: {ex.GetType().FullName}: {ex.Message}";
                }

                analysis.ApplyAndWaitForCompletion();

                var results = analysis.GetResults();

                // Export text file
                if (!string.IsNullOrEmpty(exportTextPath))
                {
                    EnsureDirectory(exportTextPath);
                    results.GetTextFile(exportTextPath);
                }

                // Export image (generate BMP from data grid)
                if (!string.IsNullOrEmpty(exportImagePath))
                {
                    EnsureDirectory(exportImagePath);
                    AnalysisBmpHelper.TryExportBmp(results, exportImagePath);
                }

                // Extract data grid
                double[][]? irradianceData = null;
                double peakIrradiance = 0;
                double totalPower = 0;
                int actualPix = 0;
                double actSize = 0;

                try
                {
                    var dataGrid = results.GetDataGrid(0);
                    if (dataGrid != null)
                    {
                        actualPix = (int)dataGrid.Nx;
                        actSize = dataGrid.Dx * actualPix;

                        if (returnData)
                        {
                            int ny = (int)dataGrid.Ny;
                            irradianceData = new double[ny][];
                            for (int j = 0; j < ny; j++)
                            {
                                irradianceData[j] = new double[actualPix];
                                for (int i = 0; i < actualPix; i++)
                                {
                                    double val = dataGrid.Z(i, j);
                                    irradianceData[j][i] = val;
                                    if (val > peakIrradiance) peakIrradiance = val;
                                    totalPower += val;
                                }
                            }
                        }
                        else
                        {
                            int ny = (int)dataGrid.Ny;
                            for (int j = 0; j < ny; j++)
                                for (int i = 0; i < actualPix; i++)
                                {
                                    double val = dataGrid.Z(i, j);
                                    if (val > peakIrradiance) peakIrradiance = val;
                                    totalPower += val;
                                }
                        }
                    }
                }
                catch { /* data grid may not be available */ }

                analysis.Close();

                return new GiaResult(
                    Success: true,
                    Field: field ?? 0,
                    Pixels: actualPix,
                    ImageSize_mm: actSize,
                    PeakIrradiance: peakIrradiance,
                    TotalPower: totalPower,
                    SettingsDebugInfo: settingsDebugInfo,
                    TextFilePath: exportTextPath,
                    ImageFilePath: exportImagePath,
                    IrradianceData: irradianceData);
            });
        }
        catch (Exception ex)
        {
            return new GiaResult(false, Error: ex.Message);
        }
    }

    private static void PatchCfgVersion(string cfgPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(cfgPath);
            if (bytes.Length > 0x0C && bytes[0x0C] != 0x03)
            {
                bytes[0x0C] = 0x03;
                File.WriteAllBytes(cfgPath, bytes);
            }
        }
        catch { /* best-effort patch */ }
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static string DescribeSettingsObject(object settingsObj)
    {
        var type = settingsObj.GetType();
        var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p =>
            {
                try
                {
                    var val = p.GetValue(settingsObj);
                    return $"{p.PropertyType.Name} {p.Name} = {val}";
                }
                catch
                {
                    return $"{p.PropertyType.Name} {p.Name} = <error>";
                }
            })
            .OrderBy(s => s)
            .ToArray();
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))
            .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})")
            .OrderBy(s => s)
            .ToArray();

        return string.Join(
            Environment.NewLine,
            new[]
            {
                $"Settings runtime type: {type.FullName}",
                "Properties:",
                props.Length > 0 ? string.Join(Environment.NewLine, props) : "<none>",
                "Methods:",
                methods.Length > 0 ? string.Join(Environment.NewLine, methods) : "<none>"
            });
    }

    private static string AppendDebug(string? existing, string addition)
    {
        return string.IsNullOrEmpty(existing) ? addition : existing + Environment.NewLine + addition;
    }

    private static void TrySetSimpleProperty<T>(object settingsObj, string propertyName, T? value, bool debugSettings, ref string? settingsDebugInfo)
    {
        if (value == null)
            return;

        try
        {
            var prop = settingsObj.GetType().GetProperty(propertyName);
            if (prop == null || !prop.CanWrite)
            {
                if (debugSettings)
                    settingsDebugInfo = AppendDebug(settingsDebugInfo, $"Property not writable or missing: {propertyName}");
                return;
            }

            object boxed = value!;
            if (prop.PropertyType != boxed.GetType())
            {
                boxed = Convert.ChangeType(boxed, prop.PropertyType);
            }

            prop.SetValue(settingsObj, boxed);
        }
        catch (Exception ex)
        {
            if (debugSettings)
                settingsDebugInfo = AppendDebug(settingsDebugInfo, $"{propertyName} set failed: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    private static void TrySetEnumProperty(object settingsObj, string propertyName, string? value, bool debugSettings, ref string? settingsDebugInfo)
    {
        if (string.IsNullOrEmpty(value))
            return;

        try
        {
            var prop = settingsObj.GetType().GetProperty(propertyName);
            if (prop == null || !prop.CanWrite)
            {
                if (debugSettings)
                    settingsDebugInfo = AppendDebug(settingsDebugInfo, $"Enum property not writable or missing: {propertyName}");
                return;
            }

            var enumValue = Enum.Parse(prop.PropertyType, value, ignoreCase: true);
            prop.SetValue(settingsObj, enumValue);
        }
        catch (Exception ex)
        {
            if (debugSettings)
                settingsDebugInfo = AppendDebug(settingsDebugInfo, $"{propertyName} set failed: {ex.GetType().FullName}: {ex.Message}");
        }
    }
}
