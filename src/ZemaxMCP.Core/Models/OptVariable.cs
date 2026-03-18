namespace ZemaxMCP.Core.Models;

public class OptVariable
{
    public int VariableNumber { get; set; }
    public string Description { get; set; } = "";
    public VariableType Type { get; set; }
    public int SurfaceNumber { get; set; } = -1;
    public int ParameterNumber { get; set; } = -1;
    public int FieldNumber { get; set; } = -1;
    public int ConfigOperandRow { get; set; } = -1;
    public int ConfigColumn { get; set; } = -1;
    public double Value { get; set; }
    public double StartingValue { get; set; }
    public ConstraintType Constraint { get; set; } = ConstraintType.Unconstrained;
    public double Min { get; set; }
    public double Max { get; set; }

    public double LowerBound => Constraint switch
    {
        ConstraintType.MinAndMax or ConstraintType.MinOnly => Min,
        _ => -1e20
    };

    public double UpperBound => Constraint switch
    {
        ConstraintType.MinAndMax or ConstraintType.MaxOnly => Max,
        _ => 1e20
    };

    public string CompositeKey =>
        $"{Type}|{SurfaceNumber}|{ParameterNumber}|{FieldNumber}|{ConfigOperandRow}|{ConfigColumn}";
}
