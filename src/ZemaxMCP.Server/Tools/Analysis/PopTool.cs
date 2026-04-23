using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Analysis;
using ZOSAPI.Analysis.PhysicalOptics;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class PopTool
{
    private readonly IZemaxSession _session;

    public PopTool(IZemaxSession session) => _session = session;

    public record PopResult(
        bool Success,
        string? Error = null,
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
        // NEW fields (add at end for JSON compat)
        int StartSurfaceResolved = 0,
        int EndSurfaceResolved = 0,
        int WavelengthResolved = 0,
        int FieldResolved = 0,
        double SurfaceToBeamApplied = 0,
        bool ResampleAfterRefractionApplied = false,
        double[][]? Grid = null,
        string? GridFilePath = null,
        string? BmpFilePath = null,
        string? OutputBeamFilePath = null);

    // Inline threshold: 256x256 = 65536 cells ~ 2 MB JSON. Larger grids must write to disk.
    private const int InlineGridCellLimit = 65536;

    [McpServerTool(Name = "zemax_pop")]
    [Description(
        "Run Physical Optics Propagation and return the intensity (or phase) grid. "
        + "Used for wavefront sensor donut simulation: add Zernike phase via zemax_set_extra_data, then run POP. "
        + "startSurface/endSurface control POP propagation range (0=keep default, endSurface=-1 uses image surface). "
        + "surfaceToBeam is the INPUT-SIDE axial offset of the beam relative to startSurface (NOT output-side defocus). "
        + "Empirically verified on ZOSAPI 2023 R1.00: varying surfaceToBeam on GaussianWaist over [-1000,+1000]mm "
        + "produces bit-identical image-plane grids; TopHat shows sub-0.1%% response. "
        + "To defocus the donut at image, change the image-surface thickness or insert a dummy surface downstream, NOT via surfaceToBeam. "
        + "autoSampling/autoWidth override autoCalculate per-axis (null inherits from autoCalculate). "
        + "resampleAfterRefraction forces ResampleAfterRefraction=true on LDE surfaces in [startSurface, endSurface] before running. "
        + "ignorePolarization maps to settings.UsePolarization=false (skip polarization calculations). "
        + "outputBeamFilePath dumps a .ZBF file for OpticStudio GUI review (sets SaveOutputBeam=true, OutputBeamFile=path). "
        + "beamType: GaussianWaist, GaussianAngle, GaussianSizeAngle, TopHat, File, DLL, Multimode, AstigmaticGaussian. "
        + "dataType: Irradiance, EXIrradiance, EYIrradiance, Phase, EXPhase, EYPhase, TransferMagnitude, TransferPhase. "
        + "beamParams is a comma-separated list of beam-type-specific parameter values (e.g. \"1.0,1.0,0,0\" for a Gaussian waist beam); "
        + "the tool passes them through to SetParameterValue in order. "
        + "If autoCalculate=true, Zemax recomputes sampling/width after the user-provided values (so user width/sampling are overridden "
        + "unless autoSampling=false or autoWidth=false restores them selectively). "
        + "CAVEAT (physics, not a bug): POP's Apply stage recomputes the output pixel pitch based on beam diffraction through the system, so "
        + "gridWidthX/Y (= Nx * PixelPitchX) may not equal the requested xWidth/yWidth even when autoWidth=false. "
        + "surfaceToBeam likewise feeds Zemax as a hint; if the engine's internal refocus logic decides the evaluation plane, the grid may "
        + "be insensitive to small surfaceToBeam changes on focused systems. Treat xWidth/surfaceToBeam as requests Zemax may honor or override. "
        + "Grid <= 256x256 returns inline; larger grids require outputGridPath (raw float64 little-endian "
        + "with 24-byte header: int32 Nx | int32 Ny | float64 Dx | float64 Dy, then Ny*Nx*8 bytes row-major). "
        + "All linear units are lens units (usually mm).")]
    public async Task<PopResult> ExecuteAsync(
        [Description("Beam type: GaussianWaist, GaussianAngle, GaussianSizeAngle, TopHat, File, DLL, Multimode, AstigmaticGaussian")] string beamType = "GaussianWaist",
        [Description("Comma-separated beam parameters (indices 1..N per beam type; e.g. \"1.0,1.0,0,0\" for Gaussian waist). Leave empty for defaults.")] string? beamParams = null,
        [Description("POP start surface (1-indexed); 0 = keep current default")] int startSurface = 0,
        [Description("POP end surface (1-indexed); -1 = use image surface, 0 = keep current default")] int endSurface = -1,
        [Description("X sampling: 1=32,2=64,3=128,4=256,5=512,6=1024")] int xSampling = 5,
        [Description("Y sampling: same scale as xSampling")] int ySampling = 5,
        [Description("X width in lens units (0 = leave default; overridden if autoCalculate=true)")] double xWidth = 0,
        [Description("Y width in lens units (0 = leave default; overridden if autoCalculate=true)")] double yWidth = 0,
        [Description("Call AutoCalculateBeamSampling() after user values (Zemax overrides sampling/width)")] bool autoCalculate = true,
        [Description("Data type: Irradiance, EXIrradiance, EYIrradiance, Phase, EXPhase, EYPhase, TransferMagnitude, TransferPhase")] string dataType = "Irradiance",
        [Description("Use peak-irradiance normalization (sets UsePeakIrradiance)")] bool peakNormalize = false,
        [Description("Input-side axial offset of the beam from startSurface (NOT image-plane defocus). For image-plane defocus, modify image-surface thickness or insert a downstream dummy surface instead.")] double surfaceToBeam = 0,
        [Description("Optional path to write raw grid (required if Nx*Ny > 65536)")] string? outputGridPath = null,
        [Description("Optional path to export BMP image")] string? exportBmpPath = null,
        [Description("Wavelength number (1-indexed); 0 = use all/primary")] int wavelength = 0,
        [Description("Field number (1-indexed); 0 = use all/primary")] int field = 0,
        [Description("Split control for sampling auto-calc; null = inherit from autoCalculate")] bool? autoSampling = null,
        [Description("Split control for width auto-calc; null = inherit from autoCalculate")] bool? autoWidth = null,
        [Description("Force ResampleAfterRefraction=true on surfaces in [startSurface, endSurface] before running. Mutates LDE but does not save.")] bool resampleAfterRefraction = false,
        [Description("If true, sets settings.UsePolarization=false (ZOSAPI polarization skip)")] bool ignorePolarization = false,
        [Description("Optional .ZBF output path; sets SaveOutputBeam=true and OutputBeamFile")] string? outputBeamFilePath = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["beamType"] = beamType,
                ["beamParams"] = beamParams,
                ["startSurface"] = startSurface,
                ["endSurface"] = endSurface,
                ["xSampling"] = xSampling,
                ["ySampling"] = ySampling,
                ["xWidth"] = xWidth,
                ["yWidth"] = yWidth,
                ["autoCalculate"] = autoCalculate,
                ["dataType"] = dataType,
                ["peakNormalize"] = peakNormalize,
                ["surfaceToBeam"] = surfaceToBeam,
                ["outputGridPath"] = outputGridPath,
                ["exportBmpPath"] = exportBmpPath,
                ["wavelength"] = wavelength,
                ["field"] = field,
                ["autoSampling"] = autoSampling,
                ["autoWidth"] = autoWidth,
                ["resampleAfterRefraction"] = resampleAfterRefraction,
                ["ignorePolarization"] = ignorePolarization,
                ["outputBeamFilePath"] = outputBeamFilePath
            };

            return await _session.ExecuteAsync("Pop", parameters, system =>
            {
                var analysis = system.Analyses.New_Analysis(AnalysisIDM.PhysicalOpticsPropagation);
                try
                {
                    var settings = analysis.GetSettings() as IAS_PhysicalOpticsPropagation;
                    if (settings == null)
                        return new PopResult(false,
                            Error: "Failed to cast POP settings to IAS_PhysicalOpticsPropagation.");

                    // BeamType
                    if (!Enum.TryParse<POPBeamTypes>(beamType, ignoreCase: true, out var bt))
                        return new PopResult(false,
                            Error: $"Invalid beamType '{beamType}'. Valid: GaussianWaist, GaussianAngle, GaussianSizeAngle, TopHat, File, DLL, Multimode, AstigmaticGaussian.");
                    settings.BeamType = bt;

                    // DataType
                    if (!Enum.TryParse<POPDataTypes>(dataType, ignoreCase: true, out var dt))
                        return new PopResult(false,
                            Error: $"Invalid dataType '{dataType}'. Valid: Irradiance, EXIrradiance, EYIrradiance, Phase, EXPhase, EYPhase, TransferMagnitude, TransferPhase.");
                    settings.DataType = dt;

                    // Surface range (BEFORE sampling/width so auto-calc respects range)
                    // startSurface: 0 = keep default, positive = explicit
                    if (startSurface > 0)
                    {
                        try { settings.StartSurface.SetSurfaceNumber(startSurface); } catch { }
                    }

                    // endSurface: -1 = image, 0 = keep default, positive = explicit
                    if (endSurface == -1)
                    {
                        try { settings.EndSurface.UseImageSurface(); } catch { }
                    }
                    else if (endSurface > 0)
                    {
                        try { settings.EndSurface.SetSurfaceNumber(endSurface); } catch { }
                    }

                    // Wavelength / Field
                    if (wavelength > 0)
                    {
                        try { settings.Wavelength.SetWavelengthNumber(wavelength); } catch { }
                    }
                    if (field > 0)
                    {
                        try { settings.Field.SetFieldNumber(field); } catch { }
                    }

                    // Resolve per-axis auto-calc flags
                    bool useAutoSampling = autoSampling ?? autoCalculate;
                    bool useAutoWidth = autoWidth ?? autoCalculate;

                    // Sampling / width (user values first; auto-calc may override below)
                    settings.XSampling = MapSampling(xSampling);
                    settings.YSampling = MapSampling(ySampling);
                    if (xWidth > 0) settings.XWidth = xWidth;
                    if (yWidth > 0) settings.YWidth = yWidth;

                    // Beam parameters: comma-separated, pass through via SetParameterValue.
                    // Zemax POP UI uses 1-indexed parameter numbering; try 1-indexed first, fall back to 0-indexed.
                    if (!string.IsNullOrWhiteSpace(beamParams))
                    {
                        var tokens = beamParams!.Split(',');
                        for (int i = 0; i < tokens.Length; i++)
                        {
                            if (!double.TryParse(tokens[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                                return new PopResult(false,
                                    Error: $"Cannot parse beamParams token '{tokens[i]}' at position {i}.");
                            try { settings.SetParameterValue(i + 1, v); }
                            catch
                            {
                                try { settings.SetParameterValue(i, v); }
                                catch { /* skip unsupported parameter slot */ }
                            }
                        }
                    }

                    // Peak-irradiance normalization toggle
                    settings.UsePeakIrradiance = peakNormalize;

                    // Polarization control
                    try { settings.UsePolarization = !ignorePolarization; } catch { }

                    // Save output beam (.ZBF)
                    if (!string.IsNullOrWhiteSpace(outputBeamFilePath))
                    {
                        try { settings.SaveOutputBeam = true; } catch { }
                        try { settings.OutputBeamFile = outputBeamFilePath; } catch { }
                    }

                    // ---- Auto-calc with selective restore ----
                    if (useAutoSampling || useAutoWidth)
                    {
                        // Snapshot user values BEFORE auto overwrites them
                        var savedXSampling = settings.XSampling;
                        var savedYSampling = settings.YSampling;
                        double savedXWidth = settings.XWidth;
                        double savedYWidth = settings.YWidth;

                        try { settings.AutoCalculateBeamSampling(); } catch { /* ignore */ }

                        // Restore the axes that should NOT be auto
                        if (!useAutoSampling)
                        {
                            try { settings.XSampling = savedXSampling; } catch { }
                            try { settings.YSampling = savedYSampling; } catch { }
                        }
                        if (!useAutoWidth)
                        {
                            try { settings.XWidth = savedXWidth; } catch { }
                            try { settings.YWidth = savedYWidth; } catch { }
                        }
                    }

                    // ---- CRITICAL: SurfaceToBeam applied AFTER auto-calc so it isn't overridden ----
                    try { settings.SurfaceToBeam = surfaceToBeam; } catch { }

                    // ---- ResampleAfterRefraction iteration on LDE surfaces in range ----
                    if (resampleAfterRefraction)
                    {
                        var lde = system.LDE;
                        int s0 = startSurface > 0 ? startSurface : 1;
                        int s1;
                        try { s1 = settings.EndSurface.GetSurfaceNumber(); }
                        catch { s1 = lde.NumberOfSurfaces - 1; }
                        // Clamp defensively
                        if (s0 < 0) s0 = 0;
                        if (s1 <= 0 || s1 >= lde.NumberOfSurfaces) s1 = lde.NumberOfSurfaces - 1;
                        for (int k = s0; k <= s1; k++)
                        {
                            try
                            {
                                var row = lde.GetSurfaceAt(k);
                                row.PhysicalOpticsData.ResampleAfterRefraction = true;
                            }
                            catch { /* surface may not support this; skip */ }
                        }
                    }

                    analysis.ApplyAndWaitForCompletion();

                    var results = analysis.GetResults();
                    dynamic resultsDyn = results;

                    // Extract grid via dynamic (GetDataGrid / GetDataGridDouble / DataGrids[0])
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
                            Error: "POP analysis produced no data grid. Check beam settings and surface configuration.");

                    int nx = (int)grid.Nx;
                    int ny = (int)grid.Ny;
                    double dx = (double)grid.Dx;
                    double dy = (double)grid.Dy;
                    double widthX = dx * nx;
                    double widthY = dy * ny;

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

                    // Compute PeakIrradiance (max |value|) and TotalPower
                    // (sum * pixel area) from the copied grid. The IAR_DataGrid
                    // PeakValue/Total properties do not exist on this ZOSAPI
                    // version, so we integrate directly from the sampled grid.
                    double peak = 0, total = 0;
                    for (int y = 0; y < ny; y++)
                    {
                        for (int x = 0; x < nx; x++)
                        {
                            double v = values2d[y][x];
                            double a = Math.Abs(v);
                            if (a > peak) peak = a;
                            total += v;
                        }
                    }
                    total *= (dx * dy);  // integrate over pixel area

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

                    // Metadata readback from settings
                    int startResolved = 0, endResolved = 0, wlResolved = 0, fldResolved = 0;
                    double surfToBeamApplied = 0;
                    try { startResolved = settings.StartSurface.GetSurfaceNumber(); } catch { }
                    try { endResolved = settings.EndSurface.GetSurfaceNumber(); } catch { }
                    // EndSurface.GetSurfaceNumber() returns 0 as a sentinel meaning
                    // "UseImageSurface()" was called. Translate to the actual image
                    // surface index so callers see a meaningful number.
                    if (endResolved == 0)
                    {
                        try { endResolved = system.LDE.NumberOfSurfaces - 1; } catch { }
                    }
                    try { wlResolved = settings.Wavelength.GetWavelengthNumber(); } catch { }
                    try { fldResolved = settings.Field.GetFieldNumber(); } catch { }
                    try { surfToBeamApplied = settings.SurfaceToBeam; } catch { }

                    return new PopResult(
                        Success: true,
                        BeamType: bt.ToString(),
                        DataType: dt.ToString(),
                        PeakIrradiance: peak,
                        TotalPower: total,
                        GridWidthX: widthX,
                        GridWidthY: widthY,
                        PixelPitchX: dx,
                        PixelPitchY: dy,
                        Nx: nx,
                        Ny: ny,
                        StartSurfaceResolved: startResolved,
                        EndSurfaceResolved: endResolved,
                        WavelengthResolved: wlResolved,
                        FieldResolved: fldResolved,
                        SurfaceToBeamApplied: surfToBeamApplied,
                        ResampleAfterRefractionApplied: resampleAfterRefraction,
                        Grid: inlineGrid,
                        GridFilePath: gridFilePath,
                        BmpFilePath: bmpPath,
                        OutputBeamFilePath: outputBeamFilePath);
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
