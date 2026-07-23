using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Editors.LDE;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class OffAxisConicFreeformTool
{
    private readonly IZemaxSession _session;
    public OffAxisConicFreeformTool(IZemaxSession session) => _session = session;
    public record Result(bool Success,string? Error=null,int SurfaceNumber=0,double Offset=0,double NormRadius=0,bool OffsetIsVariable=false);
    [McpServerTool(Name="zemax_set_off_axis_conic")]
    [Description("Read or set Offset and normalization radius of an OffAxisConicFreeform surface.")]
    public async Task<Result> ExecuteAsync(int surfaceNumber,double? offset=null,double? normRadius=null,bool? offsetVariable=null)
    {
        try{return await _session.ExecuteAsync("SetOffAxisConic",new Dictionary<string,object?>{{"surfaceNumber",surfaceNumber},{"offset",offset},{"normRadius",normRadius},{"offsetVariable",offsetVariable}},system=>
        {var lde=system.LDE;if(surfaceNumber<0||surfaceNumber>=lde.NumberOfSurfaces)return new Result(false,$"Invalid surface number: {surfaceNumber}.",surfaceNumber);var s=lde.GetSurfaceAt(surfaceNumber);if(s.Type!=SurfaceType.OffAxisConicFreeform)return new Result(false,$"Surface {surfaceNumber} is {s.Type}, not OffAxisConicFreeform.",surfaceNumber);var d=s.SurfaceData as ISurfaceOffAxisConicFreeform;if(d==null)return new Result(false,"ZOSAPI did not expose ISurfaceOffAxisConicFreeform.",surfaceNumber);if(offset.HasValue)d.Offset=offset.Value;if(normRadius.HasValue)d.NormRadius=normRadius.Value;if(offsetVariable==true)d.OffsetCell.MakeSolveVariable();if(offsetVariable==false)d.OffsetCell.MakeSolveFixed();bool variable;try{variable=d.OffsetCell.Solve==ZOSAPI.Editors.SolveType.Variable;}catch{variable=false;}return new Result(true,SurfaceNumber:surfaceNumber,Offset:d.Offset,NormRadius:d.NormRadius,OffsetIsVariable:variable);});}
        catch(Exception ex){return new Result(false,ex.Message,surfaceNumber);}
    }
}
