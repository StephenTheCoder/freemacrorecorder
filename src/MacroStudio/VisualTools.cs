using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MacroStudio.Core;
using MacroStudio.Windows;
using Microsoft.Win32;
using OpenCvSharp;
using Point=System.Windows.Point;
using Window=System.Windows.Window;
using Image=System.Windows.Controls.Image;
using Rectangle=System.Windows.Shapes.Rectangle;

namespace MacroStudio;
public static class VisualTools
{
    public static BitmapImage Preview(string path) {var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(System.IO.Path.GetFullPath(path));b.EndInit();b.Freeze();return b;}
    public static async Task<ScreenRegion?> PickAsync(Window owner,Action<ScreenRegion,Mat>? selected=null) {
        owner.Hide();await Task.Delay(180);
        try {
            var bounds=ScreenCapture.Desktop;using var desktop=await Task.Run(()=>ScreenCapture.Grab(bounds));
            var bytes=desktop.ToBytes(".png");var image=new BitmapImage();using(var stream=new MemoryStream(bytes)){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();}image.Freeze();
            var w=new Window{Title="Select desktop region — drag, Escape cancels",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Topmost=true,ShowInTaskbar=false,Background=Brushes.Black,Cursor=Cursors.Cross};
            var canvas=new Canvas{Background=new ImageBrush(image){Stretch=Stretch.Fill}};w.Content=canvas;
            var hint=new TextBlock{Text="Drag a region • Escape cancels",Background=Brushes.Black,Foreground=Brushes.White,Padding=new(12),FontSize=18,IsHitTestVisible=false};canvas.Children.Add(hint);
            var rectangle=new Rectangle{Stroke=Brushes.DeepSkyBlue,StrokeThickness=2,Fill=new SolidColorBrush(Color.FromArgb(35,0,140,255)),IsHitTestVisible=false};canvas.Children.Add(rectangle);
            Point start=default;ScreenRegion? result=null;Exception? captureError=null;
            w.SourceInitialized+=(_,_)=>{ScreenCapture.Exclude(w);DesktopWindowBounds.Place(w,bounds);};
            canvas.MouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(canvas);canvas.CaptureMouse();};
            canvas.MouseMove+=(_,e)=>{if(!canvas.IsMouseCaptured)return;var end=e.GetPosition(canvas);Canvas.SetLeft(rectangle,Math.Min(start.X,end.X));Canvas.SetTop(rectangle,Math.Min(start.Y,end.Y));rectangle.Width=Math.Abs(start.X-end.X);rectangle.Height=Math.Abs(start.Y-end.Y);};
            canvas.MouseLeftButtonUp+=(_,e)=>{if(!canvas.IsMouseCaptured)return;var end=e.GetPosition(canvas);canvas.ReleaseMouseCapture();var a=canvas.PointToScreen(start);var b=canvas.PointToScreen(end);int x=(int)Math.Min(a.X,b.X),y=(int)Math.Min(a.Y,b.Y),width=(int)Math.Abs(a.X-b.X),height=(int)Math.Abs(a.Y-b.Y);if(width>=1&&height>=1){result=new(x,y,width,height);using var crop=new Mat(desktop,new OpenCvSharp.Rect(x-bounds.X,y-bounds.Y,width,height));try{selected?.Invoke(result,crop);}catch(Exception ex){captureError=ex;}}w.Close();};
            w.KeyDown+=(_,e)=>{if(e.Key==Key.Escape)w.Close();};w.ShowDialog();if(captureError!=null)throw new InvalidOperationException(captureError.Message,captureError);return result;
        } finally {owner.Show();owner.Activate();}
    }
    public static async Task HighlightAsync(VisualMatch match) {
        var w=new Window{Title="Match result",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,ShowInTaskbar=false,ShowActivated=false,IsHitTestVisible=false,Content=new Border{BorderBrush=Brushes.LimeGreen,BorderThickness=new(4)}};
        w.SourceInitialized+=(_,_)=>{ScreenCapture.Exclude(w);DesktopWindowBounds.Place(w,new(match.X,match.Y,match.Width,match.Height));};w.Show();await Task.Delay(1600);w.Close();
    }
}
public static class DesktopWindowBounds
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(nint h,nint after,int x,int y,int width,int height,uint flags);
    public static void Place(Window w,ScreenRegion r)=>SetWindowPos(new System.Windows.Interop.WindowInteropHelper(w).Handle,0,r.X,r.Y,r.Width,r.Height,0x14);
}
public sealed class DetectionEditor : Window
{
    public DetectionSettings Result {get;private set;}
    public DetectionEditor(MacroAction action,string directory) {
        WindowTheme.Apply(this);Result=MacroStore.Clone(action).Detection;Title="Visual targeting and waits";Width=680;Height=800;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(22)};Content=root;var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var ok=new Button{Content="Apply detection"};footer.Children.Add(new Button{Content="Cancel",IsCancel=true});footer.Children.Add(ok);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text="Physical pixels • exact scale • local OpenCV",FontWeight=FontWeights.SemiBold,FontSize=18});
        TextBox Field(string label,string value){panel.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap});var b=new TextBox{Text=value};System.Windows.Automation.AutomationProperties.SetName(b,label);panel.Children.Add(b);return b;}
        var image=Field("Template image path",Result.Image);
        var preview=new Image{Height=130,Stretch=Stretch.Uniform,Margin=new(0,8,0,12)};panel.Children.Add(preview);
        void UpdatePreview(){try{preview.Source=VisualTools.Preview(System.IO.Path.GetFullPath(image.Text,directory));}catch{preview.Source=null;}}UpdatePreview();image.LostFocus+=(_,_)=>UpdatePreview();
        var buttons=new WrapPanel();panel.Children.Add(buttons);var capture=new Button{Content="Capture template…"};var browse=new Button{Content="Browse image…"};buttons.Children.Add(capture);buttons.Children.Add(browse);
        var region=Field("Search region X,Y,width,height (blank = desktop)",Result.Region is {} r?$"{r.X},{r.Y},{r.Width},{r.Height}":"");
        var choose=new Button{Content="Pick search / wait region…"};panel.Children.Add(choose);
        var threshold=Field("Matching threshold (0.60–1.00)",Result.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var timeout=Field("Timeout in milliseconds",Result.TimeoutMs.ToString());
        var offsets=Field("Click offset X,Y within template",$"{Result.OffsetX},{Result.OffsetY}");
        var dpi=Field("Template DPI (recapture when DPI or zoom changes)",Result.TemplateDpi.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var color=Field("Pixel color (#RRGGBB; position uses action X,Y)",Result.Color);var tolerance=Field("Pixel / region-change color tolerance (0–255)",Result.ColorTolerance.ToString());
        var fraction=Field("Region change: fraction of pixels that must change (0–1)",Result.ChangeFraction.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var fallback=new CheckBox{Content="On image timeout, click action X,Y instead (explicit fallback)",IsChecked=Result.CoordinateFallback};panel.Children.Add(fallback);
        var test=new Button{Content="Test match — highlight result"};footer.Children.Insert(0,test);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)};panel.Children.Add(error);
        DetectionSettings Read(){var v=Result;v.Image=image.Text;var values=region.Text.Split(',',StringSplitOptions.TrimEntries);v.Region=string.IsNullOrWhiteSpace(region.Text)?null:values.Length==4?new(int.Parse(values[0]),int.Parse(values[1]),int.Parse(values[2]),int.Parse(values[3])):throw new FormatException("Region needs X,Y,width,height.");v.Threshold=double.Parse(threshold.Text,System.Globalization.CultureInfo.InvariantCulture);v.TimeoutMs=int.Parse(timeout.Text);var xy=offsets.Text.Split(',');if(xy.Length!=2)throw new FormatException("Offset needs X,Y.");v.OffsetX=int.Parse(xy[0]);v.OffsetY=int.Parse(xy[1]);v.TemplateDpi=double.Parse(dpi.Text,System.Globalization.CultureInfo.InvariantCulture);v.Color=color.Text;v.ColorTolerance=int.Parse(tolerance.Text);v.ChangeFraction=double.Parse(fraction.Text,System.Globalization.CultureInfo.InvariantCulture);v.CoordinateFallback=fallback.IsChecked==true;MacroStore.Validate(new(){Actions=[new(){Detection=v}]});return v;}
        capture.Click+=async(_,_)=>{try{await VisualTools.PickAsync(this,(r,mat)=>{ImageMatcher.ValidateTemplate(mat);var folder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacroStudio","Assets");Directory.CreateDirectory(folder);var path=System.IO.Path.Combine(folder,Guid.NewGuid()+".png");mat.SaveImage(path);image.Text=path;offsets.Text=$"{r.Width/2},{r.Height/2}";dpi.Text=ScreenCapture.DpiAt(r.X+r.Width/2,r.Y+r.Height/2).ToString(System.Globalization.CultureInfo.InvariantCulture);});UpdatePreview();}catch(Exception ex){error.Text=ex.Message;}};
        browse.Click+=(_,_)=>{var d=new OpenFileDialog{Filter="PNG images|*.png|Images|*.bmp;*.jpg"};if(d.ShowDialog(this)==true){image.Text=d.FileName;UpdatePreview();}};
        choose.Click+=async(_,_)=>{try{var r=await VisualTools.PickAsync(this);if(r!=null)region.Text=$"{r.X},{r.Y},{r.Width},{r.Height}";}catch(Exception ex){error.Text=ex.Message;}};
        test.Click+=async(_,_)=>{test.IsEnabled=false;using var cts=new CancellationTokenSource();void Cancel(object? s,EventArgs e)=>cts.Cancel();Closed+=Cancel;try{var d=Read();error.Text="Searching… close this dialog to cancel.";var match=await Task.Run(()=>new WindowsAutomationHost(new WindowsInput()).FindAsync(d,new(){BaseDirectory=directory},cts.Token));error.Text=$"Match {match.Score:P1} at {match.X},{match.Y}";await VisualTools.HighlightAsync(match);}catch(Exception ex){error.Text=ex.Message;}finally{Closed-=Cancel;test.IsEnabled=true;}};
        bool images=action.Kind is ActionKind.ImageClick or ActionKind.WaitImage or ActionKind.WaitImageGone;
        void ShowField(TextBox box,bool show){int i=panel.Children.IndexOf(box);box.Visibility=show?Visibility.Visible:Visibility.Collapsed;if(i>0)panel.Children[i-1].Visibility=box.Visibility;}
        foreach(var box in new[]{image,threshold,offsets,dpi})ShowField(box,images);
        preview.Visibility=buttons.Visibility=test.Visibility=images?Visibility.Visible:Visibility.Collapsed;
        ShowField(timeout,action.Kind is >=ActionKind.ImageClick and <=ActionKind.WaitWindow);
        ShowField(color,action.Kind==ActionKind.WaitPixel);ShowField(tolerance,action.Kind is ActionKind.WaitPixel or ActionKind.WaitRegionChange);ShowField(fraction,action.Kind==ActionKind.WaitRegionChange);
        fallback.Visibility=action.Kind==ActionKind.ImageClick?Visibility.Visible:Visibility.Collapsed;
        ShowField(region,action.Kind is not (ActionKind.WaitPixel or ActionKind.WaitWindow));choose.Visibility=region.Visibility;
        ok.Click+=(_,_)=>{try{Result=Read();DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
    }
}
