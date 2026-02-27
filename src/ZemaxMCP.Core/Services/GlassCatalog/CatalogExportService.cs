using System.Text;
using ZemaxMCP.Core.Models;

namespace ZemaxMCP.Core.Services.GlassCatalog;

public static class CatalogExportService
{
    public static List<string> FindDuplicateNames(IEnumerable<GlassEntry> glasses)
    {
        return glasses
            .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Where(grp => grp.Count() > 1)
            .Select(grp => $"{grp.Key} ({string.Join(", ", grp.Select(g => g.CatalogName))})")
            .ToList();
    }

    public static bool CatalogExists(string glassCatDir, string catalogName)
    {
        if (string.IsNullOrEmpty(glassCatDir) || !Directory.Exists(glassCatDir))
            return false;

        string targetPath = Path.Combine(glassCatDir, catalogName + ".agf");
        return File.Exists(targetPath);
    }

    public static void Export(IEnumerable<GlassEntry> glasses, string outputPath, string catalogName)
    {
        using var writer = new StreamWriter(outputPath, false, Encoding.ASCII);
        writer.WriteLine($"CC {catalogName} - Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        foreach (var glass in glasses)
        {
            foreach (var line in glass.RawLines)
            {
                writer.WriteLine(line);
            }
        }
    }
}
