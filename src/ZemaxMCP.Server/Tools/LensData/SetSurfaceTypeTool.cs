using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetSurfaceTypeTool
{
    private readonly IZemaxSession _session;

    public SetSurfaceTypeTool(IZemaxSession session) => _session = session;

    public record SetSurfaceTypeResult(
        bool Success,
        string? Error = null,
        int SurfaceNumber = 0,
        string? PreviousType = null,
        string? NewType = null,
        string[]? AvailableTypes = null);

    [McpServerTool(Name = "zemax_set_surface_type")]
    [Description(
        "Change the surface type (e.g., Standard, CoordinateBreak, EvenAspheric, Toroidal, etc.). "
        + "Common types: 'Standard', 'CoordinateBreak', 'EvenAspheric', 'OddAspheric', "
        + "'ExtendedPolynomial', 'ZernikeStandardSag', 'Paraxial', 'Biconic'. "
        + "After changing type, use zemax_set_surface_parameter to set type-specific PARM values "
        + "(e.g., for CoordinateBreak: PARM 1=DecX, 2=DecY, 3=TiltX, 4=TiltY, 5=TiltZ, 6=Order). "
        + "Set listTypes=true to see all available surface types.")]
    public async Task<SetSurfaceTypeResult> ExecuteAsync(
        [Description("Surface number to modify")] int surfaceNumber,
        [Description("Surface type name (e.g., 'CoordinateBreak', 'EvenAsphere')")] string? surfaceType = null,
        [Description("If true, list all available surface types instead of changing")] bool listTypes = false)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["surfaceNumber"] = surfaceNumber,
                ["surfaceType"] = surfaceType,
                ["listTypes"] = listTypes
            };

            return await _session.ExecuteAsync("SetSurfaceType", parameters, system =>
            {
                var lde = system.LDE;

                if (surfaceNumber < 0 || surfaceNumber >= lde.NumberOfSurfaces)
                    throw new ArgumentException(
                        $"Invalid surface number: {surfaceNumber}. Valid range: 0-{lde.NumberOfSurfaces - 1}");

                var surface = lde.GetSurfaceAt(surfaceNumber);
                string previousType = surface.Type.ToString();

                if (listTypes)
                {
                    // Use dynamic to access GetSurfaceTypeSettings and enumerate types
                    try
                    {
                        dynamic dynSurface = surface;
                        var settings = dynSurface.GetSurfaceTypeSettings(surface.Type);
                        var types = (string[])settings.GetAvailableSurfaceTypes();
                        return new SetSurfaceTypeResult(
                            Success: true,
                            SurfaceNumber: surfaceNumber,
                            PreviousType: previousType,
                            AvailableTypes: types);
                    }
                    catch
                    {
                        return new SetSurfaceTypeResult(false,
                            Error: "Unable to list surface types in this ZOSAPI version.");
                    }
                }

                if (string.IsNullOrWhiteSpace(surfaceType))
                    return new SetSurfaceTypeResult(false,
                        Error: "surfaceType is required when listTypes=false");

                // Change surface type using ZOSAPI
                // The API requires: GetSurfaceTypeSettings(targetType) then ChangeType(settings)
                try
                {
                    dynamic dynSurface = surface;

                    // Parse the target SurfaceType enum
                    if (!Enum.TryParse<ZOSAPI.Editors.LDE.SurfaceType>(surfaceType, ignoreCase: true, out var targetType))
                    {
                        return new SetSurfaceTypeResult(false,
                            Error: $"Unknown surface type: '{surfaceType}'. "
                                + "Use listTypes=true to see valid types.");
                    }

                    var typeSettings = dynSurface.GetSurfaceTypeSettings(targetType);
                    dynSurface.ChangeType(typeSettings);

                    string newType = surface.Type.ToString();
                    if (string.Equals(previousType, newType, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(previousType, surfaceType, StringComparison.OrdinalIgnoreCase))
                    {
                        return new SetSurfaceTypeResult(false,
                            Error: $"Surface {surfaceNumber} type unchanged (still '{newType}'). "
                                + "Object and image surfaces may not support type changes.",
                            SurfaceNumber: surfaceNumber,
                            PreviousType: previousType,
                            NewType: newType);
                    }

                    return new SetSurfaceTypeResult(
                        Success: true,
                        SurfaceNumber: surfaceNumber,
                        PreviousType: previousType,
                        NewType: newType);
                }
                catch (Exception ex)
                {
                    return new SetSurfaceTypeResult(false,
                        Error: $"Failed to change surface type: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            return new SetSurfaceTypeResult(false, Error: ex.Message);
        }
    }
}
