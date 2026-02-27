namespace ZemaxMCP.Documentation;

public class OperandDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public List<ParameterDefinition> Parameters { get; set; } = new();
    public string Example { get; set; } = "";
    public List<string> RelatedOperands { get; set; } = new();
}

public class ParameterDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public string DataType { get; set; } = "double";
}

public class SearchResult
{
    public OperandDefinition Operand { get; set; } = new();
    public double Score { get; set; }
}
