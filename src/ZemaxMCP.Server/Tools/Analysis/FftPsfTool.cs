using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Analysis.Settings.Psf;

namespace ZemaxMCP.Server.Tools.Analysis;

// 说明:本工具的接口/设置应用段从历史会话记录恢复(逐字复刻);结果提取段(读 PSF
// 数据网格 + Strehl + 落盘)在原会话记录被截断,按 main 现有 PopTool 的写法重建。
// 运行时数值语义待 OpticStudio ZOSAPI 连接可用后核验。
[McpServerToolType]
public class FftPsfTool
{
    private const int InlineGridCellLimit = 65536;

    private readonly IZemaxSession _session;

    public FftPsfTool(IZemaxSession session) => _session = session;

    public record FftPsfResult(
        bool Success,
        string? Error = null,
        int? Nx = null, int? Ny = null,
        double? Dx = null, double? Dy = null,
        double[]? Grid = null,
        double? StrehlRatio = null,
        string? Field = null,
        string? Wavelength = null,
        string? TextPath = null,
        string? GridPath = null);

    [McpServerTool(Name = "zemax_fft_psf")]
    [Description(
        "Run FFT Point Spread Function analysis with full settings control. Returns the PSF grid "
        + "(intensity or phase). SampleSize controls input pupil sampling; "
        + "OutputSize controls how much of the PSF is returned. Use Type to select Linear/Logarithmic/"
        + "Phase output. ImageDelta sets the output pixel size in micrometers (0 = auto). "
        + "Note: ZOSAPI does not expose a Strehl ratio for FFT PSF (this version), so StrehlRatio is "
        + "always null here — use zemax_huygens_psf when you need the Strehl ratio.")]
    public async Task<FftPsfResult> ExecuteAsync(
        [Description("Wavelength number (1-indexed); 0 = primary")] int wavelength = 0,
        [Description("Field number (1-indexed)")] int field = 1,
        [Description("Surface number; 0 = image (default)")] int surface = 0,
        [Description("Pupil sampling: PsfSampling enum value name (e.g., 'PsfS_64x64', 'PsfS_128x128', 'PsfS_256x256') or integer enum value")]
        string sampleSize = "PsfS_128x128",
        [Description("Output sampling: same enum (default 'PsfS_64x64')")]
        string outputSize = "PsfS_64x64",
        [Description("Output type (FftPsfType): 'Linear', 'Log', 'Phase', 'Real', 'Imaginary' (default Linear)")]
        string type = "Linear",
        [Description("Image plane pixel size in micrometers (0 = auto)")] double imageDelta = 0.0,
        [Description("Normalize the PSF (true)")] bool normalize = true,
        [Description("Use polarization (default false)")] bool usePolarization = false,
        [Description("Optional output text path (.txt)")] string? textPath = null,
        [Description("Optional path for raw float64 grid (required if grid > 65536 cells)")] string? gridPath = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["wavelength"] = wavelength, ["field"] = field, ["surface"] = surface,
                ["sampleSize"] = sampleSize, ["outputSize"] = outputSize,
                ["type"] = type, ["imageDelta"] = imageDelta,
                ["normalize"] = normalize, ["usePolarization"] = usePolarization,
                ["textPath"] = textPath, ["gridPath"] = gridPath
            };

