using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ZemaxMCP.Server.Prompts;

[McpServerPromptType]
public class OptimizationPrompts
{
    [McpServerPrompt(Name = "optimize_mtf")]
    [Description("Guide for optimizing MTF at specific spatial frequencies")]
    public string OptimizeMtf(
        [Description("Target spatial frequency in lp/mm")] double frequency,
        [Description("Target MTF value (0-1)")] double targetMtf = 0.5)
    {
        return $"""
            ## MTF Optimization Guide

            Target: MTF >= {targetMtf} at {frequency} lp/mm

            ### Understanding MTF Operands

            | Operand | Description |
            |---------|-------------|
            | MTFT | Tangential MTF |
            | MTFS | Sagittal MTF |
            | MTFA | Average of T and S |
            | MTFN | Minimum of T and S |
            | MTFX | Maximum of T and S |

            ### Adding MTF Operands

            Use `zemax_add_operand` with these parameters:

            For tangential MTF:
            ```
            operandType="MTFT", int1=3 (sampling), int2=0 (wave), data1=FIELD, data2={frequency}
            target={targetMtf}, weight=1
            ```

            For sagittal MTF:
            ```
            operandType="MTFS", int1=3 (sampling), int2=0 (wave), data1=FIELD, data2={frequency}
            target={targetMtf}, weight=1
            ```

            ### Tips

            1. Start with lower sampling (1-2) for faster iteration
            2. Increase sampling (4-6) for final optimization
            3. Weight edge fields higher if uniformity is important
            4. Consider adding MTFT/MTFS at multiple frequencies
            5. Use MTFN to guarantee minimum performance

            ### Common Issues

            - **MTF stuck at zero**: Check if ray tracing works for all fields
            - **T/S imbalance**: May indicate astigmatism - add ASTI operand
            - **Low at edges**: Field curvature - consider curved image or field flattener
            """;
    }

    [McpServerPrompt(Name = "optimize_distortion")]
    [Description("Guide for minimizing distortion")]
    public string OptimizeDistortion(
        [Description("Maximum acceptable distortion percentage")] double maxDistortion = 1.0)
    {
        return $"""
            ## Distortion Optimization Guide

            Target: Distortion < {maxDistortion}%

            ### Distortion Operands

            | Operand | Description |
            |---------|-------------|
            | DIST | Seidel distortion (surface or system) |
            | DISG | General distortion (any ray, any field) |
            | DIMX | Maximum distortion |

            ### Using DIST (Surf=0 for system)

            Use `zemax_add_operand`:
            ```
            operandType="DIST", int1=0 (system), int2=1 (wave)
            target=0, weight=1
            ```

            ### Using DIMX for Maximum Control

            DIMX constrains the absolute maximum distortion:
            ```
            operandType="DIMX", int1=1 (wave), int2=0 (field=max)
            target={maxDistortion}, weight=1
            ```

            ### Tips

            1. Distortion is typically controlled by lens symmetry
            2. A symmetric doublet naturally has low distortion
            3. For asymmetric systems, use DIST at multiple fields
            4. Consider using ABCD distortion model for complex systems
            """;
    }

    [McpServerPrompt(Name = "optimize_spot_size")]
    [Description("Guide for optimizing RMS spot size")]
    public string OptimizeSpotSize(
        [Description("Target RMS spot size in um")] double targetSpotSize = 10)
    {
        return $"""
            ## Spot Size Optimization Guide

            Target: RMS spot size <= {targetSpotSize} um

            ### Spot Size Operands

            | Operand | Method | Reference |
            |---------|--------|-----------|
            | RSCE | Gaussian quadrature | Centroid |
            | RSCH | Gaussian quadrature | Chief ray |
            | RSRE | Rectangular grid | Centroid |
            | RSRH | Rectangular grid | Chief ray |

            ### Recommended: RSCE (Most common)

            Use `zemax_add_operand`:
            ```
            operandType="RSCE"
            int1=4 (rings)
            int2=0 (polychromatic)
            data1=Hx, data2=Hy (field coordinates)
            target=0, weight=1
            ```

            ### Adding for Multiple Fields

            For a typical imaging system, add RSCE at:
            - On-axis: Hx=0, Hy=0
            - 0.7 field: Hx=0, Hy=0.7
            - Full field: Hx=0, Hy=1.0

            ### Tips

            1. Centroid reference (RSCE) is usually preferred
            2. Higher ring count = more accurate but slower
            3. Weight higher fields more if edge performance matters
            4. Consider using OPDC/OPDX for diffraction-limited systems
            """;
    }
}
