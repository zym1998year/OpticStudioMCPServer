using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ZemaxMCP.Server.Prompts;

[McpServerPromptType]
public class AnalysisPrompts
{
    [McpServerPrompt(Name = "analyze_system")]
    [Description("Guide for comprehensive system analysis")]
    public string AnalyzeSystem()
    {
        return """
            ## Comprehensive System Analysis Guide

            ### Step 1: Get System Overview

            Use `zemax_get_system` to retrieve:
            - Surface data (radii, thicknesses, materials)
            - Field points
            - Wavelengths
            - Aperture settings

            ### Step 2: Cardinal Points

            Use `zemax_cardinal_points` to get:
            - Effective focal length
            - Entrance/exit pupil positions and diameters
            - Principal planes
            - Magnification

            ### Step 3: Spot Diagram Analysis

            Use `zemax_spot_diagram` for each field:
            - RMS and geometric spot sizes
            - Centroid positions
            - Airy disk comparison

            ### Step 4: MTF Analysis

            Use `zemax_fft_mtf` at key frequencies:
            - Nyquist frequency (if detector specified)
            - 50%, 25% of Nyquist
            - Compare tangential vs sagittal

            ### Step 5: Ray Trace Verification

            Use `zemax_ray_trace` to verify:
            - Marginal ray paths
            - Chief ray paths
            - Vignetting

            ### Step 6: Merit Function Review

            Use `zemax_get_merit_function` to:
            - Review all operands and values
            - Identify largest contributors to merit
            - Check constraint satisfaction

            ### Interpretation Tips

            - RMS spot < Airy radius = diffraction limited
            - MTF at Nyquist > 0.1 = acceptable for imaging
            - Large T/S difference = astigmatism present
            - Merit function contributors show design weaknesses
            """;
    }

    [McpServerPrompt(Name = "troubleshoot_design")]
    [Description("Guide for troubleshooting optical design issues")]
    public string TroubleshootDesign()
    {
        return """
            ## Design Troubleshooting Guide

            ### Common Issues and Solutions

            #### 1. High Merit Function Value

            **Symptoms:** Merit function stuck at high value
            **Diagnosis:**
            - Use `zemax_get_merit_function` to identify largest contributors
            - Check if constraints are too tight

            **Solutions:**
            - Relax constraints temporarily
            - Add more variables (surfaces, glass)
            - Try different starting point

            #### 2. Ray Trace Failures

            **Symptoms:** "TIR" or "Ray missed surface" errors
            **Diagnosis:**
            - Use `zemax_ray_trace` with edge rays
            - Check semi-diameters

            **Solutions:**
            - Increase surface semi-diameters
            - Add vignetting factors
            - Reduce field angles

            #### 3. Poor Edge Field Performance

            **Symptoms:** Good on-axis, poor off-axis
            **Diagnosis:**
            - Compare `zemax_spot_diagram` at different fields
            - Check `zemax_fft_mtf` at edge fields

            **Solutions:**
            - Add astigmatism control (ASTI operand)
            - Add field curvature control (FCUR operand)
            - Consider field flattener element

            #### 4. Chromatic Aberration

            **Symptoms:** Different wavelengths focus at different points
            **Diagnosis:**
            - Use `zemax_cardinal_points` at different wavelengths
            - Check AXCL and LACL values

            **Solutions:**
            - Add achromatic doublet
            - Use ED or fluorite glasses
            - Add secondary spectrum control

            #### 5. Manufacturing Concerns

            **Symptoms:** Design works but can't be made
            **Diagnosis:**
            - Check surface radii (too strong?)
            - Check edge/center thickness ratios
            - Review glass availability

            **Solutions:**
            - Add MNCT/MXCT constraints
            - Add MNET/MXET constraints
            - Limit curvature ranges (CVGT/CVLT)
            """;
    }
}
