using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.GlassCatalog;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.GlassCatalog;

[McpServerToolType]
public class GetGlassCatalogsTool
{
    private readonly IZemaxSession _session;

    public GetGlassCatalogsTool(IZemaxSession session) => _session = session;

    public record CatalogInfo(string Name, string FilePath);
    public record GetCatalogsResult(bool Success, string? Error, List<CatalogInfo>? Catalogs);

    [McpServerTool(Name = "zemax_get_glass_catalogs")]
    [Description("List available glass catalog names from the Zemax Glasscat directory")]
    public Task<GetCatalogsResult> ExecuteAsync()
    {
        try
        {
            var glassCatDir = GetGlassCatDir();
            if (glassCatDir == null)
                return Task.FromResult(new GetCatalogsResult(false, "Not connected to OpticStudio or Zemax data directory not available", null));

            var catalogs = AgfFileParser.DiscoverCatalogs(glassCatDir);
            var catalogList = catalogs
                .OrderBy(c => c.Key)
                .Select(c => new CatalogInfo(c.Key, c.Value))
                .ToList();

            return Task.FromResult(new GetCatalogsResult(true, null, catalogList));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new GetCatalogsResult(false, ex.Message, null));
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
