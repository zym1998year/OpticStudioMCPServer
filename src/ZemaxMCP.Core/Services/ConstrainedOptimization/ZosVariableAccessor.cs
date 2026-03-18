using ZemaxMCP.Core.Models;
using ZOSAPI;
using ZOSAPI.Editors;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

public static class ZosVariableAccessor
{
    public static double GetVariableValue(IOpticalSystem system, OptVariable variable)
    {
        var lde = system.LDE;

        return variable.Type switch
        {
            VariableType.Curvature => GetCurvature(lde.GetSurfaceAt(variable.SurfaceNumber)),
            VariableType.Thickness => lde.GetSurfaceAt(variable.SurfaceNumber).Thickness,
            VariableType.Conic => lde.GetSurfaceAt(variable.SurfaceNumber).Conic,
            VariableType.Parameter => GetParameter(lde.GetSurfaceAt(variable.SurfaceNumber), variable.ParameterNumber),
            VariableType.FieldX => system.SystemData.Fields.GetField(variable.FieldNumber).X,
            VariableType.FieldY => system.SystemData.Fields.GetField(variable.FieldNumber).Y,
            VariableType.ConfigOperand => system.MCE.GetOperandAt(variable.ConfigOperandRow)
                .GetCellAt(variable.ConfigColumn).DoubleValue,
            VariableType.ModelNd or VariableType.ModelVd or VariableType.ModelDpgF =>
                GetModelGlassValue(lde.GetSurfaceAt(variable.SurfaceNumber), variable.Type),
            _ => throw new ArgumentException($"Unknown variable type: {variable.Type}")
        };
    }

    public static void SetVariableValue(IOpticalSystem system, OptVariable variable, double value)
    {
        var lde = system.LDE;

        switch (variable.Type)
        {
            case VariableType.Curvature:
                lde.GetSurfaceAt(variable.SurfaceNumber).Radius = (value == 0) ? 1e18 : 1.0 / value;
                break;
            case VariableType.Thickness:
                lde.GetSurfaceAt(variable.SurfaceNumber).Thickness = value;
                break;
            case VariableType.Conic:
                lde.GetSurfaceAt(variable.SurfaceNumber).Conic = value;
                break;
            case VariableType.Parameter:
                {
                    var surface = lde.GetSurfaceAt(variable.SurfaceNumber);
                    var col = SurfaceColumn.Par0 + variable.ParameterNumber;
                    surface.GetSurfaceCell(col).DoubleValue = value;
                    break;
                }
            case VariableType.FieldX:
                system.SystemData.Fields.GetField(variable.FieldNumber).X = value;
                break;
            case VariableType.FieldY:
                system.SystemData.Fields.GetField(variable.FieldNumber).Y = value;
                break;
            case VariableType.ConfigOperand:
                system.MCE.GetOperandAt(variable.ConfigOperandRow)
                    .GetCellAt(variable.ConfigColumn).DoubleValue = value;
                break;
            case VariableType.ModelNd:
            case VariableType.ModelVd:
            case VariableType.ModelDpgF:
                SetModelGlassValue(lde.GetSurfaceAt(variable.SurfaceNumber), variable.Type, value);
                break;
        }
    }

    public static string GetGlassMaterial(IOpticalSystem system, int surfaceIndex)
    {
        return system.LDE.GetSurfaceAt(surfaceIndex).Material;
    }

    public static void SetGlassMaterial(IOpticalSystem system, int surfaceIndex, string materialName)
    {
        var surface = system.LDE.GetSurfaceAt(surfaceIndex);
        var solveData = surface.MaterialCell.GetSolveData();

        if (solveData.Type == SolveType.MaterialSubstitute)
        {
            var sub = solveData._S_MaterialSubstitute;
            string catalog = sub != null ? sub.Catalog : surface.MaterialCatalog;
            surface.Material = materialName;
            var newSolve = surface.MaterialCell.CreateSolveType(SolveType.MaterialSubstitute);
            newSolve._S_MaterialSubstitute.Catalog = catalog;
            surface.MaterialCell.SetSolveData(newSolve);
        }
        else
        {
            surface.Material = materialName;
        }
    }

    private static double GetCurvature(ILDERow surface)
    {
        double r = surface.Radius;
        return (r == 0) ? 0 : 1.0 / r;
    }

    private static double GetParameter(ILDERow surface, int parameterNumber)
    {
        var col = SurfaceColumn.Par0 + parameterNumber;
        return surface.GetSurfaceCell(col).DoubleValue;
    }

    private static double GetModelGlassValue(ILDERow surface, VariableType type)
    {
        var solveData = surface.MaterialCell.GetSolveData();
        var model = solveData._S_MaterialModel;
        return type switch
        {
            VariableType.ModelNd => model.IndexNd,
            VariableType.ModelVd => model.AbbeVd,
            VariableType.ModelDpgF => model.dPgF,
            _ => 0
        };
    }

    private static void SetModelGlassValue(ILDERow surface, VariableType type, double value)
    {
        var solveData = surface.MaterialCell.GetSolveData();
        var model = solveData._S_MaterialModel;
        double nd = model.IndexNd;
        double vd = model.AbbeVd;
        double dpgf = model.dPgF;
        bool varyNd = model.VaryIndex;
        bool varyVd = model.VaryAbbe;
        bool varyDpgf = model.VarydPgF;

        switch (type)
        {
            case VariableType.ModelNd: nd = value; break;
            case VariableType.ModelVd: vd = value; break;
            case VariableType.ModelDpgF: dpgf = value; break;
        }

        var newSolve = surface.MaterialCell.CreateSolveType(SolveType.MaterialModel);
        var newModel = newSolve._S_MaterialModel;
        newModel.IndexNd = nd;
        newModel.AbbeVd = vd;
        newModel.dPgF = dpgf;
        newModel.VaryIndex = varyNd;
        newModel.VaryAbbe = varyVd;
        newModel.VarydPgF = varyDpgf;
        surface.MaterialCell.SetSolveData(newSolve);
    }
}
