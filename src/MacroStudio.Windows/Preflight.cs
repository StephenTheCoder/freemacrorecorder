using System.IO;
using MacroStudio.Core;
using OpenCvSharp;

namespace MacroStudio.Windows;
public sealed partial class WindowsAutomationHost
{
    public async Task ValidateAsync(MacroProgram program,CancellationToken token){
        foreach(var a in program.Actions){
            token.ThrowIfCancellationRequested();
            if(a.Kind is ActionKind.ImageClick or ActionKind.WaitImage or ActionKind.WaitImageGone&&!a.Detection.Image.Contains("${")){
                using var template=Cv2.ImRead(Path.GetFullPath(a.Detection.Image,program.Directory));ImageMatcher.ValidateTemplate(template);
                if(a.Kind==ActionKind.ImageClick&&!a.Bindings.ContainsKey("detection.offsetX")&&!a.Bindings.ContainsKey("detection.offsetY")&&(a.Detection.OffsetX<0||a.Detection.OffsetX>=template.Width||a.Detection.OffsetY<0||a.Detection.OffsetY>=template.Height))throw new InvalidDataException("Image-click offset must lie inside its template.");
            }
            if(a.Kind is >=ActionKind.ImageClick and <=ActionKind.OcrFind && a.Kind!=ActionKind.WaitWindow && !a.Bindings.Keys.Any(k=>k.StartsWith("detection.region.")))ScreenCapture.Check(a.Detection.Region);
            if(a.Kind is ActionKind.OcrText or ActionKind.OcrFind&&!a.Data.Language.Contains("${")&&global::Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language(a.Data.Language)) is null)throw new InvalidDataException("The selected local OCR language is not installed: "+a.Data.Language);
            if(a.Kind is ActionKind.AiText or ActionKind.AiImage){var config=AiSettings.Load();if(!config.Enabled)throw new InvalidOperationException("AI is disabled. Enable and configure a provider before playing a macro with AI actions.");config.Validate();JsonChatProvider.Schema(a.Data.Type);}
        }
        foreach(var child in program.Calls.Values)await ValidateAsync(child,token);
    }
}
