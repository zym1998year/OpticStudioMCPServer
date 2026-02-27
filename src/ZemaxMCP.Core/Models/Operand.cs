namespace ZemaxMCP.Core.Models;

public record Operand
{
    public int Row { get; init; }
    public string Type { get; init; } = "";
    public int Int1 { get; init; }
    public int Int2 { get; init; }
    public double Data1 { get; init; }
    public double Data2 { get; init; }
    public double Data3 { get; init; }
    public double Data4 { get; init; }
    public double Data5 { get; init; }
    public double Data6 { get; init; }
    public double Target { get; init; }
    public double Weight { get; init; }
    public double Value { get; init; }
    public double Contribution { get; init; }
}

public record MeritFunction
{
    public double TotalMerit { get; init; }
    public int NumberOfOperands { get; init; }
    public List<Operand> Operands { get; init; } = new();
}
