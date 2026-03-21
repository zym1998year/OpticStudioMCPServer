using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;
using ZemaxMCP.Server.Tools.Base;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class GetSystemDataTool
{
    private readonly IZemaxSession _session;

    public GetSystemDataTool(IZemaxSession session) => _session = session;

    [McpServerTool(Name = "zemax_get_system")]
    [Description("Get the current optical system data including surfaces, fields, and wavelengths")]
    public async Task<LensSystem> ExecuteAsync(
        [Description("Include all surface details")] bool includeSurfaces = true,
        [Description("Include field definitions")] bool includeFields = true,
        [Description("Include wavelength definitions")] bool includeWavelengths = true)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["includeSurfaces"] = includeSurfaces,
            ["includeFields"] = includeFields,
            ["includeWavelengths"] = includeWavelengths
        };

        return await _session.ExecuteAsync("GetSystem", parameters, system =>
        {
            var lde = system.LDE;
            var sysProp = system.SystemData;

            var lensSystem = new LensSystem
            {
                FilePath = system.SystemFile,
                Title = Path.GetFileNameWithoutExtension(system.SystemFile),
                Notes = "",
                NumberOfSurfaces = lde.NumberOfSurfaces,
                Units = sysProp.Units.ToString(),
                Aperture = new ApertureData
                {
                    Type = (ApertureType)(int)sysProp.Aperture.ApertureType,
                    Value = sysProp.Aperture.ApertureValue
                },
                NumberOfConfigurations = system.MCE.NumberOfConfigurations,
                CurrentConfiguration = system.MCE.CurrentConfiguration
            };

            if (includeSurfaces)
            {
                lensSystem = lensSystem with
                {
                    Surfaces = GetSurfaces(lde)
                };
            }

            if (includeFields)
            {
                lensSystem = lensSystem with
                {
                    Fields = GetFields(sysProp.Fields)
                };
            }

            if (includeWavelengths)
            {
                lensSystem = lensSystem with
                {
                    Wavelengths = GetWavelengths(sysProp.Wavelengths)
                };
            }

            return lensSystem;
        });
    }

    private List<Surface> GetSurfaces(ZOSAPI.Editors.LDE.ILensDataEditor lde)
    {
        var surfaces = new List<Surface>();

        for (int i = 0; i < lde.NumberOfSurfaces; i++)
        {
            var row = lde.GetSurfaceAt(i);
            var materialSolve = row.MaterialCell.Solve;
            string? substituteCatalog = null;
            if (materialSolve == ZOSAPI.Editors.SolveType.MaterialSubstitute)
            {
                var solveData = row.MaterialCell.GetSolveData();
                substituteCatalog = solveData._S_MaterialSubstitute?.Catalog;
            }

            surfaces.Add(new Surface
            {
                Number = i,
                Comment = row.Comment,
                Radius = row.Radius.SanitizeRadius(),
                Thickness = row.Thickness.Sanitize(),
                Material = row.Material,
                SemiDiameter = row.SemiDiameter.Sanitize(),
                Conic = row.Conic.Sanitize(),
                SurfaceType = row.Type.ToString(),
                IsStop = row.IsStop,
                RadiusSolve = MapSolveType(row.RadiusCell.Solve),
                ThicknessSolve = MapSolveType(row.ThicknessCell.Solve),
                ConicSolve = MapSolveType(row.ConicCell.Solve),
                MaterialSolve = MapSolveType(materialSolve),
                MaterialSubstituteCatalog = substituteCatalog
            });
        }

        return surfaces;
    }

    private List<Field> GetFields(ZOSAPI.SystemData.IFields fields)
    {
        var result = new List<Field>();

        for (int i = 1; i <= fields.NumberOfFields; i++)
        {
            var field = fields.GetField(i);
            result.Add(new Field
            {
                Number = i,
                X = field.X,
                Y = field.Y,
                Weight = field.Weight,
                VDX = field.VDX,
                VDY = field.VDY,
                VCX = field.VCX,
                VCY = field.VCY
            });
        }

        return result;
    }

    private List<Wavelength> GetWavelengths(ZOSAPI.SystemData.IWavelengths wavelengths)
    {
        var result = new List<Wavelength>();

        for (int i = 1; i <= wavelengths.NumberOfWavelengths; i++)
        {
            var wl = wavelengths.GetWavelength(i);
            result.Add(new Wavelength
            {
                Number = i,
                Value = wl.Wavelength,
                Weight = wl.Weight,
                IsPrimary = false // Can't determine primary without additional API
            });
        }

        return result;
    }

    internal static string MapSolveType(ZOSAPI.Editors.SolveType solveType) => solveType switch
    {
        ZOSAPI.Editors.SolveType.Variable => "V",
        ZOSAPI.Editors.SolveType.SurfacePickup => "P",
        ZOSAPI.Editors.SolveType.MarginalRayHeight => "M",
        ZOSAPI.Editors.SolveType.MarginalRayAngle => "MA",
        ZOSAPI.Editors.SolveType.ChiefRayHeight => "C",
        ZOSAPI.Editors.SolveType.ChiefRayAngle => "CA",
        ZOSAPI.Editors.SolveType.EdgeThickness => "E",
        ZOSAPI.Editors.SolveType.FNumber => "F",
        ZOSAPI.Editors.SolveType.Position => "Pos",
        ZOSAPI.Editors.SolveType.CenterOfCurvature => "CC",
        ZOSAPI.Editors.SolveType.PupilPosition => "PP",
        ZOSAPI.Editors.SolveType.MaterialSubstitute => "MS",
        ZOSAPI.Editors.SolveType.MaterialOffset => "MO",
        _ => ""
    };
}
