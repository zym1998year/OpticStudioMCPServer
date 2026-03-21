using System.ComponentModel;
using ModelContextProtocol.Server;
using ZOSAPI.Editors;
using ZOSAPI.Editors.LDE;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class GetSurfaceSolvesTool
{
    private readonly IZemaxSession _session;

    public GetSurfaceSolvesTool(IZemaxSession session) => _session = session;

    public record GetSurfaceSolvesResult(
        bool Success,
        string? Error,
        SurfaceSolveInfo? SolveInfo
    );

    [McpServerTool(Name = "zemax_get_surface_solves")]
    [Description("Get solve/variable/pickup status for all properties of a surface")]
    public async Task<GetSurfaceSolvesResult> ExecuteAsync(
        [Description("Surface number")] int surfaceNumber)
    {
        try
        {
            var result = await _session.ExecuteAsync("GetSurfaceSolves",
                new Dictionary<string, object?> { ["surfaceNumber"] = surfaceNumber },
                system =>
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

                var solveInfo = new SurfaceSolveInfo
                {
                    SurfaceNumber = surfNum,
                    Radius = GetSolveDataFromCell(row.RadiusCell),
                    Thickness = GetSolveDataFromCell(row.ThicknessCell),
                    Conic = GetSolveDataFromCell(row.ConicCell),
                    SemiDiameter = GetSolveDataFromCell(row.SemiDiameterCell),
                    Material = GetSolveDataFromCell(row.MaterialCell, row),
                    Parameters = GetParameterSolves(row)
                };

                return new GetSurfaceSolvesResult(
                    Success: true,
                    Error: null,
                    SolveInfo: solveInfo
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new GetSurfaceSolvesResult(false, ex.Message, null);
        }
    }

    private static SolveData GetSolveDataFromCell(dynamic cell, ILDERow? row = null)
    {
        var solveData = cell.GetSolveData();
        var solveType = solveData.Type.ToString();

        var result = new SolveData
        {
            SolveType = solveType
        };

        // Extract additional parameters based on solve type
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
                var matSub = solveData._S_MaterialSubstitute;
                result = result with
                {
                    Catalog = matSub?.Catalog,
                    MaterialName = row?.Material
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

    private static Dictionary<int, SolveData> GetParameterSolves(ILDERow row)
    {
        var parameters = new Dictionary<int, SolveData>();

        // Try to get parameters 1-8 if they exist
        var paramColumns = new[] {
            SurfaceColumn.Par1, SurfaceColumn.Par2, SurfaceColumn.Par3, SurfaceColumn.Par4,
            SurfaceColumn.Par5, SurfaceColumn.Par6, SurfaceColumn.Par7, SurfaceColumn.Par8
        };

        for (int i = 0; i < paramColumns.Length; i++)
        {
            try
            {
                var cell = row.GetSurfaceCell(paramColumns[i]);
                if (cell != null)
                {
                    parameters[i + 1] = GetSolveDataFromCell(cell);
                }
            }
            catch
            {
                // Parameter doesn't exist for this surface type
            }
        }

        return parameters;
    }
}
