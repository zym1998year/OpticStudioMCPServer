using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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

    public Dictionary<string, StoredConstraint> GetAll()
    {
        return new Dictionary<string, StoredConstraint>(_constraints);
    }

    public void Clear()
    {
        _constraints.Clear();
    }

    /// <summary>
    /// Save all constraints to a sidecar JSON file next to the given Zemax file.
    /// </summary>
    public void SaveToFile(string zemaxFilePath)
    {
        var sidecarPath = GetSidecarPath(zemaxFilePath);
        var snapshot = GetAll();

        if (snapshot.Count == 0)
        {
            if (File.Exists(sidecarPath))
                File.Delete(sidecarPath);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[");
        int i = 0;
        foreach (var kvp in snapshot)
        {
            if (i > 0) sb.AppendLine(",");
            sb.AppendLine("  {");
            sb.AppendLine($"    \"CompositeKey\": \"{EscapeJson(kvp.Key)}\",");
            sb.AppendLine($"    \"Constraint\": \"{kvp.Value.Constraint}\",");
            sb.AppendLine($"    \"Min\": {kvp.Value.Min.ToString("R", CultureInfo.InvariantCulture)},");
            sb.AppendLine($"    \"Max\": {kvp.Value.Max.ToString("R", CultureInfo.InvariantCulture)}");
            sb.Append("  }");
            i++;
        }
        sb.AppendLine();
        sb.AppendLine("]");

        File.WriteAllText(sidecarPath, sb.ToString());
    }

    /// <summary>
    /// Load constraints from a sidecar JSON file if it exists. Replaces current constraints.
    /// </summary>
    /// <returns>Number of constraints loaded, or 0 if no sidecar file found.</returns>
    public int LoadFromFile(string zemaxFilePath)
    {
        var sidecarPath = GetSidecarPath(zemaxFilePath);
        if (!File.Exists(sidecarPath))
            return 0;

        var json = File.ReadAllText(sidecarPath);
        var entries = ParseEntries(json);
        if (entries.Count == 0)
            return 0;

        _constraints.Clear();
        foreach (var entry in entries)
        {
            _constraints[entry.CompositeKey] = new StoredConstraint(entry.Constraint, entry.Min, entry.Max);
        }

        return entries.Count;
    }

    public static string GetSidecarPath(string zemaxFilePath)
    {
        return zemaxFilePath + ".constraints.json";
    }

    public record StoredConstraint(ConstraintType Constraint, double Min, double Max);

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static List<ParsedEntry> ParseEntries(string json)
    {
        var results = new List<ParsedEntry>();

        // Match each JSON object in the array
        var objectPattern = new Regex(@"\{[^}]+\}", RegexOptions.Singleline);
        var matches = objectPattern.Matches(json);

        foreach (Match match in matches)
        {
            var obj = match.Value;
            var key = ExtractStringValue(obj, "CompositeKey");
            var constraintStr = ExtractStringValue(obj, "Constraint");
            var min = ExtractDoubleValue(obj, "Min");
            var max = ExtractDoubleValue(obj, "Max");

            if (key != null && constraintStr != null &&
                Enum.TryParse<ConstraintType>(constraintStr, ignoreCase: true, out var constraint))
            {
                results.Add(new ParsedEntry(key, constraint, min, max));
            }
        }

        return results;
    }

    private static string? ExtractStringValue(string json, string property)
    {
        var pattern = new Regex($"\"{Regex.Escape(property)}\"\\s*:\\s*\"([^\"]*)\"");
        var match = pattern.Match(json);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static double ExtractDoubleValue(string json, string property)
    {
        var pattern = new Regex($"\"{Regex.Escape(property)}\"\\s*:\\s*([\\d.eE+\\-]+)");
        var match = pattern.Match(json);
        if (match.Success && double.TryParse(match.Groups[1].Value,
            NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;
        return 0;
    }

    private record ParsedEntry(string CompositeKey, ConstraintType Constraint, double Min, double Max);
}
