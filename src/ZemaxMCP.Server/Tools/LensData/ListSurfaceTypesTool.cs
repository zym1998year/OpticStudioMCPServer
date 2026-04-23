using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class ListSurfaceTypesTool
{
    private readonly IZemaxSession _session;

    public ListSurfaceTypesTool(IZemaxSession session) => _session = session;

    public record ListSurfaceTypesResult(
        bool Success,
        string? Error = null,
        string[]? AvailableTypes = null,
        string? Source = null);

    [McpServerTool(Name = "zemax_list_surface_types")]
    [Description(
        "List all surface type names supported by the installed ZOSAPI version. "
        + "Returns the names of the ZOSAPI.Editors.LDE.SurfaceType enum, which is stable "
        + "across ZOSAPI versions (unlike the dynamic GetAvailableSurfaceTypes() call "
        + "that fails in some versions). Some listed types may require specific licenses "
        + "or DLLs (e.g., UserDefined) to actually apply.")]
    public Task<ListSurfaceTypesResult> ExecuteAsync()
    {
        try
        {
            var names = Enum.GetNames(typeof(ZOSAPI.Editors.LDE.SurfaceType));
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new ListSurfaceTypesResult(
                Success: true,
                AvailableTypes: names,
                Source: "ZOSAPI.Editors.LDE.SurfaceType enum"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ListSurfaceTypesResult(false, Error: ex.Message));
        }
    }
}
