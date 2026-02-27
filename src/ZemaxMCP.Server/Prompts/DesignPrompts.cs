using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ZemaxMCP.Server.Prompts;

[McpServerPromptType]
public class DesignPrompts
{
    [McpServerPrompt(Name = "design_singlet")]
    [Description("Guide for designing a simple singlet lens")]
    public string DesignSinglet(
        [Description("Target focal length in mm")] double focalLength,
        [Description("Target F/number")] double fNumber,
        [Description("Wavelength in nm")] double wavelength = 587.6)
    {
        var epd = focalLength / fNumber;
        return $"""
            Design a singlet lens with the following specifications:
            - Focal Length: {focalLength} mm
            - F/Number: {fNumber}
            - Entrance Pupil Diameter: {epd:F2} mm
            - Wavelength: {wavelength} nm

            ## Design Steps:

            1. **Create New System**
               - Use `zemax_connect` to connect to OpticStudio
               - Use `zemax_new_system` to start fresh
               - Use `zemax_set_aperture` with type="EPD" and value={epd:F2}

            2. **Set Wavelength**
               - Use `zemax_set_wavelengths` with wavelength {wavelength / 1000:F4} um

            3. **Add Surfaces**
               Use `zemax_add_surface` to create:
               - Surface 1: Front surface of lens (glass BK7)
               - Surface 2: Back surface of lens (air gap to image)

            4. **Set Initial Parameters**
               Use `zemax_set_surface` for each surface:
               - Choose a glass (BK7 is a good starting point)
               - Set initial radii using thin lens formula: 1/f = (n-1)(1/R1 - 1/R2)
               - Set center thickness (~5mm for a small lens)

            5. **Build Merit Function**
               Use `zemax_add_operand` to add:
               - EFFL operand targeting {focalLength} mm (weight: 10)
               - RSCE operands for on-axis field (weight: 1)
               - MNCT for minimum center thickness (~3mm)

            6. **Set Variables**
               Use `zemax_set_surface` with radiusVariable=true for surfaces 1 and 2

            7. **Optimize**
               Use `zemax_optimize` with algorithm="DLS"

            ## Useful Operands:
            - EFFL: Effective focal length
            - RSCE: RMS spot size (centroid reference)
            - MNCT: Minimum center thickness
            - MNET: Minimum edge thickness
            """;
    }

    [McpServerPrompt(Name = "design_doublet")]
    [Description("Guide for designing an achromatic doublet")]
    public string DesignDoublet(
        [Description("Target focal length in mm")] double focalLength,
        [Description("Target F/number")] double fNumber,
        [Description("Short wavelength in nm")] double wavelengthShort = 486.1,
        [Description("Center wavelength in nm")] double wavelengthCenter = 587.6,
        [Description("Long wavelength in nm")] double wavelengthLong = 656.3)
    {
        var epd = focalLength / fNumber;
        return $"""
            Design an achromatic doublet with the following specifications:
            - Focal Length: {focalLength} mm
            - F/Number: {fNumber}
            - Entrance Pupil Diameter: {epd:F2} mm
            - Wavelengths: {wavelengthShort} nm (F), {wavelengthCenter} nm (d), {wavelengthLong} nm (C)

            ## Design Approach:

            A cemented doublet uses a crown glass (low dispersion) and flint glass
            (high dispersion) to correct chromatic aberration.

            ### Step 1: System Setup
            - Use `zemax_connect` and `zemax_new_system`
            - Use `zemax_set_aperture` with type="EPD" and value={epd:F2}
            - Use `zemax_set_wavelengths` with three wavelengths (F, d, C lines)

            ### Step 2: Initial Configuration
            Surfaces needed (use `zemax_add_surface`):
            1. Front surface of crown element (N-BK7)
            2. Cemented interface (crown to flint, N-SF2)
            3. Back surface of flint element

            Suggested starting glasses:
            - Crown: N-BK7 (Schott)
            - Flint: N-SF2 (Schott)

            ### Step 3: Merit Function
            Use `zemax_add_operand` for:
            - **EFFL**: Target {focalLength} mm (weight: 10)
            - **AXCL**: Axial color between wavelengths 1 and 3 (target: 0, weight: 1)
            - **RSCE**: RMS spot at each wavelength (target: 0, weight: 1)

            ### Step 4: Variables
            Make variable (radiusVariable=true):
            - R1 (front radius)
            - R2 (cemented surface radius)
            - R3 (back radius)

            ### Step 5: Optimization
            Use `zemax_optimize` with algorithm="DLS"

            ## Key Operands Reference:
            - AXCL: Axial chromatic aberration
            - LACL: Lateral color
            - SPHA: Spherical aberration
            - EFFL: Effective focal length
            - RSCE/RSRE: RMS spot size
            """;
    }
}
