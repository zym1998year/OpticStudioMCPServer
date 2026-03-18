using ZemaxMCP.Core.Models;
using ZOSAPI;
using ZOSAPI.Editors;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

public class VariableScanner
{
    public List<OptVariable> ScanVariables(IOpticalSystem system)
    {
        var variables = new List<OptVariable>();
        int varNum = 1;

        var lde = system.LDE;
        int numSurfaces = lde.NumberOfSurfaces;

        for (int i = 0; i < numSurfaces; i++)
        {
            ILDERow surface = lde.GetSurfaceAt(i);
            int surfNum = surface.SurfaceNumber;

            // Check Radius → store as Curvature (1/R)
            if (surface.RadiusCell.Solve == SolveType.Variable)
            {
                double r = surface.Radius;
                double c = (r == 0) ? 0 : 1.0 / r;
                variables.Add(new OptVariable
                {
                    VariableNumber = varNum++,
                    Description = $"Surface {surfNum} Curvature",
                    Type = VariableType.Curvature,
                    SurfaceNumber = i,
                    Value = c,
                    StartingValue = c
                });
            }

            // Check Thickness
            if (surface.ThicknessCell.Solve == SolveType.Variable)
            {
                variables.Add(new OptVariable
                {
                    VariableNumber = varNum++,
                    Description = $"Surface {surfNum} Thickness",
                    Type = VariableType.Thickness,
                    SurfaceNumber = i,
                    Value = surface.Thickness,
                    StartingValue = surface.Thickness
                });
            }

            // Check Conic
            if (surface.ConicCell.Solve == SolveType.Variable)
            {
                variables.Add(new OptVariable
                {
                    VariableNumber = varNum++,
                    Description = $"Surface {surfNum} Conic",
                    Type = VariableType.Conic,
                    SurfaceNumber = i,
                    Value = surface.Conic,
                    StartingValue = surface.Conic
                });
            }

            // Check Parameters 1-40
            for (int p = 1; p <= 40; p++)
            {
                SurfaceColumn col = SurfaceColumn.Par0 + p;
                var cell = surface.GetSurfaceCell(col);
                if (cell.IsActive && cell.Solve == SolveType.Variable)
                {
                    double cellVal = cell.DoubleValue;
                    variables.Add(new OptVariable
                    {
                        VariableNumber = varNum++,
                        Description = $"Surface {surfNum} Param {p}",
                        Type = VariableType.Parameter,
                        SurfaceNumber = i,
                        ParameterNumber = p,
                        Value = cellVal,
                        StartingValue = cellVal
                    });
                }
            }

            // Check Model Glass (Nd, Vd, dPgF)
            var materialSolve = surface.MaterialCell.GetSolveData();
            if (materialSolve.Type == SolveType.MaterialModel)
            {
                var model = materialSolve._S_MaterialModel;
                if (model != null)
                {
                    if (model.VaryIndex)
                    {
                        variables.Add(new OptVariable
                        {
                            VariableNumber = varNum++,
                            Description = $"Surface {surfNum} Model Nd",
                            Type = VariableType.ModelNd,
                            SurfaceNumber = i,
                            Value = model.IndexNd,
                            StartingValue = model.IndexNd
                        });
                    }
                    if (model.VaryAbbe)
                    {
                        variables.Add(new OptVariable
                        {
                            VariableNumber = varNum++,
                            Description = $"Surface {surfNum} Model Vd",
                            Type = VariableType.ModelVd,
                            SurfaceNumber = i,
                            Value = model.AbbeVd,
                            StartingValue = model.AbbeVd
                        });
                    }
                    if (model.VarydPgF)
                    {
                        variables.Add(new OptVariable
                        {
                            VariableNumber = varNum++,
                            Description = $"Surface {surfNum} Model dPgF",
                            Type = VariableType.ModelDpgF,
                            SurfaceNumber = i,
                            Value = model.dPgF,
                            StartingValue = model.dPgF
                        });
                    }
                }
            }
        }

        // Scan Fields (1-indexed in ZOS-API)
        int numFields = system.SystemData.Fields.NumberOfFields;
        for (int f = 1; f <= numFields; f++)
        {
            var field = system.SystemData.Fields.GetField(f);

            if (field.XSolve == SolveType.Variable)
            {
                variables.Add(new OptVariable
                {
                    VariableNumber = varNum++,
                    Description = $"Field {f} X",
                    Type = VariableType.FieldX,
                    FieldNumber = f,
                    Value = field.X,
                    StartingValue = field.X
                });
            }

            if (field.YSolve == SolveType.Variable)
            {
                variables.Add(new OptVariable
                {
                    VariableNumber = varNum++,
                    Description = $"Field {f} Y",
                    Type = VariableType.FieldY,
                    FieldNumber = f,
                    Value = field.Y,
                    StartingValue = field.Y
                });
            }
        }

        // Scan MCE (Multi-Configuration Editor) — 1-indexed
        int numConfigs = system.MCE.NumberOfConfigurations;
        if (numConfigs > 1)
        {
            int numOperands = system.MCE.NumberOfOperands;
            for (int row = 1; row <= numOperands; row++)
            {
                var operand = system.MCE.GetOperandAt(row);
                string typeName = operand.TypeName;

                for (int col = 1; col <= numConfigs; col++)
                {
                    var cell = operand.GetCellAt(col);
                    if (cell.Solve == SolveType.Variable)
                    {
                        double cellVal = cell.DoubleValue;
                        variables.Add(new OptVariable
                        {
                            VariableNumber = varNum++,
                            Description = $"MCE Row {row} ({typeName}) Config {col}",
                            Type = VariableType.ConfigOperand,
                            ConfigOperandRow = row,
                            ConfigColumn = col,
                            Value = cellVal,
                            StartingValue = cellVal
                        });
                    }
                }
            }
        }

        return variables;
    }

