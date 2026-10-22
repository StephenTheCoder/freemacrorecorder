using System.IO;
using System.Windows;
using MacroStudio.Core;
using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MacroStudio.Windows;
public sealed partial class WindowsAutomationHost
{
    public static async Task<(string Text,List<(string Text,ScreenRegion Bounds)> Words)> RecognizeAsync(Mat image,ScreenRegion region,string language,CancellationToken token) {
        var engine=OcrEngine.TryCreateFromLanguage(new Language(language))??throw new InvalidOperationException($"Local OCR language '{language}' is not installed. Install its Windows language OCR feature or choose an installed language.");
        if(image.Width>OcrEngine.MaxImageDimension||image.Height>OcrEngine.MaxImageDimension)throw new InvalidOperationException($"OCR region exceeds the Windows OCR limit ({OcrEngine.MaxImageDimension} pixels per side). Select a smaller region.");
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(image.ToBytes(".png"));await writer.StoreAsync().AsTask(token);writer.DetachStream();}stream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(token);using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore).AsTask(token);
        var result=await engine.RecognizeAsync(bitmap).AsTask(token);token.ThrowIfCancellationRequested();
        var words=result.Lines.SelectMany(l=>l.Words).Select(w=>(w.Text,new ScreenRegion(region.X+(int)w.BoundingRect.X,region.Y+(int)w.BoundingRect.Y,(int)w.BoundingRect.Width,(int)w.BoundingRect.Height))).ToList();
        return (string.Join(Environment.NewLine,result.Lines.Select(l=>l.Text)),words);
    }
    async Task<bool> ExecuteDataAsync(MacroAction a,AutomationContext context,CancellationToken token) {
        var d=a.Data;var vars=context.Variables;
        switch(a.Kind){
            case ActionKind.AiText:case ActionKind.AiImage:
                byte[]? png=null;if(a.Kind==ActionKind.AiImage){var image=context.ResolvePath(d.File);if(new FileInfo(image).Length>10*1024*1024)throw new InvalidDataException("AI screenshot exceeds 10 MB.");using var bitmap=Cv2.ImRead(image);if(bitmap.Empty())throw new InvalidDataException("AI screenshot could not be decoded.");png=bitmap.ToBytes(".png");}
                var result=await AiSettings.Create().ProcessAsync(new(d.Value,png,d.Type),token);await context.Checkpoint(token);vars.Set(d.Variable,result);return true;
            case ActionKind.Screenshot:
                using(var capture=ScreenCapture.Grab(a.Detection.Region)){
                    var path=string.IsNullOrWhiteSpace(d.File)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacroStudio","Captures",Guid.NewGuid()+".png"):context.ResolvePath(d.File);
                    if(!Path.GetExtension(path).Equals(".png",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Screenshots use PNG files.");Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,capture.ToBytes(".png"),token);if(!string.IsNullOrWhiteSpace(d.Variable))vars.Set(d.Variable,new(VariableType.Image,path));
                }return true;
            case ActionKind.OcrText:case ActionKind.OcrFind:
                var r=ScreenCapture.Check(a.Detection.Region);using(var capture=ScreenCapture.Grab(r)){
                    var recognized=await RecognizeAsync(capture,r,d.Language,token);await context.Checkpoint(token);
                    if(a.Kind==ActionKind.OcrText)vars.Set(d.Variable,new(VariableType.Text,recognized.Text));
                    else {
                        var terms=d.Value.Split(' ',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);if(terms.Length==0)throw new InvalidDataException("OCR find requires search text.");
                        var matches=new List<ScreenRegion>();for(int i=0;i<=recognized.Words.Count-terms.Length;i++)if(terms.Select((s,j)=>s.Equals(recognized.Words[i+j].Text,StringComparison.OrdinalIgnoreCase)).All(b=>b)){
                            var bounds=recognized.Words.Skip(i).Take(terms.Length).Select(w=>w.Bounds).ToList();int x=bounds.Min(b=>b.X),y=bounds.Min(b=>b.Y);matches.Add(new(x,y,bounds.Max(b=>b.X+b.Width)-x,bounds.Max(b=>b.Y+b.Height)-y));
                        }
                        if(matches.Count!=1)throw new InvalidOperationException($"OCR text '{d.Value}' has {matches.Count} matches; exactly one is required. Narrow the region or use more distinctive text.");var m=matches[0];vars.Set(d.Variable,new(VariableType.Coordinates,$"{m.X+m.Width/2},{m.Y+m.Height/2}"));
                    }
                }return true;
            case ActionKind.ClipboardRead:vars.Set(d.Variable,new(VariableType.Text,await ClipboardAsync(()=>Clipboard.ContainsText()?Clipboard.GetText():"",token)));return true;
            case ActionKind.ClipboardWrite:await ClipboardAsync(()=>{if(d.Value.Length==0)Clipboard.Clear();else Clipboard.SetText(d.Value);return "";},token);return true;
            case ActionKind.ReadText:vars.Set(d.Variable,new(VariableType.Text,await ReadLimitedAsync(context.ResolvePath(d.File),token)));return true;
            case ActionKind.WriteText:
                if(d.Append)await File.AppendAllTextAsync(context.ResolvePath(d.File),d.Value,token);else await File.WriteAllTextAsync(context.ResolvePath(d.File),d.Value,token);return true;
            case ActionKind.ReadCsv:case ActionKind.WriteCsv:
                var file=context.ResolvePath(d.File);var rows=File.Exists(file)?CsvData.Parse(await ReadLimitedAsync(file,token)):a.Kind==ActionKind.WriteCsv?[]:throw new FileNotFoundException("CSV data file is missing.",file);
                if(d.Row>100000||d.Column>1000)throw new InvalidDataException("CSV cell exceeds row 100000 / column 1000 limit (zero based).");
                if(a.Kind==ActionKind.ReadCsv){if(d.Row>=rows.Count||d.Column>=rows[d.Row].Count)throw new InvalidDataException("CSV row/column does not exist.");vars.Set(d.Variable,new(VariableType.Text,rows[d.Row][d.Column]));}
                else{while(rows.Count<=d.Row)rows.Add([]);while(rows[d.Row].Count<=d.Column)rows[d.Row].Add("");rows[d.Row][d.Column]=d.Value;await File.WriteAllTextAsync(file,CsvData.Serialize(rows),token);}return true;
            default:return false;
        }
    }
    static async Task<string> ReadLimitedAsync(string path,CancellationToken token){if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("Text/CSV file exceeds 16 MB.");return await File.ReadAllTextAsync(path,token);}
    static async Task<T> ClipboardAsync<T>(Func<T> work,CancellationToken token){
        if(Application.Current is {} app)return await app.Dispatcher.InvokeAsync(work).Task.WaitAsync(token);
        var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{try{completion.SetResult(work());}catch(Exception ex){completion.SetException(ex);}}){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();return await completion.Task.WaitAsync(token);
    }
}
