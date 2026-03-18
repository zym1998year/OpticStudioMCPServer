namespace ZemaxMCP.Core.Models;

public class OptimizationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public double InitialMerit { get; set; }
    public double FinalMerit { get; set; }
    public int Iterations { get; set; }
    public int Restarts { get; set; }
}
