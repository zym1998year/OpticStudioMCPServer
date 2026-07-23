using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SurfaceApertureTool
{
    private readonly IZemaxSession _session;
    public SurfaceApertureTool(IZemaxSession session) => _session = session;
    public record Result(bool Success, string? Error = null, int SurfaceNumber = 0, string? ApertureType = null, double? MinimumRadius = null, double? MaximumRadius = null, double? XDecenter = null, double? YDecenter = null);

    [McpServerTool(Name = "zemax_set_surface_aperture")]
    [Description("Set a real sequential circular aperture or obscuration; unlike Semi-Diameter it terminates rays.")]
    public async Task<Result> SetAsync(int surfaceNumber, string apertureType, double minimumRadius = 0, double? maximumRadius = null, double xDecenter = 0, double yDecenter = 0)
    {
        try { return await _session.ExecuteAsync("SetSurfaceAperture", new Dictionary<string, object?> { ["surfaceNumber"]=surfaceNumber,["apertureType"]=apertureType,["minimumRadius"]=minimumRadius,["maximumRadius"]=maximumRadius,["xDecenter"]=xDecenter,["yDecenter"]=yDecenter }, system =>
        {
            var lde=system.LDE; if(surfaceNumber<0||surfaceNumber>=lde.NumberOfSurfaces) return new Result(false,$"Invalid surface number: {surfaceNumber}.",surfaceNumber);
            if(!Enum.TryParse<SurfaceApertureTypes>(apertureType,true,out var type) || type is not (SurfaceApertureTypes.None or SurfaceApertureTypes.CircularAperture or SurfaceApertureTypes.CircularObscuration or SurfaceApertureTypes.FloatingAperture)) return new Result(false,$"Unsupported aperture type '{apertureType}'.",surfaceNumber);
            var surface=lde.GetSurfaceAt(surfaceNumber); var settings=surface.ApertureData.CreateApertureTypeSettings(type);
            if(type is SurfaceApertureTypes.CircularAperture or SurfaceApertureTypes.CircularObscuration)
            {
                if(!maximumRadius.HasValue||maximumRadius<=0||minimumRadius<0||minimumRadius>=maximumRadius) return new Result(false,"maximumRadius must be positive and greater than minimumRadius.",surfaceNumber);
                ISurfaceApertureCircular circular=type==SurfaceApertureTypes.CircularAperture?settings._S_CircularAperture:settings._S_CircularObscuration;
                circular.MinimumRadius=minimumRadius; circular.MaximumRadius=maximumRadius.Value; circular.ApertureXDecenter=xDecenter; circular.ApertureYDecenter=yDecenter;
            }
            surface.ApertureData.ChangeApertureTypeSettings(settings); return Read(surfaceNumber,surface);
        }); }
        catch(Exception ex){return new Result(false,ex.Message,surfaceNumber);}
    }

    [McpServerTool(Name = "zemax_get_surface_aperture")]
    [Description("Read the real sequential aperture or obscuration on a surface.")]
    public async Task<Result> GetAsync(int surfaceNumber)
    {
        try { return await _session.ExecuteAsync("GetSurfaceAperture", new Dictionary<string, object?> { ["surfaceNumber"]=surfaceNumber }, system =>
        { var lde=system.LDE; return surfaceNumber<0||surfaceNumber>=lde.NumberOfSurfaces ? new Result(false,$"Invalid surface number: {surfaceNumber}.",surfaceNumber) : Read(surfaceNumber,lde.GetSurfaceAt(surfaceNumber)); }); }
        catch(Exception ex){return new Result(false,ex.Message,surfaceNumber);}
    }
    private static Result Read(int number, ILDERow surface)
    {
        var type=surface.ApertureData.CurrentType; if(type is not (SurfaceApertureTypes.CircularAperture or SurfaceApertureTypes.CircularObscuration)) return new Result(true,SurfaceNumber:number,ApertureType:type.ToString());
        var s=surface.ApertureData.CurrentTypeSettings; ISurfaceApertureCircular c=type==SurfaceApertureTypes.CircularAperture?s._S_CircularAperture:s._S_CircularObscuration;
        return new Result(true,SurfaceNumber:number,ApertureType:type.ToString(),MinimumRadius:c.MinimumRadius,MaximumRadius:c.MaximumRadius,XDecenter:c.ApertureXDecenter,YDecenter:c.ApertureYDecenter);
    }
}
