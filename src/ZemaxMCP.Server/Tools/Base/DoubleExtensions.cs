namespace ZemaxMCP.Server.Tools.Base;

/// <summary>
/// Extension methods for handling special floating-point values (Infinity, NaN)
/// that cannot be serialized to JSON.
/// </summary>
public static class DoubleExtensions
{
    /// <summary>
    /// Sanitizes a double value for JSON serialization.
    /// Converts Infinity to a large finite value and NaN to 0.
    /// </summary>
    /// <param name="value">The value to sanitize</param>
    /// <param name="infinityReplacement">Value to use for infinity (default: 1e18)</param>
    /// <returns>A JSON-safe double value</returns>
    public static double Sanitize(this double value, double infinityReplacement = 1e18)
    {
        if (double.IsPositiveInfinity(value))
            return infinityReplacement;
        if (double.IsNegativeInfinity(value))
            return -infinityReplacement;
        if (double.IsNaN(value))
            return 0;
        return value;
    }

    /// <summary>
    /// Sanitizes a radius value, using 0 for infinite radius (flat surface).
    /// </summary>
    /// <param name="radius">The radius value</param>
    /// <returns>0 for infinite radius (flat), otherwise the original value</returns>
    public static double SanitizeRadius(this double radius)
    {
        // In optics, infinite radius means flat surface, conventionally represented as 0
        if (double.IsInfinity(radius) || double.IsNaN(radius))
            return 0;
        return radius;
    }
}