            return await _session.ExecuteAsync("FftPsf", parameters, system =>
            {
                var analysis = system.Analyses.New_Analysis_SettingsFirst(
                    ZOSAPI.Analysis.AnalysisIDM.FftPsf);
                try
                {
                    var settingsGeneral = analysis.GetSettings();
                    var s = settingsGeneral as IAS_FftPsf;
                    if (s != null)
                    {
                        if (wavelength > 0) s.Wavelength.SetWavelengthNumber(wavelength);
                        if (field > 0) s.Field.SetFieldNumber(field);
                        if (surface == 0) s.Surface.UseImageSurface();
                        else s.Surface.SetSurfaceNumber(surface);
                        if (Enum.TryParse<PsfSampling>(sampleSize, ignoreCase: true, out var ssEnum))
                            s.SampleSize = ssEnum;
                        if (Enum.TryParse<PsfSampling>(outputSize, ignoreCase: true, out var osEnum))
                            s.OutputSize = osEnum;
                        if (Enum.TryParse<FftPsfType>(type, ignoreCase: true, out var typeEnum))
                            s.Type = typeEnum;
                        // 以下属性名在部分 ZOSAPI 版本间有漂移,用 dynamic + try 逐项软应用,
                        // 缺失则跳过(不影响其余设置与结果读取)。
                        dynamic sd = s;
                        try { sd.ImageDelta = imageDelta; } catch { }
                        try { sd.Normalize = normalize; } catch { }
                        try { sd.UsePolarization = usePolarization; } catch { }
                    }

                    analysis.ApplyAndWaitForCompletion();

                    var results = analysis.GetResults();

                    // 文本输出:供 textPath 落盘 + 解析 Field / Wavelength 表头。
                    // 注:本版 ZOSAPI 的 FFT PSF 文本/HeaderData/MetaData 均不含 Strehl,故此处
                    // strehl 恒为 null(经探针实测);需要 Strehl 用 zemax_huygens_psf。
                    string tmpTxt = textPath ?? Path.Combine(
                        Path.GetTempPath(), $"zemax_fft_psf_{Guid.NewGuid():N}.txt");
                    double? strehl = null; string? fieldLabel = null, waveLabel = null;
                    try
                    {
                        results.GetTextFile(tmpTxt);
                        (strehl, fieldLabel, waveLabel) = ParsePsfHeader(tmpTxt);
                    }
                    catch { }

                    // 数据网格:按 PopTool 的动态回退取 grid,再按 Z(x,y)/Values 读值
                    dynamic resultsDyn = results;
                    dynamic? grid = null;
                    try { grid = resultsDyn.GetDataGrid(0); } catch { }
                    if (grid == null) { try { grid = resultsDyn.GetDataGridDouble(0); } catch { } }
                    if (grid == null)
                        return new FftPsfResult(false,
                            Error: "FFT PSF produced no data grid. Check sampling/type settings.");

                    int nx = (int)grid.Nx;
                    int ny = (int)grid.Ny;
                    double dx = (double)grid.Dx;
                    double dy = (double)grid.Dy;

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
                        return new FftPsfResult(false,
                            Error: "Unable to read PSF data grid: Z(x,y)/Values[y,x]/Values(y,x) all failed.");

                    var flat = new double[nx * ny];   // row-major (y*nx + x)
                    for (int y = 0; y < ny; y++)
                        for (int x = 0; x < nx; x++)
                            flat[y * nx + x] = reader(y, x);

                    int cells = nx * ny;
                    double[]? inlineGrid = null; string? gridOut = null;
                    if (cells <= InlineGridCellLimit && string.IsNullOrEmpty(gridPath))
                    {
                        inlineGrid = flat;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(gridPath))
                            return new FftPsfResult(false,
                                Error: $"Grid {nx}x{ny}={cells} exceeds inline limit {InlineGridCellLimit}. Provide gridPath.");
                        EnsureDirectory(gridPath);
                        WriteGridBin(gridPath!, nx, ny, dx, dy, flat);
                        gridOut = gridPath;
                    }

                    if (textPath == null) { try { File.Delete(tmpTxt); } catch { } }

                    return new FftPsfResult(true, Nx: nx, Ny: ny, Dx: dx, Dy: dy,
                        Grid: inlineGrid, StrehlRatio: strehl,
                        Field: fieldLabel, Wavelength: waveLabel,
                        TextPath: textPath, GridPath: gridOut);
                }
                finally
                {
                    analysis.Close();
                }
            });
        }
        catch (Exception ex)
        {
            return new FftPsfResult(false, Error: ex.Message);
        }
    }

    // 解析 PSF 文本表头:Strehl 比、Field、Wavelength 行(缺失则返回 null)。
    private static (double? strehl, string? field, string? wave) ParsePsfHeader(string path)
    {
        double? strehl = null; string? field = null, wave = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            var lower = line.ToLowerInvariant();
            if (strehl == null && lower.Contains("strehl"))
            {
                foreach (var tok in line.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries))
                    if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    { strehl = v; break; }
            }
            else if (field == null && lower.StartsWith("field") && line.Contains(':'))
                field = line[(line.IndexOf(':') + 1)..].Trim();
            else if (wave == null && (lower.StartsWith("wave") || lower.StartsWith("wavelength")) && line.Contains(':'))
                wave = line[(line.IndexOf(':') + 1)..].Trim();
        }
        return (strehl, field, wave);
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    // 二进制格式与 PopTool.WriteGridBin 一致:int32 nx, int32 ny, float64 dx, float64 dy, 然后 nx*ny 个 float64(行主序)。
    private static void WriteGridBin(string path, int nx, int ny, double dx, double dy, double[] flat)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(nx);
        bw.Write(ny);
        bw.Write(dx);
        bw.Write(dy);
        for (int i = 0; i < flat.Length; i++)
            bw.Write(flat[i]);
    }
}
