namespace ZemaxMCP.Documentation;

public class OperandSearchService
{
    private readonly OperandDatabase _database;

    public OperandSearchService(OperandDatabase database)
    {
        _database = database;
    }

    public IList<SearchResult> Search(string query, int maxResults = 10, string? category = null)
    {
        var results = _database.SearchOperands(query, maxResults * 2);

        if (!string.IsNullOrEmpty(category))
        {
            results = results.Where(r =>
                r.Operand.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        return results.Take(maxResults).ToList();
    }

    public IList<OperandDefinition> GetByCategory(string category)
    {
        return _database.GetByCategory(category).ToList();
    }

    public IList<string> GetCategories()
    {
        return _database.GetCategories().ToList();
    }

    public OperandDefinition? GetOperand(string name)
    {
        return _database.GetOperand(name);
    }
}
