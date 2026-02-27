using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.GlassCatalog;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.GlassCatalog;

[McpServerToolType]
public class GetGlassesTool
{
    private readonly IZemaxSession _session;

    public GetGlassesTool(IZemaxSession session) => _session = session;

    public record GlassInfo(
        string Name,
        string Catalog,
        double Nd,
        double Vd,
        double DPgF,
        string Status,
        double TCE,
        double RelativeCost,
        double MinWavelength,
        double MaxWavelength,
        double Density,
        int MeltFrequency,
        string? Comment
    );

    public record GetGlassesResult(bool Success, string? Error, int TotalCount, List<GlassInfo>? Glasses);

    [McpServerTool(Name = "zemax_get_glasses")]
    [Description("List glasses in a catalog with properties (Nd, Vd, dPgF, status, cost, TCE, etc.)")]
    public Task<GetGlassesResult> ExecuteAsync(
        [Description("Catalog name(s), comma-separated (e.g., 'SCHOTT' or 'SCHOTT,OHARA')")] string catalogs)
    {
        try
        {
            var glassCatDir = GetGlassCatDir();
            if (glassCatDir == null)
                return Task.FromResult(new GetGlassesResult(false, "Not connected to OpticStudio or Zemax data directory not available", 0, null));

            var availableCatalogs = AgfFileParser.DiscoverCatalogs(glassCatDir);
            var requestedNames = catalogs.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

            var allGlasses = new List<GlassInfo>();
            var missingCatalogs = new List<string>();

            foreach (var name in requestedNames)
            {
                if (availableCatalogs.TryGetValue(name, out var agfPath))
                {
                    var glasses = AgfFileParser.ParseCatalog(agfPath, name);
                    allGlasses.AddRange(glasses.Select(g => new GlassInfo(
                        g.Name, g.CatalogName, g.Nd, g.Vd,
                        Math.Round(g.DPgF, 6), g.StatusText,
                        g.TCE, g.RelativeCost,
                        g.MinWavelength, g.MaxWavelength,
                        g.Density, g.MeltFrequency, g.Comment
                    )));
                }
                else
                {
                    missingCatalogs.Add(name);
                }
            }

            string? error = missingCatalogs.Count > 0
                ? $"Catalogs not found: {string.Join(", ", missingCatalogs)}"
                : null;

            return Task.FromResult(new GetGlassesResult(
                missingCatalogs.Count == 0,
                error,
                allGlasses.Count,
                allGlasses
            ));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new GetGlassesResult(false, ex.Message, 0, null));
        }
    }

    private string? GetGlassCatDir()
    {
        if (!_session.IsConnected || string.IsNullOrEmpty(_session.ZemaxDataDir))
            return null;

        var dir = Path.Combine(_session.ZemaxDataDir, "Glasscat");
        return Directory.Exists(dir) ? dir : null;
    }
}
