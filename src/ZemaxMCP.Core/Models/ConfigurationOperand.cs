namespace ZemaxMCP.Core.Models;

/// <summary>
/// Represents a value for a specific configuration in the MCE
/// </summary>
public record ConfigurationValue
{
    public int ConfigurationNumber { get; init; }
    public double Value { get; init; }
    public string SolveType { get; init; } = "Fixed";
    public int? PickupConfig { get; init; }
    public double? ScaleFactor { get; init; }
    public double? Offset { get; init; }
}

/// <summary>
/// Represents a configuration operand (row) in the Multi-Configuration Editor
/// </summary>
public record ConfigurationOperand
{
    public int OperandNumber { get; init; }
    public string OperandType { get; init; } = "";
    public int Param1 { get; init; }
    public int Param2 { get; init; }
    public int Param3 { get; init; }
    public List<ConfigurationValue> Values { get; init; } = new();
}
