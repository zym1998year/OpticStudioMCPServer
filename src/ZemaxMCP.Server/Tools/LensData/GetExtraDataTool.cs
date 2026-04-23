using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class GetExtraDataTool
{
    private readonly IZemaxSession _session;

    public GetExtraDataTool(IZemaxSession session) => _session = session;

    public record ExtraDataEntry(int Cell, double Value, bool IsVariable);

    public record GetExtraDataResult(
        bool Success,
        string? Error = null,
        int SurfaceNumber = 0,
        string? SurfaceType = null,
        string? AccessPath = null,
        ExtraDataEntry[]? Entries = null);

    [McpServerTool(Name = "zemax_get_extra_data")]
    [Description(
        "Read cells from the Extra Data Editor (XDAT) for a surface. "
        + "XDAT stores per-surface-type data like Zernike coefficients, Max Term, "
        + "Normalization Radius, extended-polynomial terms, etc. "
        + "When endCell=0, auto-detects by reading until 3 consecutive cells fail. "
        + "Returns the access path used (SurfaceColumnExtraData / GetCellAtAbsolute / "
        + "ExtraDataGetCell) for diagnostics.")]
    public async Task<GetExtraDataResult> ExecuteAsync(
        [Description("Surface number")] int surfaceNumber,
        [Description("Start cell number (1-indexed)")] int startCell = 1,
        [Description("End cell number (0 = auto-detect; stops after 3 consecutive read failures)")] int endCell = 0)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber,
                ["startCell"] = startCell,
                ["endCell"] = endCell
            };

            return await _session.ExecuteAsync("GetExtraData", parameters, system =>
            {
                var lde = system.LDE;
                if (surfaceNumber < 0 || surfaceNumber >= lde.NumberOfSurfaces)
                    return new GetExtraDataResult(false,
                        Error: $"Invalid surface number: {surfaceNumber}. Valid range: 0-{lde.NumberOfSurfaces - 1}.");

                var surface = lde.GetSurfaceAt(surfaceNumber);
                string surfType = surface.Type.ToString();

                if (startCell < 1) startCell = 1;
                int hardLimit = endCell > 0 ? endCell : 250;

                var entries = new List<ExtraDataEntry>();
                int consecutiveFailures = 0;
                ExtraDataHelper.AccessPath pathUsed = ExtraDataHelper.AccessPath.Unknown;

                for (int n = startCell; n <= hardLimit; n++)
                {
                    // Prefer the typed-interface path for Zernike surfaces; fall back
                    // to the column-probe helper for non-Zernike surface types.
                    var cell = ExtraDataHelper.TryGetZernikeCell(surface, n, out var path);
                    if (cell == null)
                        cell = ExtraDataHelper.TryGetCell(surface, n, out path);
                    if (cell == null)
                    {
                        consecutiveFailures++;
                        if (endCell == 0 && consecutiveFailures >= 3) break;
                        continue;
                    }

                    if (pathUsed == ExtraDataHelper.AccessPath.Unknown) pathUsed = path;

                    try
                    {
                        double v = ExtraDataHelper.ReadValue(cell);
                        bool isVar = ExtraDataHelper.IsVariable(cell);
                        entries.Add(new ExtraDataEntry(n, v, isVar));
                        consecutiveFailures = 0;
                    }
                    catch
                    {
                        consecutiveFailures++;
                        if (endCell == 0 && consecutiveFailures >= 3) break;
                    }
                }

                if (entries.Count == 0)
                    return new GetExtraDataResult(false,
                        Error: $"No XDAT cells accessible for surface {surfaceNumber} (type {surfType}). " +
                               "Check that the surface type supports Extra Data.",
                        SurfaceNumber: surfaceNumber,
                        SurfaceType: surfType);

                return new GetExtraDataResult(
                    Success: true,
                    SurfaceNumber: surfaceNumber,
                    SurfaceType: surfType,
                    AccessPath: pathUsed.ToString(),
                    Entries: entries.ToArray());
            });
        }
        catch (Exception ex)
        {
            return new GetExtraDataResult(false, Error: ex.Message);
        }
    }
}