    public List<MaterialInfo> ScanMaterials(IOpticalSystem system)
    {
        var materials = new List<MaterialInfo>();
        var lde = system.LDE;
        int numSurfaces = lde.NumberOfSurfaces;

        var substituteCatalogs = new Dictionary<string, string[]>();

        for (int i = 0; i < numSurfaces; i++)
        {
            ILDERow surface = lde.GetSurfaceAt(i);
            string material = surface.Material;
            if (string.IsNullOrEmpty(material))
                continue;

            var solveData = surface.MaterialCell.GetSolveData();
            var info = new MaterialInfo
            {
                SurfaceIndex = i,
                SurfaceNumber = surface.SurfaceNumber,
                Material = material,
                Catalog = surface.MaterialCatalog,
                SolveType = solveData.Type
            };

            if (solveData.Type == SolveType.MaterialSubstitute)
            {
                var sub = solveData._S_MaterialSubstitute;
                if (sub != null)
                {
                    info.SubstituteCatalog = sub.Catalog;
                }
            }

            materials.Add(info);
        }

        // Enumerate glasses for each substitute catalog
        foreach (var mat in materials)
        {
            if (mat.SolveType != SolveType.MaterialSubstitute || string.IsNullOrEmpty(mat.SubstituteCatalog))
                continue;

            if (substituteCatalogs.ContainsKey(mat.SubstituteCatalog))
            {
                mat.SubstituteGlasses = substituteCatalogs[mat.SubstituteCatalog];
                continue;
            }

            var catTool = system.Tools.OpenMaterialsCatalog();
            try
            {
                catTool.SelectedCatalog = mat.SubstituteCatalog;
                mat.SubstituteGlasses = catTool.GetAllMaterials();
                substituteCatalogs[mat.SubstituteCatalog] = mat.SubstituteGlasses;
            }
            finally
            {
                catTool.Close();
            }
        }

        return materials;
    }
}
