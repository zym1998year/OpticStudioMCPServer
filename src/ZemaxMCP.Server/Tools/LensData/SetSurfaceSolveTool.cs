using System.ComponentModel;
using ModelContextProtocol.Server;
using ZOSAPI.Editors;
using ZOSAPI.Editors.LDE;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetSurfaceSolveTool
{
    private readonly IZemaxSession _session;

    public SetSurfaceSolveTool(IZemaxSession session) => _session = session;

    public record SetSurfaceSolveResult(
        bool Success,
        string? Error,
        SolveData? NewSolveData
    );

    [McpServerTool(Name = "zemax_set_surface_solve")]
    [Description("Set the solve type for a surface property (radius, thickness, conic, semiDiameter, material, or parameter)")]
    public async Task<SetSurfaceSolveResult> ExecuteAsync(
        [Description("Surface number")] int surfaceNumber,
        [Description("Property to set: radius, thickness, conic, semiDiameter, material, or param1-param8")] string property,
        [Description("Solve type: Fixed, Variable, Pickup, MarginalRayHeight, MarginalRayAngle, ChiefRayHeight, ChiefRayAngle, EdgeThickness, Position, FNumber, CenterOfCurvature, PupilPosition, MaterialSubstitute, MaterialOffset")] string solveType,
        [Description("For Pickup: source surface number")] int? pickupSurface = null,
        [Description("For Pickup: source column (0=radius, 1=thickness, 2=conic, etc.)")] int? pickupColumn = null,
        [Description("For Pickup: scale factor")] double? scaleFactor = null,
        [Description("For Pickup: offset value")] double? offset = null,
        [Description("For MarginalRay/ChiefRay: height or angle value")] double? height = null,
        [Description("For MarginalRay: pupil zone (0-1)")] double? pupilZone = null,
        [Description("For EdgeThickness: thickness value")] double? thickness = null,
        [Description("For EdgeThickness: radial height")] double? radialHeight = null,
        [Description("For Position: distance from surface")] double? position = null,
        [Description("For FNumber: F/# value")] double? fNumber = null,
        [Description("For CenterOfCurvature: reference surface")] int? referenceSurface = null,
        [Description("For MaterialSubstitute: catalog name")] string? catalog = null,
        [Description("For MaterialSubstitute: material name")] string? materialName = null,
        [Description("For MaterialOffset: index offset")] double? indexOffset = null)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber,
                ["property"] = property,
                ["solveType"] = solveType
            };

            var result = await _session.ExecuteAsync("SetSurfaceSolve", parameters, system =>
            {
                var lde = system.LDE;
                var surfNum = surfaceNumber;

                if (surfaceNumber == -1)
                {
                    surfNum = lde.NumberOfSurfaces - 1;
                }

                if (surfNum < 0 || surfNum >= lde.NumberOfSurfaces)
                {
                    throw new ArgumentException(
                        $"Invalid surface number: {surfaceNumber}. Valid range: 0-{lde.NumberOfSurfaces - 1}");
                }

                var row = lde.GetSurfaceAt(surfNum);
                dynamic? cell = GetCellForProperty(row, property);

                if (cell == null)
                {
                    throw new ArgumentException($"Invalid property: {property}");
                }

                // Apply the solve based on type
                ApplySolve(cell, solveType, pickupSurface, pickupColumn, scaleFactor, offset,
                    height, pupilZone, thickness, radialHeight, position, fNumber,
                    referenceSurface, catalog, materialName, indexOffset);

                // Return the new solve data
                var newSolveData = GetSolveDataFromCell(cell);

                return new SetSurfaceSolveResult(
                    Success: true,
                    Error: null,
                    NewSolveData: newSolveData
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SetSurfaceSolveResult(false, ex.Message, null);
        }
    }

    private static dynamic? GetCellForProperty(ILDERow row, string property)
    {
        return property.ToLowerInvariant() switch
        {
            "radius" => row.RadiusCell,
            "thickness" => row.ThicknessCell,
            "conic" => row.ConicCell,
            "semidiameter" => row.SemiDiameterCell,
            "material" => row.MaterialCell,
            "param1" => row.GetSurfaceCell(SurfaceColumn.Par1),
            "param2" => row.GetSurfaceCell(SurfaceColumn.Par2),
            "param3" => row.GetSurfaceCell(SurfaceColumn.Par3),
            "param4" => row.GetSurfaceCell(SurfaceColumn.Par4),
            "param5" => row.GetSurfaceCell(SurfaceColumn.Par5),
            "param6" => row.GetSurfaceCell(SurfaceColumn.Par6),
            "param7" => row.GetSurfaceCell(SurfaceColumn.Par7),
            "param8" => row.GetSurfaceCell(SurfaceColumn.Par8),
            _ => null
        };
    }

    private static void ApplySolve(dynamic cell, string solveType,
        int? pickupSurface, int? pickupColumn, double? scaleFactor, double? offset,
        double? height, double? pupilZone, double? thickness, double? radialHeight,
        double? position, double? fNumber, int? referenceSurface,
        string? catalog, string? materialName, double? indexOffset)
    {
        switch (solveType.ToLowerInvariant())
        {
            case "fixed":
                cell.MakeSolveFixed();
                break;

            case "variable":
                cell.MakeSolveVariable();
                break;

            case "pickup":
                if (!pickupSurface.HasValue)
                    throw new ArgumentException("Pickup solve requires pickupSurface parameter");
                cell.MakeSolvePickup(
                    pickupSurface.Value,
                    pickupColumn ?? 0,
                    scaleFactor ?? 1.0,
                    offset ?? 0.0);
                break;

            case "marginalrayheight":
                cell.MakeSolveMarginalRayHeight(
                    height ?? 0.0,
                    pupilZone ?? 1.0);
                break;

            case "marginalrayangle":
                cell.MakeSolveMarginalRayAngle(
                    height ?? 0.0,
                    pupilZone ?? 1.0);
                break;

            case "chiefrayheight":
                cell.MakeSolveChiefRayHeight(height ?? 0.0);
                break;

            case "chiefrayangle":
                cell.MakeSolveChiefRayAngle(height ?? 0.0);
                break;

            case "edgethickness":
                cell.MakeSolveEdgeThickness(
                    thickness ?? 0.0,
                    radialHeight ?? 0.0);
                break;

            case "position":
                if (!referenceSurface.HasValue)
                    throw new ArgumentException("Position solve requires referenceSurface parameter");
                cell.MakeSolvePosition(
                    referenceSurface.Value,
                    position ?? 0.0);
                break;

            case "fnumber":
                cell.MakeSolveFNumber(fNumber ?? 0.0);
                break;

            case "centerofcurvature":
                if (!referenceSurface.HasValue)
                    throw new ArgumentException("CenterOfCurvature solve requires referenceSurface parameter");
                cell.MakeSolveCenterOfCurvature(referenceSurface.Value);
                break;

            case "pupilposition":
                cell.MakeSolvePupilPosition();
                break;

            case "materialsubstitute":
                if (string.IsNullOrEmpty(catalog) || string.IsNullOrEmpty(materialName))
                    throw new ArgumentException("MaterialSubstitute solve requires catalog and materialName parameters");
                cell.MakeSolveMaterialSubstitute(catalog, materialName);
                break;

            case "materialoffset":
                cell.MakeSolveMaterialOffset(indexOffset ?? 0.0);
                break;

            default:
                throw new ArgumentException($"Unknown solve type: {solveType}");
        }
    }

    private static SolveData GetSolveDataFromCell(dynamic cell)
    {
        var solveData = cell.GetSolveData();
        var solveType = solveData.Type.ToString();

        var result = new SolveData
        {
            SolveType = solveType
        };

        switch ((SolveType)solveData.Type)
        {
            case SolveType.SurfacePickup:
                result = result with
                {
                    PickupSurface = solveData.Pickup_Surface,
                    PickupColumn = solveData.Pickup_Column,
                    ScaleFactor = solveData.Pickup_ScaleFactor,
                    Offset = solveData.Pickup_Offset
                };
                break;

            case SolveType.MarginalRayHeight:
                result = result with
                {
                    Height = solveData.MarginalRayHeight_Height,
                    PupilZone = solveData.MarginalRayHeight_PupilZone
                };
                break;

            case SolveType.MarginalRayAngle:
                result = result with
                {
                    Height = solveData.MarginalRayAngle_Angle,
                    PupilZone = solveData.MarginalRayAngle_PupilZone
                };
                break;

            case SolveType.ChiefRayHeight:
                result = result with
                {
                    Height = solveData.ChiefRayHeight_Height
                };
                break;

            case SolveType.ChiefRayAngle:
                result = result with
                {
                    Height = solveData.ChiefRayAngle_Angle
                };
                break;

            case SolveType.EdgeThickness:
                result = result with
                {
                    Thickness = solveData.EdgeThickness_Thickness,
                    RadialHeight = solveData.EdgeThickness_RadialHeight
                };
                break;

            case SolveType.Position:
                result = result with
                {
                    Position = solveData.Position_FromSurface
                };
                break;

            case SolveType.FNumber:
                result = result with
                {
                    FNumber = solveData.FNumber_FNumber
                };
                break;

            case SolveType.CenterOfCurvature:
                result = result with
                {
                    ReferenceSurface = solveData.CenterOfCurvature_Surface
                };
                break;

            case SolveType.MaterialSubstitute:
                result = result with
                {
                    Catalog = solveData.MaterialSubstitute_Catalog,
                    MaterialName = solveData.MaterialSubstitute_Material
                };
                break;

            case SolveType.MaterialOffset:
                result = result with
                {
                    IndexOffset = solveData.MaterialOffset_Offset
                };
                break;
        }

        return result;
    }
}
