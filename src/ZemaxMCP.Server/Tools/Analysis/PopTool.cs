using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Analysis;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class PopTool
{
    private readonly IZemaxSession _session;

    public PopTool(IZemaxSession session) => _session = session;

    public record PopResult(
        bool Success,
        string? Error = null,
        int StartSurface = 0,
        int EndSurface = 0,
        int Wavelength = 0,
        int Field = 0,
        string? BeamType = null,
        string? DataType = null,
        double PeakIrradiance = 0,
        double TotalPower = 0,
        double GridWidthX = 0,
        double GridWidthY = 0,
        double PixelPitchX = 0,
        double PixelPitchY = 0,
        int Nx = 0,
        int Ny = 0,
        double[][]? Grid = null,
        string? GridFilePath = null,
        string? BmpFilePath = null);

    // Inline threshold: 256x256 = 65536 cells ~ 2 MB JSON. Larger grids must write to disk.
    private const int InlineGridCellLimit = 65536;

    [McpServerTool(Name = "zemax_pop")]
    [Description(
        "Run Physical Optics Propagation and return the intensity (or phase) grid. "
        + "Used for wavefront sensor donut simulation: add Zernike phase via zemax_set_extra_data, "
        + "shift focus, then call this tool to get the defocused intensity pattern. "
        + "Grid <= 256x256 returns inline; larger grids require outputGridPath (raw float64 little-endian "
        + "with 24-byte header: int32 Nx | int32 Ny | float64 Dx | float64 Dy, then Ny*Nx*8 bytes row-major). "
        + "All linear units are lens units (usually mm).")]
    public async Task<PopResult> ExecuteAsync(
        [Description("Start surface for POP")] int startSurface = 1,
        [Description("End surface; -1 = image")] int endSurface = -1,
        [Description("Wavelength number (1-indexed)")] int wavelength = 1,
        [Description("Field number (1-indexed)")] int field = 1,
        [Description("Beam type: GaussianWaist, GaussianAngle, GaussianSize, TopHat, FileBeam, etc.")] string beamType = "GaussianWaist",
        [Description("Beam param 1 (Waist X or beam-type-specific first param, in lens units)")] double beamParam1 = 0,
        [Description("Beam param 2 (Waist Y)")] double beamParam2 = 0,
        [Description("Beam param 3 (Decenter X)")] double beamParam3 = 0,
        [Description("Beam param 4 (Decenter Y)")] double beamParam4 = 0,
        [Description("X sampling: 1=32,2=64,3=128,4=256,5=512,6=1024")] int xSampling = 5,
        [Description("Y sampling: same scale as xSampling")] int ySampling = 5,
        [Description("X width in lens units (0 = auto when autoCalculate=true)")] double xWidth = 0,
        [Description("Y width in lens units (0 = auto when autoCalculate=true)")] double yWidth = 0,
        [Description("Use Zemax auto sampling/width")] bool autoCalculate = true,
        [Description("Data type: Irradiance, PhaseRadians, RealPart, ImagPart, Ex, Ey")] string dataType = "Irradiance",
        [Description("Peak-irradiance normalization")] bool peakNormalize = false,
        [Description("Optional path to write raw grid (required if Nx*Ny > 65536)")] string? outputGridPath = null,
        [Description("Optional path to export BMP image")] string? exportBmpPath = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["startSurface"] = startSurface,
                ["endSurface"] = endSurface,
                ["wavelength"] = wavelength,
                ["field"] = field,
                ["beamType"] = beamType,
                ["xSampling"] = xSampling,
                ["ySampling"] = ySampling,
                ["xWidth"] = xWidth,
                ["yWidth"] = yWidth,
                ["autoCalculate"] = autoCalculate,
                ["dataType"] = dataType,
                ["peakNormalize"] = peakNormalize,
                ["outputGridPath"] = outputGridPath,
                ["exportBmpPath"] = exportBmpPath
            };

            return await _session.ExecuteAsync("Pop", parameters, system =>
            {
                var analysis = system.Analyses.New_Analysis(AnalysisIDM.PhysicalOpticsPropagation);
                try
                {
                    // Settings configuration via dynamic to tolerate ZOSAPI version drift
                    dynamic settings = analysis.GetSettings();

                    TrySet(() => settings.Wavelength = wavelength);
                    TrySet(() => settings.Field = field);
                    TrySet(() => settings.StartSurface = startSurface);
                    TrySet(() => settings.EndSurface = endSurface);
                    TrySet(() => settings.AutoCalculate = autoCalculate);
                    TrySet(() => settings.PeakIrradiance = peakNormalize);

                    TrySet(() => settings.XSampling = MapSampling(xSampling));
                    TrySet(() => settings.YSampling = MapSampling(ySampling));

                    if (!autoCalculate)
                    {
                        TrySet(() => settings.XWidth = xWidth);
                        TrySet(() => settings.YWidth = yWidth);
                    }

                    // Beam type — try enum parse across common ZOSAPI names
                    if (!TrySetBeamType(settings, beamType, out string beamTypeError))
                        return new PopResult(false, Error: beamTypeError);

                    TrySet(() => settings.BeamParameter1 = beamParam1);
                    TrySet(() => settings.BeamParameter2 = beamParam2);
                    TrySet(() => settings.BeamParameter3 = beamParam3);
                    TrySet(() => settings.BeamParameter4 = beamParam4);

                    if (!TrySetDataType(settings, dataType, out string dataTypeError))
                        return new PopResult(false, Error: dataTypeError);

                    analysis.ApplyAndWaitForCompletion();

                    var results = analysis.GetResults();
                    dynamic resultsDyn = results;

                    // Extract grid via dynamic (GetDataGrid / GetDataGridDouble)
                    dynamic? grid = null;
                    try { grid = resultsDyn.DataGrids != null && resultsDyn.NumberOfDataGrids > 0 ? resultsDyn.DataGrids[0] : null; }
                    catch { }
                    if (grid == null)
                    {
                        try { grid = resultsDyn.GetDataGrid(0); }
                        catch { }
                    }
                    if (grid == null)
                    {
                        try { grid = resultsDyn.GetDataGridDouble(0); }
                        catch { }
                    }

                    if (grid == null)
                        return new PopResult(false,
                            Error: "POP analysis produced no data grid. Check startSurface/endSurface and beam settings.");

                    int nx = (int)grid.Nx;
                    int ny = (int)grid.Ny;
                    double dx = (double)grid.Dx;
                    double dy = (double)grid.Dy;
                    double widthX = dx * nx;
                    double widthY = dy * ny;

                    double peak = 0, total = 0;
                    try { peak = (double)grid.PeakValue; } catch { }
                    try { total = (double)grid.Total; } catch { }

                    // Probe once to determine the correct accessor shape.
                    // Sibling AnalysisBmpHelper uses grid.Z(x, y) for IAR_DataGrid — try it first.
                    // Fall back to Values[y,x] (2D indexer) then Values(y,x) (method) for version drift.
                    Func<int, int, double>? reader = null;
                    try { _ = (double)grid.Z(0, 0); reader = (y, x) => (double)grid.Z(x, y); }
                    catch
                    {
                        try { _ = (double)grid.Values[0, 0]; reader = (y, x) => (double)grid.Values[y, x]; }
                        catch
                        {
                            try { _ = (double)grid.Values(0, 0); reader = (y, x) => (double)grid.Values(y, x); }
                            catch { }
                        }
                    }
                    if (reader == null)
                        return new PopResult(false,
                            Error: "Unable to read POP data grid: none of Z(x,y), Values[y,x], Values(y,x) worked.");

                    var values2d = new double[ny][];
                    for (int y = 0; y < ny; y++)
                    {
                        values2d[y] = new double[nx];
                        for (int x = 0; x < nx; x++)
                            values2d[y][x] = reader(y, x);
                    }

                    // Decide inline vs file output
                    int cells = nx * ny;
                    double[][]? inlineGrid = null;
                    string? gridFilePath = null;

                    if (cells <= InlineGridCellLimit && string.IsNullOrEmpty(outputGridPath))
                    {
                        inlineGrid = values2d;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(outputGridPath))
                            return new PopResult(false,
                                Error: $"Grid {nx}x{ny}={cells} exceeds inline limit {InlineGridCellLimit}. Provide outputGridPath.");

                        EnsureDirectory(outputGridPath);
                        WriteGridBin(outputGridPath!, nx, ny, dx, dy, values2d);
                        gridFilePath = outputGridPath;
                    }

                    // Optional BMP export
                    string? bmpPath = null;
                    if (!string.IsNullOrEmpty(exportBmpPath))
                    {
                        EnsureDirectory(exportBmpPath);
                        if (AnalysisBmpHelper.TryExportBmp(results, exportBmpPath!))
                            bmpPath = exportBmpPath;
                    }

                    return new PopResult(
                        Success: true,
                        StartSurface: startSurface,
                        EndSurface: endSurface,
                        Wavelength: wavelength,
                        Field: field,
                        BeamType: beamType,
                        DataType: dataType,
                        PeakIrradiance: peak,
                        TotalPower: total,
                        GridWidthX: widthX,
                        GridWidthY: widthY,
                        PixelPitchX: dx,
                        PixelPitchY: dy,
                        Nx: nx,
                        Ny: ny,
                        Grid: inlineGrid,
                        GridFilePath: gridFilePath,
                        BmpFilePath: bmpPath);
                }
                finally
                {
                    analysis.Close();
                }
            });
        }
        catch (Exception ex)
        {
            return new PopResult(false, Error: ex.Message);
        }
    }

    private static void TrySet(Action setter)
    {
        try { setter(); } catch { /* property not supported in this ZOSAPI version; skip */ }
    }

    private static bool TrySetBeamType(dynamic settings, string beamType, out string error)
    {
        error = "";
        // Try on multiple candidate enum types and property names
        string[] enumTypeNames =
        {
            "ZOSAPI.Analysis.Settings.PhysicalOptics.POPBeamTypes",
            "ZOSAPI.Analysis.Settings.POP.POPBeamTypes",
            "ZOSAPI.Analysis.PhysicalOpticsPropagation.POPBeamTypes"
        };
        string[] propertyNames = { "BeamType", "SourceBeamType", "InputBeamType" };

        foreach (var tn in enumTypeNames)
        {
            var enumType = Type.GetType(tn + ", ZOSAPI");
            if (enumType == null) continue;
            if (!TryParseEnumCaseInsensitive(enumType, beamType, out object? enumVal)) continue;

            foreach (var pn in propertyNames)
            {
                try
                {
                    var prop = ((object)settings).GetType().GetProperty(pn);
                    if (prop == null) continue;
                    prop.SetValue(settings, enumVal);
                    return true;
                }
                catch { }
            }
        }

        // Fallback: try assigning the string directly (some ZOSAPI versions accept string setters)
        try { settings.BeamType = beamType; return true; } catch { }

        error = $"Unable to set beam type '{beamType}'. Tried enums POPBeamTypes in multiple namespaces and properties BeamType/SourceBeamType/InputBeamType.";
        return false;
    }

    private static bool TrySetDataType(dynamic settings, string dataType, out string error)
    {
        error = "";
        string[] enumTypeNames =
        {
            "ZOSAPI.Analysis.Settings.PhysicalOptics.POPDataTypes",
            "ZOSAPI.Analysis.Settings.POP.POPDataTypes",
            "ZOSAPI.Analysis.PhysicalOpticsPropagation.POPDataTypes"
        };
        string[] propertyNames = { "DataType", "OutputDataType" };

        foreach (var tn in enumTypeNames)
        {
            var enumType = Type.GetType(tn + ", ZOSAPI");
            if (enumType == null) continue;
            if (!TryParseEnumCaseInsensitive(enumType, dataType, out object? enumVal)) continue;

            foreach (var pn in propertyNames)
            {
                try
                {
                    var prop = ((object)settings).GetType().GetProperty(pn);
                    if (prop == null) continue;
                    prop.SetValue(settings, enumVal);
                    return true;
                }
                catch { }
            }
        }

        try { settings.DataType = dataType; return true; } catch { }

        error = $"Unable to set data type '{dataType}'.";
        return false;
    }

    /// <summary>
    /// Case-insensitive enum parse compatible with .NET Framework 4.8
    /// (which lacks the generic non-generic TryParse ignoreCase overload).
    /// </summary>
    private static bool TryParseEnumCaseInsensitive(Type enumType, string value, out object? result)
    {
        result = null;
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            foreach (var name in Enum.GetNames(enumType))
            {
                if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
                {
                    result = Enum.Parse(enumType, name);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static ZOSAPI.Analysis.SampleSizes MapSampling(int sampling) => sampling switch
    {
        1 => ZOSAPI.Analysis.SampleSizes.S_32x32,
        2 => ZOSAPI.Analysis.SampleSizes.S_64x64,
        3 => ZOSAPI.Analysis.SampleSizes.S_128x128,
        4 => ZOSAPI.Analysis.SampleSizes.S_256x256,
        5 => ZOSAPI.Analysis.SampleSizes.S_512x512,
        6 => ZOSAPI.Analysis.SampleSizes.S_1024x1024,
        _ => ZOSAPI.Analysis.SampleSizes.S_512x512
    };

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static void WriteGridBin(string path, int nx, int ny, double dx, double dy, double[][] values)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(nx);                // int32
        bw.Write(ny);                // int32
        bw.Write(dx);                // float64
        bw.Write(dy);                // float64
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
                bw.Write(values[y][x]);
    }
}
