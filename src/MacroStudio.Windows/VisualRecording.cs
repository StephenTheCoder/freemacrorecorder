using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroStudio.Core;
using OpenCvSharp;
using System.IO;

namespace MacroStudio.Windows;
// Separate worker: hooks only enqueue input metadata. Frames must finish BEFORE an input edge.
public sealed class VisualRecording : IDisposable
{
    sealed record Frame(double End,ScreenRegion Region,byte[] Png,double Dpi);
    readonly List<Frame> frames=[];
    readonly object gate=new();
    readonly CancellationTokenSource cancel=new();
    readonly Task worker;
    public static double Timestamp=>Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency;
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Native.Point point);
    public VisualRecording(){worker=Task.Run(Capture);}
    async Task Capture(){
        try{while(!cancel.IsCancellationRequested){
            if(GetCursorPos(out var p)) {
                try {
                    var d=ScreenCapture.Desktop;int x=Math.Clamp(p.X-48,d.X,d.X+d.Width-96),y=Math.Clamp(p.Y-32,d.Y,d.Y+d.Height-64);var r=new ScreenRegion(x,y,96,64);
                    using var image=ScreenCapture.Grab(r);var frame=new Frame(Timestamp,r,image.ToBytes(".png"),ScreenCapture.DpiAt(p.X,p.Y));
                    lock(gate){frames.Add(frame);frames.RemoveAll(f=>f.End<frame.End-2000);}
                }catch(Exception ex)when(ex is not OutOfMemoryException){/* A missing frame is reported for the affected click, never silently substituted. */}
            }
            await Task.Delay(45,cancel.Token);
        }}catch(OperationCanceledException){}
    }
    public DetectionSettings? Before(CapturedInput input,double lastEdge) {
        Frame? f;lock(gate)f=frames.LastOrDefault(f=>f.End<input.Stamp&&f.End>lastEdge&&input.Stamp-f.End<=250&&input.X>=f.Region.X&&input.Y>=f.Region.Y&&input.X<f.Region.X+f.Region.Width&&input.Y<f.Region.Y+f.Region.Height);
        if(f is null)return null;
        using var mat=Cv2.ImDecode(f.Png,ImreadModes.Color);ImageMatcher.ValidateTemplate(mat);
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacroStudio","Assets");Directory.CreateDirectory(folder);var path=Path.Combine(folder,Guid.NewGuid()+".png");File.WriteAllBytes(path,f.Png);
        return new(){Image=path,TemplateDpi=f.Dpi,OffsetX=input.X-f.Region.X,OffsetY=input.Y-f.Region.Y};
    }
    public void Dispose(){cancel.Cancel();worker.GetAwaiter().GetResult();cancel.Dispose();}
}
