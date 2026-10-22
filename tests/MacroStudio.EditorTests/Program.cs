using MacroStudio;
using MacroStudio.Core;
using MacroStudio.Windows;
using System.Windows.Interop;

internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        var application=new MacroStudio.App();application.InitializeComponent();
        using var vm=new StudioViewModel();int checks=0;
        if(args.Contains("--render")){RenderUi(vm);return 0;}
        void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        void Check(string name,Action run){run();checks++;Console.WriteLine("PASS "+name);}
        void Select(params int[] indices)=>vm.Selected=indices.Select(i=>vm.Rows[i]).ToList();
        try {
            Check("compact controller has no-activate style and preserves foreground focus",()=>{var before=Native.GetForegroundWindow();var controller=new ControllerWindow(vm,_=>{});var hwnd=new WindowInteropHelper(controller).EnsureHandle();Assert(!controller.ShowActivated&&((long)Native.GetWindowLongPtr(hwnd,-20)&0x08000000)!=0,"Missing nonactivating style");Assert(Native.SendMessage(hwnd,0x21,0,0)==3,"WM_MOUSEACTIVATE should return MA_NOACTIVATE");Assert(Native.GetForegroundWindow()==before,"Controller stole focus");controller.Close();});
            Check("insert, edit, dirty tracking and saved baseline",()=>{vm.Replace(new());Assert(!vm.Dirty,"New document dirty");vm.Insert(new(){Kind=ActionKind.Text,Text="one"});Assert(vm.Dirty&&vm.Rows.Count==1,"Insert failed");var action=MacroStore.Clone(vm.Document.Actions[0]);action.Text="edited";vm.Edit(action.Id,action);Assert(vm.Rows[0].Description.Contains("edited"),"Edit failed");vm.MarkSaved("test.json");Assert(!vm.Dirty,"Saved baseline failed");});
            Check("undo/redo restores actions and dirty baseline",()=>{vm.Undo();Assert(vm.Document.Actions[0].Text=="one"&&vm.Dirty,"Undo did not restore original");vm.Redo();Assert(vm.Document.Actions[0].Text=="edited"&&!vm.Dirty,"Redo baseline failed");});
            Check("multi-selection duplicate gives fresh IDs in sequence",()=>{vm.Insert(new(){Kind=ActionKind.Delay,DurationMs=200});Select(0,1);vm.Paste(vm.Copy());Assert(vm.Document.Actions.Count==4,"Paste size");Assert(vm.Document.Actions.Select(a=>a.Id).Distinct().Count()==4,"IDs duplicated");Assert(vm.Document.Actions.Select(a=>a.Kind).SequenceEqual(new[]{ActionKind.Text,ActionKind.Delay,ActionKind.Text,ActionKind.Delay}),"Paste ordering");});
            Check("reorder and move preserve selection and relative order",()=>{Select(2,3);var ids=vm.Selected.Select(r=>r.Action.Id).ToArray();vm.Reorder(vm.Document.Actions[0].Id);Assert(vm.Document.Actions.Take(2).Select(a=>a.Id).SequenceEqual(ids),"Reorder failed");Assert(vm.Selected.Select(r=>r.Action.Id).SequenceEqual(ids),"Selection lost");vm.MoveSelection(1);Assert(vm.Document.Actions.Skip(1).Take(2).Select(a=>a.Id).SequenceEqual(ids),"Move order changed");});
            Check("enable/disable, search and delete cooperate with history",()=>{Select(1,2);vm.ToggleEnabled();Assert(vm.Selected.All(r=>!r.Enabled),"Disable failed");vm.ToggleEnabled();Assert(vm.Selected.All(r=>r.Enabled),"Enable failed");vm.Search="edited";Assert(vm.Rows.Count==2,"Search failed");Select(0,1);vm.Delete();Assert(vm.Document.Actions.Count==2&&vm.Rows.Count==0,"Filtered delete failed");vm.Undo();Assert(vm.Rows.Count==2,"Delete undo failed");vm.Search="";});
            Check("playback options validate and participate in undo",()=>{vm.Speed=2;vm.UntilStopped=true;vm.SkipPaths=true;vm.RestoreWindows=true;vm.Repetitions=3;vm.Undo();Assert(vm.Repetitions==1,"Options undo failed");vm.Redo();Assert(vm.Repetitions==3&&vm.Speed==2&&vm.UntilStopped&&vm.SkipPaths&&vm.RestoreWindows,"Options redo failed");try{vm.Speed=0;throw new Exception("Invalid speed accepted");}catch(ArgumentException){};});
            Console.WriteLine($"{checks}/{checks} editor checks passed");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void RenderUi(StudioViewModel vm){
        var directory=System.IO.Path.GetFullPath("work/advanced-ui");System.IO.Directory.CreateDirectory(directory);
        vm.Replace(new(){Name="Invoice processing",Actions=[new(){Kind=ActionKind.OcrText,Group="Read invoice",Data=new(){Variable="invoiceText"}},new(){Kind=ActionKind.SetVariable,Group="Read invoice",Data=new(){Variable="total",Type=VariableType.Number,Value="42"}},new(){Kind=ActionKind.Branch,Breakpoint=true,Group="Route result",Data=new(){Value="${total}",Operation=">",Operand="0"},Flow=new(){Destination="approve"}},new(){Kind=ActionKind.Label,Group="Route result",Flow=new(){Label="approve"}},new(){Kind=ActionKind.ImageClick,Group="Route result",Detection=new(){Image="approve.png"}}]});
        System.Windows.Data.CollectionViewSource.GetDefaultView(vm.Rows).GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Section"));
        void Save(System.Windows.Window window,string name,int width,int height){var root=(System.Windows.FrameworkElement)window.Content;root.DataContext=vm;if(root is System.Windows.Controls.Panel panel)panel.Background=(System.Windows.Media.Brush)System.Windows.Application.Current.Resources["Bg"];root.Measure(new(width,height));root.Arrange(new(0,0,width,height));root.UpdateLayout();var target=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);target.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));using var file=System.IO.File.Create(System.IO.Path.Combine(directory,name+".png"));encoder.Save(file);window.Content=null;window.Close();}
        Save(new MainWindow{DataContext=vm},"main",1320,800);
        Save(new MainWindow{DataContext=vm},"main-compact",1000,700);
        Save(new ActionEditor(new(){Kind=ActionKind.ImageClick}),"action-editor",600,730);
        Save(new DetectionEditor(new(){Kind=ActionKind.ImageClick},Environment.CurrentDirectory),"visual-settings",660,760);
        Save(new DataEditor(new(){Kind=ActionKind.OcrText}),"data-settings",600,730);
        Save(new FlowEditor(new(){Kind=ActionKind.Branch},["approve","reject"]),"flow-settings",570,620);
        Save(new DebuggerWindow(vm),"debugger",780,530);
        Save(new AiConfigurationWindow(),"ai-settings",620,590);
        Console.WriteLine("Rendered real WPF view trees to "+directory);
    }
}
