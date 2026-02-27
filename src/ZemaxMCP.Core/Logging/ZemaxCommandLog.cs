using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace ZemaxMCP.Core.Logging;

/// <summary>
/// Logs all commands sent to ZEMAX OpticStudio to a dedicated log file.
/// Each session creates a new log file with timestamp in the filename.
/// </summary>
public class ZemaxCommandLog : IZemaxCommandLog, IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public string LogFilePath => _logFilePath;

    public ZemaxCommandLog(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");

        // Ensure log directory exists
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        // Create log file with session timestamp
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        _logFilePath = Path.Combine(_logDirectory, $"zemax-commands-{timestamp}.log");

        _writer = new StreamWriter(_logFilePath, append: false, Encoding.UTF8)
        {
            AutoFlush = true
        };

        // Write header
        WriteHeader();
    }

    private void WriteHeader()
    {
        lock (_lock)
        {
            _writer.WriteLine("================================================================================");
            _writer.WriteLine($"ZEMAX MCP Server - Command Log");
            _writer.WriteLine($"Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            _writer.WriteLine($"Machine: {Environment.MachineName}");
            _writer.WriteLine($"User: {Environment.UserName}");
            _writer.WriteLine("================================================================================");
            _writer.WriteLine();
        }
    }

    public void LogCommand(string commandName, IDictionary<string, object?>? parameters = null)
    {
        lock (_lock)
        {
            if (_disposed) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            _writer.WriteLine($"[{timestamp}] COMMAND: {commandName}");

            if (parameters != null && parameters.Count > 0)
            {
                _writer.WriteLine("  Parameters:");
                foreach (var param in parameters)
                {
                    var value = FormatValue(param.Value);
                    _writer.WriteLine($"    {param.Key}: {value}");
                }
            }
        }
    }

    public void LogResult(string commandName, bool success, object? result = null, double elapsedMs = 0)
    {
        lock (_lock)
        {
            if (_disposed) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var status = success ? "SUCCESS" : "FAILED";
            var elapsed = elapsedMs > 0 ? $" ({elapsedMs:F1}ms)" : "";

            _writer.WriteLine($"[{timestamp}] RESULT: {commandName} -> {status}{elapsed}");

            if (result != null)
            {
                var resultStr = FormatValue(result);
                // Limit result output to avoid huge log entries
                if (resultStr.Length > 500)
                {
                    resultStr = resultStr.Substring(0, 500) + "... (truncated)";
                }
                _writer.WriteLine($"  Result: {resultStr}");
            }
            _writer.WriteLine();
        }
    }

    public void LogError(string commandName, Exception exception)
    {
        lock (_lock)
        {
            if (_disposed) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            _writer.WriteLine($"[{timestamp}] ERROR: {commandName}");
            _writer.WriteLine($"  Exception: {exception.GetType().Name}");
            _writer.WriteLine($"  Message: {exception.Message}");

            if (exception.InnerException != null)
            {
                _writer.WriteLine($"  Inner: {exception.InnerException.Message}");
            }

            _writer.WriteLine($"  StackTrace:");
            foreach (var line in (exception.StackTrace ?? "").Split('\n').Take(10))
            {
                _writer.WriteLine($"    {line.Trim()}");
            }
            _writer.WriteLine();
        }
    }

    public void LogOperation(string operation)
    {
        lock (_lock)
        {
            if (_disposed) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            _writer.WriteLine($"[{timestamp}] OPERATION: {operation}");
        }
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
            return "null";

        return value switch
        {
            string s => $"\"{s}\"",
            bool b => b.ToString().ToLowerInvariant(),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            float f => f.ToString("G", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            IEnumerable enumerable when value.GetType() != typeof(string) => FormatEnumerable(enumerable),
            _ when value.GetType().IsClass && value.GetType() != typeof(string) =>
                FormatObject(value),
            _ => value.ToString() ?? "null"
        };
    }

    private static string FormatEnumerable(IEnumerable enumerable)
    {
        var items = new List<string>();
        foreach (var item in enumerable)
        {
            items.Add(FormatValue(item));
        }
        return $"[{string.Join(", ", items)}]";
    }

    private static string FormatObject(object value)
    {
        try
        {
            var type = value.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            if (properties.Length == 0)
                return value.ToString() ?? "null";

            var sb = new StringBuilder();
            sb.Append("{");
            var first = true;
            foreach (var prop in properties.Take(10)) // Limit to first 10 properties
            {
                try
                {
                    var propValue = prop.GetValue(value);
                    if (!first) sb.Append(", ");
                    sb.Append($"{prop.Name}: {FormatValue(propValue)}");
                    first = false;
                }
                catch
                {
                    // Skip properties that throw exceptions
                }
            }
            sb.Append("}");
            return sb.ToString();
        }
        catch
        {
            return value.ToString() ?? "null";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            _writer.WriteLine();
            _writer.WriteLine("================================================================================");
            _writer.WriteLine($"Session Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            _writer.WriteLine("================================================================================");
            _writer.Dispose();
        }
    }
}
