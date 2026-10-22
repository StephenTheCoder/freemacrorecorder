using System.Runtime.InteropServices;
using System.Text;

namespace MacroStudio.Windows;
public static class Native
{
    public const ulong InjectionTag = 0x4D535455;
    public delegate nint HookProc(int code, nint message, nint data);
    public delegate void WinEventProc(nint hook, uint evt, nint hwnd, int obj, int child, uint thread, uint time);
    public delegate bool EnumProc(nint hwnd,nint param);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; public Point(int x,int y) { X=x;Y=y; } }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseHook { public Point Point; public uint Data,Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyHook { public uint Vk,Scan,Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct Message { public nint Hwnd; public uint Msg; public nuint WParam; public nint LParam; public uint Time; public Point Pt; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int X,Y; public uint Data,Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyInput { public ushort Vk,Scan; public uint Flags,Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError=true)] public static extern nint SetWindowsHookEx(int id,HookProc proc,nint module,uint thread);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern int GetMessage(out Message msg,nint hwnd,uint min,uint max);
    [DllImport("user32.dll")] public static extern bool PeekMessage(out Message msg,nint hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref Message msg);
    [DllImport("user32.dll")] public static extern nint DispatchMessage(ref Message msg);
    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint thread,uint message,nuint w,nint l);
    [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd,uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(nint hwnd,StringBuilder text,int count);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc,nint param);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd,int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int metric);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern nint SetThreadDpiAwarenessContext(nint value);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool SetProcessDpiAwarenessContext(nint value);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SendMessageW")] public static extern nint SendMessage(nint hwnd,uint message,nint w,nint l);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool RegisterHotKey(nint hwnd,int id,uint mods,uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd,int id);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min,uint max,nint module,WinEventProc proc,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);
}
