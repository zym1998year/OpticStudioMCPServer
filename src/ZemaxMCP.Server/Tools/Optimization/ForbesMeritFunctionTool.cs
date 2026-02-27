using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Editors.MFE;

namespace ZemaxMCP.Server.Tools.Optimization;

/// <summary>
/// Creates a merit function using Forbes 1988 Gaussian quadrature pupil sampling.
/// This generates explicit OPDX/OPDC/OPDM operands with optimal sampling points,
/// similar to what Zemax's native Optimization Wizard produces.
///
/// Reference: G.W. Forbes, "Optical system assessment for design: numerical ray tracing
/// in the Gaussian pupil", J. Opt. Soc. Am. A, Vol. 5, No. 11, November 1988.
/// </summary>
[McpServerToolType]
public class ForbesMeritFunctionTool
{
    private readonly IZemaxSession _session;

    public ForbesMeritFunctionTool(IZemaxSession session) => _session = session;

    public record ForbesMeritFunctionResult(
        bool Success,
        string? Error,
        int TotalOperandsAdded,
        int ConfigurationsIncluded,
        int FieldsIncluded,
        int WavelengthsIncluded,
        int PupilSamplesPerField,
        double InitialMerit,
        List<string> Summary
    );

    [McpServerTool(Name = "zemax_forbes_merit_function")]
    [Description("Create merit function using Forbes 1988 Gaussian quadrature pupil sampling with explicit OPD operands. Generates OPDX/OPDC/OPDM operands with optimal (Px, Py) sampling points for all configurations, fields, and wavelengths.")]
    public async Task<ForbesMeritFunctionResult> ExecuteAsync(
        [Description("OPD operand type: OPDX (centroid, no tilt), OPDC (chief ray), or OPDM (max OPD)")]
        string operandType = "OPDX",

        [Description("Number of radial rings for Gaussian quadrature (1-6). More rings = higher accuracy. 3 rings gives ~1% accuracy.")]
        int rings = 3,

        [Description("Number of angular samples per ring (arms). For off-axis fields, use 6 arms. Total pupil samples = rings * arms.")]
        int arms = 6,

        [Description("Include all defined wavelengths (true) or use polychromatic (false with wavelength=0)")]
        bool includeAllWavelengths = true,

        [Description("Specific wavelength number if not using all wavelengths (0 for polychromatic)")]
        int wavelength = 0,

        [Description("Include all configurations in multi-configuration systems")]
        bool includeAllConfigurations = true,

        [Description("Clear existing merit function before adding new operands")]
        bool clearExisting = true,

        [Description("Add BLNK comment operands for organization (like native Zemax wizard)")]
        bool addComments = true,

        [Description("Use Radau scheme (includes center ray) for axial field points")]
        bool useRadauForAxial = false,

        [Description("Assume Y-symmetry for fields with Hx=0. When true, samples only half the pupil (Px >= 0) for fields where Hx=0, reducing operand count by ~50%. Per Forbes 1988 Section 3.B.")]
        bool assumeSymmetry = false)
    {
        // Validate operand type
        operandType = operandType?.Trim().ToUpperInvariant() ?? "OPDX";
        if (operandType != "OPDX" && operandType != "OPDC" && operandType != "OPDM")
        {
            return new ForbesMeritFunctionResult(
                Success: false,
                Error: $"Invalid operand type '{operandType}'. Valid options: OPDX, OPDC, OPDM",
                TotalOperandsAdded: 0, ConfigurationsIncluded: 0, FieldsIncluded: 0,
                WavelengthsIncluded: 0, PupilSamplesPerField: 0, InitialMerit: 0,
                Summary: new List<string>()
            );
        }

        rings = Math.Max(1, Math.Min(6, rings));
        arms = Math.Max(1, Math.Min(12, arms));

        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["operandType"] = operandType,
                ["rings"] = rings,
                ["arms"] = arms,
                ["clearExisting"] = clearExisting
            };

