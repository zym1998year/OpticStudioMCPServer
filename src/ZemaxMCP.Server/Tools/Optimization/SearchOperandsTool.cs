using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Documentation;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class SearchOperandsTool
{
    private readonly OperandSearchService _searchService;

    public SearchOperandsTool(OperandSearchService searchService)
        => _searchService = searchService;

    public record OperandMatch(
        string Name,
        string Description,
        string Category,
        double Relevance
    );

    public record SearchOperandsResult(
        int TotalMatches,
        List<OperandMatch> Matches
    );

    [McpServerTool(Name = "zemax_search_operands")]
    [Description("Search for optimization operands by name or description")]
    public Task<SearchOperandsResult> ExecuteAsync(
        [Description("Search query (e.g., 'spot size', 'MTF', 'thickness')")] string query,
        [Description("Maximum results to return")] int maxResults = 10,
        [Description("Filter by category (e.g., 'aberration', 'boundary', 'ray')")] string? category = null)
    {
        var results = _searchService.Search(query, maxResults, category);

        return Task.FromResult(new SearchOperandsResult(
            TotalMatches: results.Count,
            Matches: results.Select(r => new OperandMatch(
                Name: r.Operand.Name,
                Description: r.Operand.Description,
                Category: r.Operand.Category,
                Relevance: r.Score
            )).ToList()
        ));
    }
}
