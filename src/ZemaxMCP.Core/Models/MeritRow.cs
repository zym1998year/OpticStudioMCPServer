namespace ZemaxMCP.Core.Models;

public class MeritRow
{
    public int RowNumber { get; set; }
    public string TypeName { get; set; } = "";
    public double Target { get; set; }
    public double Value { get; set; }
    public double Weight { get; set; }
}