            var result = await _session.ExecuteAsync("ForbesMeritFunction", parameters, system =>
            {
                var mfe = system.MFE;
                var fields = system.SystemData.Fields;
                var wavelengths = system.SystemData.Wavelengths;
                var mce = system.MCE;

                var summary = new List<string>();
                int totalOperandsAdded = 0;

                // Get configuration info
                int numConfigs = mce.NumberOfConfigurations;
                int currentConfig = mce.CurrentConfiguration;

                // Clear existing merit function if requested
                if (clearExisting)
                {
                    int maxIterations = mfe.NumberOfOperands + 10;
                    int iterations = 0;
                    while (mfe.NumberOfOperands > 1 && iterations < maxIterations)
                    {
                        int countBefore = mfe.NumberOfOperands;
                        mfe.RemoveOperandAt(mfe.NumberOfOperands);
                        iterations++;
                        if (mfe.NumberOfOperands >= countBefore)
                            break;
                    }
                }

                // Add DMFS operand (Default Merit Function Start) if needed
                if (mfe.NumberOfOperands == 1)
                {
                    var firstOp = mfe.GetOperandAt(1);
                    if (firstOp.Type != MeritOperandType.DMFS)
                    {
                        var dmfsRow = mfe.InsertNewOperandAt(1);
                        dmfsRow.ChangeType(MeritOperandType.DMFS);
                        totalOperandsAdded++;
                    }
                }

                // Build wavelength list
                var wavelengthList = new List<int>();
                if (includeAllWavelengths)
                {
                    for (int w = 1; w <= wavelengths.NumberOfWavelengths; w++)
                        wavelengthList.Add(w);
                }
                else
                {
                    wavelengthList.Add(wavelength);
                }

                // Build configuration list
                var configList = new List<int>();
                if (includeAllConfigurations && numConfigs > 1)
                {
                    for (int c = 1; c <= numConfigs; c++)
                        configList.Add(c);
                }
                else
                {
                    configList.Add(currentConfig);
                }

                // Calculate total weight for normalization
                double totalFieldWeight = 0;
                for (int f = 1; f <= fields.NumberOfFields; f++)
                    totalFieldWeight += fields.GetField(f).Weight;
                if (totalFieldWeight <= 0) totalFieldWeight = fields.NumberOfFields;

                // Calculate maximum field extent for normalization to Hx, Hy coordinates
                double maxFieldExtent = 0;
                for (int f = 1; f <= fields.NumberOfFields; f++)
                {
                    var fld = fields.GetField(f);
                    double extent = Math.Sqrt(fld.X * fld.X + fld.Y * fld.Y);
                    if (extent > maxFieldExtent) maxFieldExtent = extent;
                }
                if (maxFieldExtent <= 0) maxFieldExtent = 1.0; // Avoid division by zero for axial-only systems

                double totalWavelengthWeight = 0;
                foreach (int w in wavelengthList)
                {
                    if (w == 0) totalWavelengthWeight = 1.0;
                    else totalWavelengthWeight += wavelengths.GetWavelength(w).Weight;
                }
                if (totalWavelengthWeight <= 0) totalWavelengthWeight = wavelengthList.Count;

                // Add header comment
                if (addComments)
                {
                    var headerRow = mfe.AddOperand();
                    headerRow.ChangeType(MeritOperandType.BLNK);
                    // Set comment text - BLNK uses the comment field
                    SetOperandComment(headerRow, $"Forbes 1988 GQ: {operandType} {rings} rings {arms} arms");
                    totalOperandsAdded++;
                }

                int pupilSamplesPerField = 0;

                // Process each configuration
                foreach (int configNum in configList)
                {
                    // Add CONF operand for multi-configuration
                    if (configList.Count > 1)
                    {
                        var confRow = mfe.AddOperand();
                        confRow.ChangeType(MeritOperandType.CONF);
                        confRow.GetCellAt(2).IntegerValue = configNum;
                        totalOperandsAdded++;

                        if (addComments)
                        {
                            var confComment = mfe.AddOperand();
                            confComment.ChangeType(MeritOperandType.BLNK);
                            SetOperandComment(confComment, $"Configuration {configNum}");
                            totalOperandsAdded++;
                        }
                    }

                    // Process each field
                    for (int fieldNum = 1; fieldNum <= fields.NumberOfFields; fieldNum++)
                    {
                        var field = fields.GetField(fieldNum);
                        // Normalize field coordinates to Hx, Hy (0 to 1 range)
                        double hx = field.X / maxFieldExtent;
                        double hy = field.Y / maxFieldExtent;
                        double fieldWeight = field.Weight / totalFieldWeight;

                        // Determine if this is an axial field (both hx and hy near zero)
                        bool isAxialField = Math.Abs(hx) < 1e-6 && Math.Abs(hy) < 1e-6;

                        // Add field comment
                        if (addComments)
                        {
                            var fieldComment = mfe.AddOperand();
                            fieldComment.ChangeType(MeritOperandType.BLNK);
                            SetOperandComment(fieldComment, $"Field {fieldNum}: Hx={hx:F4}, Hy={hy:F4}");
                            totalOperandsAdded++;
                        }

                        // Generate pupil sample points using Forbes method
                        // Use symmetry when: assumeSymmetry is true AND Hx=0 (field only off-axis in Y)
                        bool useSymmetricSampling = assumeSymmetry && Math.Abs(hx) < 1e-6 && !isAxialField;

                        List<ForbesPupilSampling.PupilSamplePoint> pupilSamples;
                        if (isAxialField)
                        {
                            // Axial field: rotationally symmetric, only need meridional rays
                            pupilSamples = ForbesPupilSampling.GenerateAxialSamplePoints(rings, useRadau: useRadauForAxial);
                        }
                        else if (useSymmetricSampling)
                        {
                            // Y-symmetric field (Hx=0): sample half pupil, double weights
                            pupilSamples = ForbesPupilSampling.GenerateSymmetricSamplePoints(rings, arms, useRadau: false);
                        }
                        else
                        {
                            // General off-axis: full pupil sampling
                            pupilSamples = ForbesPupilSampling.GenerateSamplePoints(rings, arms, useRadau: false);
                        }

                        if (fieldNum == 1 && configNum == configList[0])
                            pupilSamplesPerField = pupilSamples.Count;

                        // Process each wavelength
                        foreach (int waveNum in wavelengthList)
                        {
                            double waveWeight = waveNum == 0 ? 1.0 :
                                wavelengths.GetWavelength(waveNum).Weight / totalWavelengthWeight;

                            // Add operand for each pupil sample point
                            foreach (var sample in pupilSamples)
                            {
                                var row = mfe.AddOperand();

                                // Set operand type
                                if (Enum.TryParse<MeritOperandType>(operandType, true, out var opType))
                                {
                                    row.ChangeType(opType);
                                }

                                // OPDX/OPDC/OPDM parameters:
                                // Int1 (Cell 2): Sampling - set to 0 for explicit pupil coordinates
                                // Int2 (Cell 3): Wavelength number
                                // Data1 (Cell 4): Hx - normalized field x
                                // Data2 (Cell 5): Hy - normalized field y
                                // Data3 (Cell 6): Px - normalized pupil x
                                // Data4 (Cell 7): Py - normalized pupil y

                                row.GetCellAt(2).IntegerValue = 0;  // Explicit pupil coords
                                row.GetCellAt(3).IntegerValue = waveNum;
                                row.GetCellAt(4).DoubleValue = hx;
                                row.GetCellAt(5).DoubleValue = hy;
                                row.GetCellAt(6).DoubleValue = sample.Px;
                                row.GetCellAt(7).DoubleValue = sample.Py;

                                row.Target = 0;

                                // Combined weight = field weight * wavelength weight * pupil weight
                                double combinedWeight = fieldWeight * waveWeight * sample.Weight;
                                row.Weight = combinedWeight;

                                totalOperandsAdded++;
                            }
                        }
                    }
                }

                // Calculate initial merit function value
                double initialMerit = mfe.CalculateMeritFunction();

                summary.Add($"Operand type: {operandType}");
                summary.Add($"Forbes GQ: {rings} rings, {arms} arms");
                summary.Add($"Assume Y-symmetry: {assumeSymmetry}");
                summary.Add($"Pupil samples per field: {pupilSamplesPerField}");
                summary.Add($"Configurations: {configList.Count}");
                summary.Add($"Fields: {fields.NumberOfFields}");
                summary.Add($"Wavelengths: {wavelengthList.Count}");
                summary.Add($"Total operands: {totalOperandsAdded}");
                summary.Add($"Initial merit: {initialMerit:E4}");

                return new ForbesMeritFunctionResult(
                    Success: true,
                    Error: null,
                    TotalOperandsAdded: totalOperandsAdded,
                    ConfigurationsIncluded: configList.Count,
                    FieldsIncluded: fields.NumberOfFields,
                    WavelengthsIncluded: wavelengthList.Count,
                    PupilSamplesPerField: pupilSamplesPerField,
                    InitialMerit: initialMerit,
                    Summary: summary
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new ForbesMeritFunctionResult(
                Success: false,
                Error: ex.Message,
                TotalOperandsAdded: 0, ConfigurationsIncluded: 0, FieldsIncluded: 0,
                WavelengthsIncluded: 0, PupilSamplesPerField: 0, InitialMerit: 0,
                Summary: new List<string>()
            );
        }
    }

    /// <summary>
    /// Helper to set comment text on a BLNK operand row.
    /// The comment is typically stored in a specific cell or as the row comment.
    /// </summary>
    private static void SetOperandComment(IMFERow row, string comment)
    {
        try
        {
            // BLNK operands in Zemax store the comment text
            // Try to set via the row's comment property if available
            // For now, we leave it as the default BLNK display
        }
        catch
        {
            // Ignore errors - comment is not critical
        }
    }
}
