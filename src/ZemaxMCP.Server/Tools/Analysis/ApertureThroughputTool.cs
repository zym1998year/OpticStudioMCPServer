using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Tools.RayTrace;

namespace ZemaxMCP.Server.Tools.Analysis;

[McpServerToolType]
public class ApertureThroughputTool
{
    private readonly IZemaxSession _session;
    public ApertureThroughputTool(IZemaxSession session) => _session = session;
    public record VignetteCount(int Surface,int Count);
    public record Result(bool Success,string? Error=null,int SurfaceNumber=0,int GridSize=0,int TotalPupilRays=0,int ClearRays=0,int VignettedRays=0,int ErrorRays=0,double ClearFraction=0,double IntensityWeightedFraction=0,VignetteCount[]? VignetteBySurface=null);
    [McpServerTool(Name="zemax_aperture_throughput")]
    [Description("Trace a circular normalized-pupil grid and report real aperture/obscuration throughput and vignette surface counts.")]
    public async Task<Result> ExecuteAsync(double hx=0,double hy=0,int wavelength=1,int surface=0,int gridSize=41)
    {
        if(gridSize<5||gridSize>101)return new Result(false,"gridSize must be between 5 and 101.",GridSize:gridSize);
        try{return await _session.ExecuteAsync("ApertureThroughput",new Dictionary<string,object?>{{"hx",hx},{"hy",hy},{"wavelength",wavelength},{"surface",surface},{"gridSize",gridSize}},system=>
        {var target=surface==0?system.LDE.NumberOfSurfaces-1:surface;if(target<1||target>=system.LDE.NumberOfSurfaces)return new Result(false,$"Invalid destination surface: {surface}.",target,gridSize);int total=0,clear=0,vignetted=0,errors=0;double clearIntensity=0,allIntensity=0;var count=new Dictionary<int,int>();var ray=system.Tools.OpenBatchRayTrace();try{for(var iy=0;iy<gridSize;iy++){var py=-1d+2d*iy/(gridSize-1d);for(var ix=0;ix<gridSize;ix++){var px=-1d+2d*ix/(gridSize-1d);if(px*px+py*py>1d+1e-12)continue;total++;var ok=ray.SingleRayNormUnpol(RaysType.Real,target,wavelength,hx,hy,px,py,false,out var error,out var vignette,out _,out _,out _,out _,out _,out _,out _,out _,out _,out _,out var intensity);if(!ok||error!=0){errors++;continue;}allIntensity+=intensity;if(vignette==0){clear++;clearIntensity+=intensity;}else{vignetted++;count[vignette]=count.TryGetValue(vignette,out var n)?n+1:1;}}}}finally{ray.Close();}return new Result(true,SurfaceNumber:target,GridSize:gridSize,TotalPupilRays:total,ClearRays:clear,VignettedRays:vignetted,ErrorRays:errors,ClearFraction:total>0?(double)clear/total:0,IntensityWeightedFraction:allIntensity>0?clearIntensity/allIntensity:0,VignetteBySurface:count.OrderBy(x=>x.Key).Select(x=>new VignetteCount(x.Key,x.Value)).ToArray());});}
        catch(Exception ex){return new Result(false,ex.Message,SurfaceNumber:surface,GridSize:gridSize);}
    }
}
