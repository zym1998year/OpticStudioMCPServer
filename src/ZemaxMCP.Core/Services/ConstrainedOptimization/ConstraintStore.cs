using System.Collections.Concurrent;
using ZemaxMCP.Core.Models;

namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

public class ConstraintStore
{
    private readonly ConcurrentDictionary<string, StoredConstraint> _constraints = new();

    public void SetConstraint(string compositeKey, ConstraintType constraint, double min, double max)
    {
        _constraints[compositeKey] = new StoredConstraint(constraint, min, max);
    }

    public void ApplyConstraints(List<OptVariable> variables)
    {
        foreach (var v in variables)
        {
            if (_constraints.TryGetValue(v.CompositeKey, out var stored))
            {
                v.Constraint = stored.Constraint;
                v.Min = stored.Min;
                v.Max = stored.Max;
            }
        }
    }

    public StoredConstraint? GetConstraint(string compositeKey)
    {
        _constraints.TryGetValue(compositeKey, out var stored);
        return stored;
    }

    public void Clear()
    {
        _constraints.Clear();
    }

    public record StoredConstraint(ConstraintType Constraint, double Min, double Max);
}
