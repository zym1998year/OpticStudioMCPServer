using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Editors.MFE;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class OptimizationWizardTool
{
    private readonly IZemaxSession _session;

    public OptimizationWizardTool(IZemaxSession session) => _session = session;

    public record OptimizationWizardResult(
        bool Success,
        string? Error,
        int OperandsAdded,
        int FieldsIncluded,
        int ConstraintsAdded,
        string Criterion,
        string Reference,
        double InitialMerit,
        List<string> OperandsSummary
    );

    [McpServerTool(Name = "zemax_optimization_wizard")]
    [Description("Automatically construct a merit function based on optimization criteria (similar to Zemax Optimization Wizard)")]
    public async Task<OptimizationWizardResult> ExecuteAsync(
        [Description("Optimization criterion: RMSSpotRadius, RMSSpotRadiusX, RMSSpotRadiusY, RMSWavefront, PeakToValley")]
        string criterion = "RMSSpotRadius",

        [Description("Reference point: Centroid or ChiefRay")]
        string reference = "Centroid",

        [Description("Pupil integration method: GaussianQuadrature or RectangularArray")]
        string pupilIntegration = "GaussianQuadrature",

        [Description("Rings for Gaussian quadrature (1-6)")]
        int rings = 3,

        [Description("Grid size for rectangular array")]
        int gridSize = 6,

        [Description("Arms for Gaussian quadrature")]
        int arms = 6,

        [Description("Add operands for all defined fields")]
        bool includeAllFields = true,

        [Description("Wavelength number (0 for polychromatic)")]
        int wavelength = 0,

        [Description("Add glass boundary constraints (MNCT, MXCT, MNET)")]
        bool addBoundaryConstraints = false,

        [Description("Minimum center thickness for glass (mm)")]
        double minCenterThickness = 1.0,

        [Description("Maximum center thickness for glass (mm)")]
        double maxCenterThickness = 50.0,

        [Description("Minimum edge thickness for air gaps (mm)")]
        double minEdgeThickness = 0.5,

        [Description("Clear existing merit function before adding")]
        bool clearExisting = true)
    {
        // Validate and normalize inputs
        criterion = criterion?.Trim() ?? "RMSSpotRadius";
        reference = reference?.Trim() ?? "Centroid";
        pupilIntegration = pupilIntegration?.Trim() ?? "GaussianQuadrature";

        rings = Math.Max(1, Math.Min(6, rings));
        gridSize = Math.Max(1, Math.Min(32, gridSize));
        arms = Math.Max(1, Math.Min(12, arms));

        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["criterion"] = criterion,
                ["reference"] = reference,
                ["pupilIntegration"] = pupilIntegration,
                ["rings"] = rings,
                ["clearExisting"] = clearExisting
            };

            var result = await _session.ExecuteAsync("OptimizationWizard", parameters, system =>
            {
                if (system == null)
                {
                    throw new InvalidOperationException("Optical system is not available");
                }

                var mfe = system.MFE;
                if (mfe == null)
                {
                    throw new InvalidOperationException("Merit Function Editor is not available");
                }

                var operandsSummary = new List<string>();

                // Use the ZOSAPI SEQOptimizationWizard as per ZEMAX documentation
                var optWizard = mfe.SEQOptimizationWizard;
                if (optWizard == null)
                {
                    throw new InvalidOperationException("SEQ Optimization Wizard is not available");
                }

                // Map criterion to Data index
                // The Data property uses integer indices. Common mappings:
                // 0 = RMS (Wavefront), 1 = RMS Spot Radius, 2 = RMS Spot X, 3 = RMS Spot Y
                // 4 = Wavefront PTV, etc.
                // We need to find the correct index by iterating GetDataTypeAt()
                int dataIndex = FindDataTypeIndex(optWizard, criterion);
                optWizard.Data = dataIndex;

                // Map reference to Reference index
                // 0 = Centroid, 1 = Chief Ray typically
                int referenceIndex = FindReferenceIndex(optWizard, reference);
                optWizard.Reference = referenceIndex;

                // Map pupil integration method
                // 0 = Gaussian Quadrature, 1 = Rectangular Array typically
                int pupilMethodIndex = pupilIntegration.Equals("GaussianQuadrature", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                optWizard.PupilIntegrationMethod = pupilMethodIndex;

                // Set Ring index - ZEMAX uses 0-based index where index = rings - 1
                // According to docs: Ring = 2 means 3 rings
                int ringIndex = rings - 1;
                if (ringIndex >= 0 && ringIndex < optWizard.NumberOfRings)
                {
                    optWizard.Ring = ringIndex;
                }

                // Set Grid for rectangular array
                int gridIndex = FindGridIndex(optWizard, gridSize);
                if (gridIndex >= 0)
                {
                    optWizard.Grid = gridIndex;
                }

                // Set Arms
                int armIndex = FindArmIndex(optWizard, arms);
                if (armIndex >= 0)
                {
                    optWizard.Arm = armIndex;
                }

                // Set overall weight
                optWizard.OverallWeight = 1.0;

                // Set boundary constraints if requested
                if (addBoundaryConstraints)
                {
                    optWizard.IsGlassUsed = true;
                    optWizard.GlassMin = minCenterThickness;
                    optWizard.GlassMax = maxCenterThickness;
                    optWizard.GlassEdge = minEdgeThickness;

                    optWizard.IsAirUsed = true;
                    optWizard.AirMin = minEdgeThickness;
                    optWizard.AirMax = 1000.0;
                    optWizard.AirEdge = minEdgeThickness;
                }
                else
                {
                    optWizard.IsGlassUsed = false;
                    optWizard.IsAirUsed = false;
                }

                // Apply the wizard - this creates the merit function
                optWizard.CommonSettings.Apply();

                // Get the resulting merit function info
                int operandsAdded = mfe.NumberOfOperands;
                double initialMerit = mfe.CalculateMeritFunction();

                // Build summary of what was configured
                operandsSummary.Add($"Criterion: {GetDataTypeName(optWizard, dataIndex)} (index {dataIndex})");
                operandsSummary.Add($"Reference: {GetReferenceName(optWizard, referenceIndex)} (index {referenceIndex})");
                operandsSummary.Add($"Pupil Integration: {GetPupilMethodName(optWizard, pupilMethodIndex)}");
                operandsSummary.Add($"Rings: {rings} (index {ringIndex})");
                if (addBoundaryConstraints)
                {
                    operandsSummary.Add($"Glass constraints: Min={minCenterThickness}mm, Max={maxCenterThickness}mm");
                    operandsSummary.Add($"Air constraints: Edge={minEdgeThickness}mm");
                }
                operandsSummary.Add($"Total operands: {operandsAdded}");

                // Count fields
                int numFields = 0;
                var fields = system.SystemData?.Fields;
                if (fields != null)
                {
                    numFields = fields.NumberOfFields;
                }

                // Estimate constraints added (if boundary constraints enabled)
                int constraintsAdded = 0;
                if (addBoundaryConstraints)
                {
                    var lde = system.LDE;
                    if (lde != null)
                    {
                        for (int i = 1; i < lde.NumberOfSurfaces - 1; i++)
                        {
                            var surf = lde.GetSurfaceAt(i);
                            if (surf != null && !string.IsNullOrWhiteSpace(surf.Material))
                            {
                                constraintsAdded += 2; // MNCT + MXCT
                            }
                        }
                    }
                }

                return new OptimizationWizardResult(
                    Success: true,
                    Error: null,
                    OperandsAdded: operandsAdded,
                    FieldsIncluded: numFields,
                    ConstraintsAdded: constraintsAdded,
                    Criterion: criterion,
                    Reference: reference,
                    InitialMerit: initialMerit,
                    OperandsSummary: operandsSummary
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new OptimizationWizardResult(
                Success: false,
                Error: ex.Message,
                OperandsAdded: 0, FieldsIncluded: 0, ConstraintsAdded: 0,
                Criterion: criterion, Reference: reference, InitialMerit: 0,
                OperandsSummary: new List<string>()
            );
        }
    }

    /// <summary>
    /// Find the index for a given criterion name by searching GetDataTypeAt()
    /// </summary>
    private static int FindDataTypeIndex(ZOSAPI.Wizards.ISEQOptimizationWizard wizard, string criterion)
    {
        // Default mappings based on common ZEMAX configurations
        // These are typical index values:
        string normalizedCriterion = criterion.ToUpperInvariant().Replace(" ", "");

        // Try to find by iterating through available types
        for (int i = 0; i < wizard.NumberOfDataTypes; i++)
        {
            string typeName = wizard.GetDataTypeAt(i) ?? "";
            string normalizedType = typeName.ToUpperInvariant().Replace(" ", "");

            if (normalizedType.Contains(normalizedCriterion) ||
                normalizedCriterion.Contains(normalizedType))
            {
                return i;
            }

            // Check common patterns
            if (normalizedCriterion.Contains("SPOTRADIUS") && normalizedType.Contains("SPOT") && normalizedType.Contains("RADIUS"))
                return i;
            if (normalizedCriterion.Contains("WAVEFRONT") && normalizedType.Contains("WAVEFRONT"))
                return i;
            if (normalizedCriterion.Contains("PEAKTOVALLEY") && (normalizedType.Contains("PTV") || normalizedType.Contains("PEAK")))
                return i;
        }

        // Default to 1 (RMS Spot Radius) based on ZEMAX example
        return 1;
    }

    /// <summary>
    /// Find the index for a given reference type
    /// </summary>
    private static int FindReferenceIndex(ZOSAPI.Wizards.ISEQOptimizationWizard wizard, string reference)
    {
        for (int i = 0; i < wizard.NumberOfReferences; i++)
        {
            string refName = wizard.GetReferenceAt(i) ?? "";
            if (refName.IndexOf(reference, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return i;
            }
        }
        // Default: 0 = Centroid, 1 = Chief Ray
        return reference.Equals("ChiefRay", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    /// <summary>
    /// Find the index for a given grid size
    /// </summary>
    private static int FindGridIndex(ZOSAPI.Wizards.ISEQOptimizationWizard wizard, int gridSize)
    {
        for (int i = 0; i < wizard.NumberOfGrids; i++)
        {
            string gridName = wizard.GetGridAt(i) ?? "";
            if (gridName.Contains(gridSize.ToString()))
            {
                return i;
            }
        }
        return 2; // Default to a reasonable grid
    }

    /// <summary>
    /// Find the index for a given arm count
    /// </summary>
    private static int FindArmIndex(ZOSAPI.Wizards.ISEQOptimizationWizard wizard, int arms)
    {
        for (int i = 0; i < wizard.NumberOfArms; i++)
        {
            string armName = wizard.GetArmAt(i) ?? "";
            if (armName.Contains(arms.ToString()))
            {
                return i;
            }
        }
        return arms - 1; // Default based on arm count
    }

    private static string GetDataTypeName(ZOSAPI.Wizards.ISEQOptimizationWizard wizard, int index)
    {
        try { return wizard.GetDataTypeAt(index) ?? $"Type {index}"; }
        catch { return $"Type {index}"; }
    }

    private static string GetReferenceName(ZOSAPI.Wizards.ISEQOptimizationWizard wizard, int index)
    {
        try { return wizard.GetReferenceAt(index) ?? $"Reference {index}"; }
        catch { return $"Reference {index}"; }
    }

    private static string GetPupilMethodName(ZOSAPI.Wizards.ISEQOptimizationWizard wizard, int index)
    {
        try { return wizard.GetPupilIntegrationMethodAt(index) ?? $"Method {index}"; }
        catch { return $"Method {index}"; }
    }
}
