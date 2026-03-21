namespace ZemaxMCP.Core.Models;

public class MultistartResult
{
    public double InitialMerit { get; set; }
    public double PostInitialLmMerit { get; set; }
    public double FinalMerit { get; set; }
    public int TrialsRun { get; set; }
    public int TrialsAccepted { get; set; }
    public int SubstituteMaterialsFound { get; set; }
    public int TotalTrialsRun { get; set; }
    public int TotalTrialsAccepted { get; set; }
    public string? SaveFolder { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
