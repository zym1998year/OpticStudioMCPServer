using ZOSAPI.Editors;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Server.Tools.LensData;

/// Encapsulates the three ZOSAPI access paths for XDAT (Extra Data Editor) cells.
/// ZOSAPI exposes XDAT differently across versions; the helper tries each path
/// in priority order and returns the first IEditorCell that responds.
internal static class ExtraDataHelper
{
    public enum AccessPath { Unknown, SurfaceColumnExtraData, GetCellAtAbsolute, ExtraDataGetCell }

    /// Returns a cell-like object that supports DoubleValue / IntegerValue / MakeSolveVariable.
    /// Callers use dynamic to read/write. Returns null if no path worked.
    /// Records the successful path in <paramref name="pathUsed"/> for diagnostics.
    public static dynamic? TryGetCell(ILDERow surface, int cellNumber, out AccessPath pathUsed)
    {
        pathUsed = AccessPath.Unknown;

        // Path 1: SurfaceColumn.ExtraData0 + N (if the enum value exists)
        try
        {
            var extraDataEnum = (SurfaceColumn)Enum.Parse(typeof(SurfaceColumn), "ExtraData0", ignoreCase: true);
            var col = (SurfaceColumn)((int)extraDataEnum + cellNumber);
            var cell = surface.GetSurfaceCell(col);
            if (cell != null)
            {
                pathUsed = AccessPath.SurfaceColumnExtraData;
                return cell;
            }
        }
        catch { /* enum value missing in this ZOSAPI version; fall through */ }

        // Path 2: GetCellAt(absoluteColumnIndex). XDAT columns follow the standard columns.
        // Standard LDE has ~12 visible columns (Type, Comment, Radius, Thickness, Material,
        // Coating, SemiDiameter, ChipZone, MechSemiDia, Conic, TCE, + Par1..Par14).
        // Absolute XDAT column index = standardColumnCount + N. Try N+14 first (after Par14),
        // then a few common alternates; the first non-null wins.
        foreach (int offset in new[] { 14, 26, 28, 30 })
        {
            try
            {
                dynamic dynSurface = surface;
                var cell = dynSurface.GetCellAt(offset + cellNumber);
                if (cell != null)
                {
                    pathUsed = AccessPath.GetCellAtAbsolute;
                    return cell;
                }
            }
            catch { /* method missing or column out of range; try next offset */ }
        }

        // Path 3a: surface.ExtraDataCell(N) — direct method
        try
        {
            dynamic dynSurface = surface;
            var cell = dynSurface.ExtraDataCell(cellNumber);
            if (cell != null)
            {
                pathUsed = AccessPath.ExtraDataGetCell;
                return cell;
            }
        }
        catch { /* method missing */ }

        // Path 3b: surface.ExtraData.GetCell(N) — property then method
        try
        {
            dynamic dynSurface = surface;
            var xd = dynSurface.ExtraData;
            if (xd != null)
            {
                var cell = xd.GetCell(cellNumber);
                if (cell != null)
                {
                    pathUsed = AccessPath.ExtraDataGetCell;
                    return cell;
                }
            }
        }
        catch { /* property missing */ }

        // Path 3c: surface.GetExtraData(N)
        try
        {
            dynamic dynSurface = surface;
            var cell = dynSurface.GetExtraData(cellNumber);
            if (cell != null)
            {
                pathUsed = AccessPath.ExtraDataGetCell;
                return cell;
            }
        }
        catch { /* method missing */ }

        return null;
    }

    /// Reads the numeric value of a cell, falling back to IntegerValue on failure.
    public static double ReadValue(dynamic cell)
    {
        try { return (double)cell.DoubleValue; }
        catch { return (double)(int)cell.IntegerValue; }
    }

    /// Writes a numeric value, falling back to IntegerValue if DoubleValue rejects.
    public static void WriteValue(dynamic cell, double value)
    {
        try { cell.DoubleValue = value; }
        catch { cell.IntegerValue = (int)value; }
    }

    /// Queries whether a cell has a Variable solve attached.
    public static bool IsVariable(dynamic cell)
    {
        try
        {
            var solveData = cell.GetSolveData();
            if (solveData == null) return false;
            string typeName = solveData.Type.ToString();
            return string.Equals(typeName, "Variable", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
