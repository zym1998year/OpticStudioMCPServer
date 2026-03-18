using ZOSAPI.Editors;

namespace ZemaxMCP.Core.Models;

public class MaterialInfo
{
    public int SurfaceIndex { get; set; }
    public int SurfaceNumber { get; set; }
    public string Material { get; set; } = "";
    public string Catalog { get; set; } = "";
    public SolveType SolveType { get; set; }
    public string SubstituteCatalog { get; set; } = "";
    public string[] SubstituteGlasses { get; set; } = Array.Empty<string>();
}
