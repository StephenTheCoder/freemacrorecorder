using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MacroStudio.Core;
using MacroStudio.Windows;

namespace MacroStudio;
public static class CommandLine
{
    public const string Usage="MacroStudio.exe --run <macro.json> [--validate] [--timeout <seconds>] [--log <file>]\nMacroStudio.exe --help\nRuns require an unlocked interactive Windows desktop. Escape cancels. Exit codes: 0 success, 2 validation/arguments, 3 execution error, 4 cancellation, 5 unavailable desktop.";
    [DllImport("user32.dll",SetLastError=true)] static extern nint OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(nint handle,int index,StringBuilder name,int bytes,out int needed);
    [DllImport("kernel32.dll")] static extern bool AttachConsole(uint process);
    public static bool DesktopAvailable(){var h=OpenInputDesktop(0,false,0x1);if(h==0)return false;try{var name=new StringBuilder(256);return GetUserObjectInformation(h,2,name,name.Capacity*2,out _)&&name.ToString().Equals("Default",StringComparison.OrdinalIgnoreCase);}finally{CloseDesktop(h);}}
    public static async Task<int> RunAsync(string[] args){
        AttachConsole(uint.MaxValue);string? log=null;PlaybackEngine? engine=null;var report=new List<string>();int result=0;
        void Write(string text){report.Add(text);Console.WriteLine(text);}
        try{
            if(args.SequenceEqual(new[]{"--help"})){Write(Usage);return 0;}
            string? file=null;bool validate=false;int timeout=3600;
            for(int i=0;i<args.Length;i++){string Next()=>++i<args.Length?args[i]:throw new ArgumentException("Missing argument value.");switch(args[i]){case "--run":file=Next();break;case "--validate":validate=true;break;case "--log":log=Path.GetFullPath(Next());break;case "--timeout":timeout=int.Parse(Next());if(timeout is <1 or >86400)throw new ArgumentException("Timeout must be 1–86400 seconds.");break;default:throw new ArgumentException("Unknown argument: "+args[i]);}}
            if(file is null)throw new ArgumentException(Usage);file=Path.GetFullPath(file);var doc=await MacroStore.LoadAsync(file);await MacroProgram.BuildAsync(doc.Actions,Path.GetDirectoryName(file)!,file);
            if(validate){Write("Validated: "+file);return 0;}
            if(!DesktopAvailable()){Write("Cannot run: an unlocked interactive desktop is required. In Task Scheduler select 'Run only when user is logged on', then keep the session unlocked. Secure/locked/service desktops are unsupported.");return 5;}
            using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(timeout));using var hooks=new HookService();await hooks.ConfigureAsync([new("Stop",0,27)],false);hooks.Control+=_=>cancel.Cancel();ConsoleCancelEventHandler stop=(_,e)=>{e.Cancel=true;cancel.Cancel();};Console.CancelKeyPress+=stop;
            try{var sink=new WindowsInput();engine=new(sink,new(),new WindowsAutomationHost(sink)){BaseDirectory=Path.GetDirectoryName(file)!,RootFile=file,RespectBreakpoints=false};await engine.PlayAsync(doc.Actions,doc.Playback,cancel.Token,1000);Write("Playback complete: "+file);}finally{Console.CancelKeyPress-=stop;}
        }catch(OperationCanceledException){result=4;Write("Playback cancelled; held input released.");}
        catch(Exception ex){result=ex is InvalidDataException or ArgumentException or FileNotFoundException?2:3;Write("Error: "+ex.Message);}
        finally{
            log??=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacroStudio","Logs",$"run-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
            try{Directory.CreateDirectory(Path.GetDirectoryName(log)!);var details=engine?.Logs.Select(l=>$"{l.Time:O}\t{l.Level}\t{l.Action}\t{l.Message}")??[];await File.WriteAllLinesAsync(log,report.Concat(details));}catch(Exception ex){Console.Error.WriteLine("Could not save run log: "+ex.Message);}
        }
        return result;
    }
}
