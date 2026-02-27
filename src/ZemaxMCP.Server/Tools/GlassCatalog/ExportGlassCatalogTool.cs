using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.GlassCatalog;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.GlassCatalog;

[McpServerToolType]
public class ExportGlassCatalogTool
{
    private readonly IZemaxSession _session;

    public ExportGlassCatalogTool(IZemaxSession session) => _session = session;

    public record ExportResult(
        bool Success,
        string? Error,
        string? OutputPath,
        int GlassCount,
        List<string>? Duplicates
    );

    [McpServerTool(Name = "zemax_export_glass_catalog")]
    [Description("Export filtered glasses to a new .agf catalog file in the Zemax Glasscat directory")]
    public Task<ExportResult> ExecuteAsync(
        [Description("Name for the new catalog (without .agf extension)")] string catalogName,
        [Description("Source catalog name(s), comma-separated")] string sourceCatalogs,
        [Description("Overwrite if catalog already exists")] bool overwrite = false,
        [Description("Only include preferred glasses")] bool? preferredOnly = null,
        [Description("Max weighted distance from target")] double? distanceRadius = null,
        [Description("Distance filter: weight for Nd")] double wn = 1.0,
        [Description("Distance filter: weight for Vd")] double wa = 1E-04,
        [Description("Distance filter: weight for dPgF")] double wp = 1E+02,
        [Description("Distance filter: target Nd")] double ndTarget = 1.5168,
        [Description("Distance filter: target Vd")] double vdTarget = 64.17,
        [Description("Distance filter: target dPgF")] double dpgfTarget = 0.0,
        [Description("Max relative cost")] double? maxCost = null,
        [Description("Minimum Nd")] double? ndMin = null,
        [Description("Maximum Nd")] double? ndMax = null,
        [Description("Minimum Vd")] double? vdMin = null,
        [Description("Maximum Vd")] double? vdMax = null,
        [Description("Minimum dPgF")] double? dpgfMin = null,
        [Description("Maximum dPgF")] double? dpgfMax = null,
        [Description("Minimum TCE")] double? tceMin = null,
        [Description("Maximum TCE")] double? tceMax = null,
        [Description("Glass must transmit down to this wavelength (µm)")] double? minWavelengthCoverage = null,
        [Description("Glass must transmit up to this wavelength (µm)")] double? maxWavelengthCoverage = null,
        [Description("Maximum melt frequency")] int? maxMeltFrequency = null)
    {
        try
        {
            var glassCatDir = GetGlassCatDir();
            if (glassCatDir == null)
                return Task.FromResult(new ExportResult(false, "Not connected to OpticStudio or Zemax data directory not available", null, 0, null));

            // Check if catalog already exists
            if (!overwrite && CatalogExportService.CatalogExists(glassCatDir, catalogName))
                return Task.FromResult(new ExportResult(false, $"Catalog '{catalogName}' already exists. Set overwrite=true to replace.", null, 0, null));

            // Load source catalogs
            var availableCatalogs = AgfFileParser.DiscoverCatalogs(glassCatDir);
            var requestedNames = sourceCatalogs.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

            var allGlasses = new List<ZemaxMCP.Core.Models.GlassEntry>();
            foreach (var name in requestedNames)
            {
                if (availableCatalogs.TryGetValue(name, out var agfPath))
                    allGlasses.AddRange(AgfFileParser.ParseCatalog(agfPath, name));
            }

            // Apply filters
            var criteria = new GlassFilterCriteria
            {
                PreferredOnly = preferredOnly,
                DistanceRadius = distanceRadius,
                Wn = wn,
                Wa = wa,
                Wp = wp,
                NdTarget = ndTarget,
                VdTarget = vdTarget,
                DPgFTarget = dpgfTarget,
                MaxCost = maxCost,
                NdMin = ndMin,
                NdMax = ndMax,
                VdMin = vdMin,
                VdMax = vdMax,
                DPgFMin = dpgfMin,
                DPgFMax = dpgfMax,
                TCEMin = tceMin,
                TCEMax = tceMax,
                MinWavelengthCoverage = minWavelengthCoverage,
                MaxWavelengthCoverage = maxWavelengthCoverage,
                MaxMeltFrequency = maxMeltFrequency
            };

            var filtered = GlassFilterService.Apply(allGlasses, criteria);

            if (filtered.Count == 0)
                return Task.FromResult(new ExportResult(false, "No glasses match the filter criteria", null, 0, null));

            // Check for duplicate names
            var duplicates = CatalogExportService.FindDuplicateNames(filtered);
            if (duplicates.Count > 0)
                return Task.FromResult(new ExportResult(false, "Duplicate glass names found across catalogs", null, 0, duplicates));

            // Export
            string outputPath = Path.Combine(glassCatDir, catalogName + ".agf");
            CatalogExportService.Export(filtered, outputPath, catalogName);

            return Task.FromResult(new ExportResult(true, null, outputPath, filtered.Count, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ExportResult(false, ex.Message, null, 0, null));
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
