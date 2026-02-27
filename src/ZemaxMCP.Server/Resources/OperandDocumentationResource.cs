using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Documentation;

namespace ZemaxMCP.Server.Resources;

[McpServerResourceType]
public class OperandDocumentationResource
{
    private readonly OperandDatabase _operandDb;

    public OperandDocumentationResource(OperandDatabase operandDb)
        => _operandDb = operandDb;

    [McpServerResource(Name = "zemax://docs/operands")]
    [Description("Optimization operand documentation and reference")]
    public Task<string> GetAsync()
    {
        var content = _operandDb.GenerateFullReference();
        return Task.FromResult(content);
    }
}
