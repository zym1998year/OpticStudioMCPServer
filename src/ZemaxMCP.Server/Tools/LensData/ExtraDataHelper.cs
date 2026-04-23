using ZOSAPI.Editors;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Server.Tools.LensData;

/// Encapsulates the three ZOSAPI access paths for XDAT (Extra Data Editor) cells.
/// ZOSAPI exposes XDAT differently across versions; the helper tries each path
/// in priority order and returns the first IEditorCell that responds.
internal static class ExtraDataHelper
{
    public enum AccessPath { Unknown, ZernikeTypedInterface, SurfaceColumnExtraData, GetCellAtAbsolute, ExtraDataGetCell }

    /// High-priority path for Zernike-based surfaces. Uses the strongly-typed
    /// <c>ISurfaceNthZernike</c> interface (and its Phase-variant descendant
    /// <c>ISurfaceZernikeStandardPhase</c>) to expose cells with the same semantic
    /// numbering the Zemax UI uses in the Extra Data column.
    ///
    /// <para>Cell numbering for Phase variants (ZernikeStandardPhase, ZernikeFringePhase,
    /// ZernikeAnnularPhase):</para>
    /// <list type="bullet">
    /// <item>1 -> Extrapolate (Int32, 0=off/1=on)</item>
    /// <item>2 -> DiffractOrder (Double)</item>
    /// <item>3 -> NumberOfTerms (Int32; aka MaxTerm)</item>
    /// <item>4 -> NormRadius (Double)</item>
    /// <item>5+(n-1) -> Zn (nth Zernike coefficient, Double)</item>
    /// </list>
    ///
    /// <para>Cell numbering for Sag variants (ZernikeStandardSag, ZernikeFringeSag,
    /// ZernikeAnnularSag) which lack Extrapolate/DiffractOrder:</para>
    /// <list type="bullet">
    /// <item>1 -> NumberOfTerms</item>
    /// <item>2 -> NormRadius</item>
    /// <item>3+(n-1) -> Zn</item>
    /// </list>
    ///
    /// Returns null if the surface is not a Zernike variant (caller should fall back
    /// to the column-based <see cref="TryGetCell"/> probe).
    public static dynamic? TryGetZernikeCell(ILDERow surface, int cellNumber, out AccessPath pathUsed)
    {
        pathUsed = AccessPath.Unknown;
        if (cellNumber < 1) return null;

        // Try the SurfaceData property first (preferred ZOSAPI convention for typed
        // surface interfaces). Fall back to casting the ILDERow itself.
        dynamic? zernikeBase = null;
        try
        {
            dynamic sd = surface.SurfaceData;
            if (sd != null) zernikeBase = sd;
        }
        catch { /* SurfaceData missing or non-Zernike */ }

        if (zernikeBase == null)
        {
            try { dynamic s = surface; zernikeBase = s; }
            catch { /* ILDERow not castable; fall through */ }
        }

        if (zernikeBase == null) return null;

        // Probe a safe property to confirm this is actually a Zernike-family surface.
        // ISurfaceNthZernike exposes NumberOfTerms on every variant.
        int numTerms;
        try { numTerms = (int)zernikeBase.NumberOfTerms; }
        catch { return null; /* not a Zernike surface — fall back to column probe */ }

        pathUsed = AccessPath.ZernikeTypedInterface;

        // Detect Phase variant (has Extrapolate/DiffractOrder). We probe the property
        // getter and treat any failure as "not a Phase variant" (i.e. a Sag variant).
        bool isPhaseVariant = false;
        try
        {
            var _ = zernikeBase.Extrapolate;
            isPhaseVariant = true;
        }
        catch { /* Sag variant — no Extrapolate property */ }

        if (isPhaseVariant)
        {
            switch (cellNumber)
            {
                case 1:
                    try { return zernikeBase.ExtrapolateCell; } catch { return null; }
                case 2:
                    try { return zernikeBase.DiffractOrderCell; } catch { return null; }
                case 3:
                    try { return zernikeBase.NumberOfTermsCell; } catch { return null; }
                case 4:
                    try { return zernikeBase.NormRadiusCell; } catch { return null; }
                default:
                    int coeffIdx = cellNumber - 4;  // cell 5 -> Z1
                    if (coeffIdx < 1 || coeffIdx > numTerms) return null;
                    try { return zernikeBase.NthZernikeCoefficientCell(coeffIdx); }
                    catch { return null; }
            }
        }
        else
        {
            // Sag variants: no Extrapolate/DiffractOrder — cells shift up by 2.
            switch (cellNumber)
            {
                case 1:
                    try { return zernikeBase.NumberOfTermsCell; } catch { return null; }
                case 2:
                    try { return zernikeBase.NormRadiusCell; } catch { return null; }
                default:
                    int coeffIdx = cellNumber - 2;  // cell 3 -> Z1
                    if (coeffIdx < 1 || coeffIdx > numTerms) return null;
                    try { return zernikeBase.NthZernikeCoefficientCell(coeffIdx); }
                    catch { return null; }
            }
        }
    }

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
