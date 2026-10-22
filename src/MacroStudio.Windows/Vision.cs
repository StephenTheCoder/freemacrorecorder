using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MacroStudio.Core;
using OpenCvSharp;
using Window=System.Windows.Window;
using System.IO;
using Rect=OpenCvSharp.Rect;

namespace MacroStudio.Windows;

public sealed record VisualMatch(int X, int Y, int Width, int Height, double Score);
public static class ScreenCapture
{
    [DllImport("user32.dll")] static extern nint GetDC(nint window);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll", SetLastError=true)] static extern bool BitBlt(nint dest, int x, int y, int w, int h, nint source, int sx, int sy, uint op);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll", SetLastError=true)] static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] static extern nint MonitorFromPoint(Native.Point point,uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(nint monitor,int type,out uint x,out uint y);
    public static ScreenRegion Desktop => new(Native.GetSystemMetrics(76),Native.GetSystemMetrics(77),Native.GetSystemMetrics(78),Native.GetSystemMetrics(79));
    public static double DpiAt(int x, int y) {
        var monitor=MonitorFromPoint(new(x,y),2);if(GetDpiForMonitor(monitor,0,out var dpi,out _)==0)return dpi;
        var h=Native.GetAncestor(Native.WindowFromPoint(new(){X=x,Y=y}),2);
        return h==0?96:Math.Max(96,GetDpiForWindow(h));
    }
    public static void Exclude(Window window) {
        var h=new WindowInteropHelper(window).Handle;
        if(h!=0 && !SetWindowDisplayAffinity(h,0x11)) throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot exclude the recorder window from capture.");
    }
    public static ScreenRegion Check(ScreenRegion? region) {
        var r=region??Desktop;var d=Desktop;
        if(r.Width<1||r.Height<1||(long)r.Width*r.Height>16000000||r.X<d.X||r.Y<d.Y||(long)r.X+r.Width>(long)d.X+d.Width||(long)r.Y+r.Height>(long)d.Y+d.Height)
            throw new InvalidOperationException("Capture region is outside the current desktop or exceeds 16 million pixels. Select a valid search region.");
        return r;
    }
    public static Mat Grab(ScreenRegion? region=null) {
        var old=Native.SetThreadDpiAwarenessContext(-4);
        nint src=0,dc=0,bmp=0,previous=0;
        try {
            var r=Check(region); src=GetDC(0);dc=CreateCompatibleDC(src);bmp=CreateCompatibleBitmap(src,r.Width,r.Height);
            if(src==0||dc==0||bmp==0)throw new Win32Exception("Desktop capture could not allocate an image.");
            previous=SelectObject(dc,bmp);
            if(!BitBlt(dc,0,0,r.Width,r.Height,src,r.X,r.Y,0x00CC0020|0x40000000))throw new Win32Exception(Marshal.GetLastWin32Error(),"Desktop capture failed; use an unlocked interactive desktop.");
            var image=Imaging.CreateBitmapSourceFromHBitmap(bmp,0,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=new System.IO.MemoryStream();encoder.Save(stream);
            return Cv2.ImDecode(stream.ToArray(),ImreadModes.Color);
        } finally {if(previous!=0)SelectObject(dc,previous);if(bmp!=0)DeleteObject(bmp);if(dc!=0)DeleteDC(dc);if(src!=0)ReleaseDC(0,src);Native.SetThreadDpiAwarenessContext(old);}
    }
}
public static class ImageMatcher
{
    public static void ValidateTemplate(Mat template) {
        if(template.Empty()||template.Width<8||template.Height<8||template.Width>1024||template.Height>1024)throw new InvalidDataException("Template must be 8–1024 pixels on each side.");
        using var gray=new Mat();Cv2.CvtColor(template,gray,ColorConversionCodes.BGR2GRAY);Cv2.MeanStdDev(gray,out _,out var deviation);
        using var edges=new Mat();Cv2.Canny(gray,edges,40,120);
        if(deviation.Val0<8||Cv2.CountNonZero(edges)<Math.Max(12,template.Total()*.005))throw new InvalidDataException("Template has too little visual detail. Capture a distinctive icon, text and surrounding edges.");
    }
    public static VisualMatch? Find(Mat screen,Mat template,double threshold,CancellationToken token=default,Action? checkpoint=null) {
        ValidateTemplate(template);token.ThrowIfCancellationRequested();checkpoint?.Invoke();
        if(template.Width>screen.Width||template.Height>screen.Height)return null;
        var candidates=new List<VisualMatch>();
        // Bound each synchronous OpenCV operation and check cancellation between strips.
        int screenHeight=screen.Height,templateHeight=template.Height;
        for(int y=0;y<=screenHeight-templateHeight;y+=128) {
            token.ThrowIfCancellationRequested();checkpoint?.Invoke();int rows=Math.Min(128,screen.Height-template.Height-y+1);
            using var strip=new Mat(screen,new Rect(0,y,screen.Width,rows+template.Height-1));using var scores=new Mat();
            Cv2.MatchTemplate(strip,template,scores,TemplateMatchModes.CCoeffNormed);
            for(int i=0;i<8;i++) {
                Cv2.MinMaxLoc(scores,out _,out var best,out _,out var at);
                if(!double.IsFinite(best)||best<threshold)break;
                candidates.Add(new(at.X,y+at.Y,template.Width,template.Height,best));
                int x0=Math.Max(0,at.X-template.Width/2),y0=Math.Max(0,at.Y-template.Height/2);
                Cv2.Rectangle(scores,new Rect(x0,y0,Math.Min(scores.Width-x0,at.X+template.Width/2+1-x0),Math.Min(scores.Height-y0,at.Y+template.Height/2+1-y0)),Scalar.All(-1),-1);
            }
        }
        token.ThrowIfCancellationRequested();checkpoint?.Invoke();var sorted=candidates.OrderByDescending(c=>c.Score).ToList();if(sorted.Count==0)return null;
        var first=sorted[0];
        if(sorted.Skip(1).Any(m=>(Math.Abs(m.X-first.X)>template.Width/2||Math.Abs(m.Y-first.Y)>template.Height/2)&&m.Score>=Math.Max(threshold,first.Score-.03)))
            throw new InvalidOperationException("Ambiguous image: multiple targets match. Narrow the search region or capture more unique context.");
        return first;
    }
}
public sealed partial class WindowsAutomationHost(IInputSink sink) : IAutomationHost
{
    public async Task<VisualMatch> FindAsync(DetectionSettings d,AutomationContext context,CancellationToken token) {
        if(d.Scale!=1)throw new InvalidOperationException("Scaling is unsupported; recapture at the target DPI and zoom.");
        var r=ScreenCapture.Check(d.Region);using var template=Cv2.ImRead(context.ResolvePath(d.Image));ImageMatcher.ValidateTemplate(template);
        var dpis=new[]{ScreenCapture.DpiAt(r.X,r.Y),ScreenCapture.DpiAt(r.X+r.Width-1,r.Y),ScreenCapture.DpiAt(r.X,r.Y+r.Height-1),ScreenCapture.DpiAt(r.X+r.Width-1,r.Y+r.Height-1)};
        if(dpis.All(dpi=>Math.Abs(dpi-d.TemplateDpi)>1))throw new InvalidOperationException("Search region DPI differs from the template. Scaling is unsupported; recapture the template at the current DPI and zoom.");
        VisualMatch? found=null;
        await PollCoreAsync(d,context,token,guard=>{
            using var screen=ScreenCapture.Grab(r);guard();found=ImageMatcher.Find(screen,template,d.Threshold,token,guard);
            if(found is null)return false;
            found=found with {X=found.X+r.X,Y=found.Y+r.Y};
            if(Math.Abs(ScreenCapture.DpiAt(found.X+found.Width/2,found.Y+found.Height/2)-d.TemplateDpi)>1)throw new InvalidOperationException("Target DPI differs from captured template. Automatic scaling is unsupported; recapture at the current DPI.");
            return true;
        });return found!;
    }
    public static Task PollAsync(DetectionSettings d,AutomationContext context,CancellationToken token,Func<bool> check) => PollCoreAsync(d,context,token,_=>check());
    static async Task PollCoreAsync(DetectionSettings d,AutomationContext context,CancellationToken token,Func<Action,bool> check) {
        var active=new PlaybackClock();
        void Guard(){token.ThrowIfCancellationRequested();if(active.Elapsed>=d.TimeoutMs)throw new TimeoutException($"{d.TimeoutMs} ms timeout: target condition was not identified. Check visibility, region, DPI/zoom and template detail.");}
        while(true) {
            token.ThrowIfCancellationRequested();active.Pause();await context.Checkpoint(token);active.Resume();
            Guard();bool identified=check(Guard);Guard();if(identified)return;
            await Task.Delay(d.PollMs,token);
        }
    }
    public async Task ExecuteAsync(MacroAction a,AutomationContext context,CancellationToken token) {
        var d=a.Detection;
        switch(a.Kind) {
            case ActionKind.ImageClick:
                VisualMatch match;
                try {match=await FindAsync(d,context,token);}
                catch(TimeoutException) when(d.CoordinateFallback) {await context.Checkpoint(token);sink.Move(a.X,a.Y,a);sink.Button(a.Button,true);sink.Button(a.Button,false);return;}
                if(d.OffsetX<0||d.OffsetY<0||d.OffsetX>=match.Width||d.OffsetY>=match.Height)throw new InvalidOperationException("Click offset lies outside the template.");
                await context.Checkpoint(token);sink.Move(match.X+d.OffsetX,match.Y+d.OffsetY,new(){Coordinates=CoordinateMode.Desktop});sink.Button(a.Button,true);sink.Button(a.Button,false);break;
            case ActionKind.WaitImage: await FindAsync(d,context,token);break;
            case ActionKind.WaitImageGone:
                using(var template=Cv2.ImRead(context.ResolvePath(d.Image))) {
                    ImageMatcher.ValidateTemplate(template);
                    await PollCoreAsync(d,context,token,guard=>{using var screen=ScreenCapture.Grab(d.Region);guard();return ImageMatcher.Find(screen,template,d.Threshold,token,guard) is null;});
                }break;
            case ActionKind.WaitPixel:
                var color=(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(d.Color);
                await PollAsync(d,context,token,()=>{using var pixel=ScreenCapture.Grab(new(a.X,a.Y,1,1));var c=pixel.At<Vec3b>(0,0);return Math.Abs(c.Item0-color.B)<=d.ColorTolerance&&Math.Abs(c.Item1-color.G)<=d.ColorTolerance&&Math.Abs(c.Item2-color.R)<=d.ColorTolerance;});break;
            case ActionKind.WaitRegionChange:
                using(var baseline=ScreenCapture.Grab(d.Region)) {
                    await PollAsync(d,context,token,()=>{using var current=ScreenCapture.Grab(d.Region);using var diff=new Mat();Cv2.Absdiff(baseline,current,diff);using var gray=new Mat();Cv2.CvtColor(diff,gray,ColorConversionCodes.BGR2GRAY);Cv2.Threshold(gray,gray,d.ColorTolerance,255,ThresholdTypes.Binary);return Cv2.CountNonZero(gray)/(double)gray.Total()>=d.ChangeFraction;});
                }break;
            case ActionKind.WaitWindow:
                await PollAsync(d,context,token,()=>{try {WindowCatalog.Find(a.Target??throw new InvalidDataException("Wait window needs a target."));return true;}catch(InvalidOperationException ex)when(ex.Message.StartsWith("Target window not found")){return false;}});break;
            default:if(!await ExecuteDataAsync(a,context,token))throw new InvalidOperationException($"Unsupported automation action: {a.Kind}");break;
        }
    }
}
