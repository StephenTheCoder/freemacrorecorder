using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using MacroStudio.Core;

namespace MacroStudio.Windows;
public static class WindowsCredentials
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Credential{public uint Flags,Type;public string TargetName,Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;public uint BlobSize;public nint Blob;public uint Persist,AttributeCount;public nint Attributes;public string TargetAlias,UserName;}
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Write(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Read(string target,uint type,uint flags,out nint credential);
    [DllImport("advapi32.dll")]static extern void CredFree(nint credential);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Delete(string target,uint type,uint flags);
    public const string Target="MacroStudio/AI/Default";
    public static void Save(string secret,string target=Target){var bytes=Encoding.Unicode.GetBytes(secret);if(bytes.Length>2560)throw new InvalidDataException("Credential exceeds Windows storage limit.");var memory=Marshal.AllocHGlobal(bytes.Length);try{Marshal.Copy(bytes,0,memory,bytes.Length);var credential=new Credential{Type=1,TargetName=target,Comment="MacroStudio opt-in automation provider",BlobSize=(uint)bytes.Length,Blob=memory,Persist=2,UserName=Environment.UserName,TargetAlias=""};if(!Write(ref credential,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not store provider credential.");}finally{for(int i=0;i<bytes.Length;i++)Marshal.WriteByte(memory,i,0);Marshal.FreeHGlobal(memory);Array.Clear(bytes);}}
    public static string? Load(string target=Target){if(!Read(target,1,0,out var pointer)){int error=Marshal.GetLastWin32Error();if(error==1168)return null;throw new Win32Exception(error,"Could not read provider credential.");}try{var c=Marshal.PtrToStructure<Credential>(pointer);return Marshal.PtrToStringUni(c.Blob,(int)c.BlobSize/2);}finally{CredFree(pointer);}}
    public static void Remove(string target=Target){if(!Delete(target,1,0)&&Marshal.GetLastWin32Error()!=1168)throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not remove provider credential.");}
}
public static class AiSettings
{
    static readonly HttpClient client=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
    static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacroStudio","ai-provider.json");
    public static AiConfiguration Load(){try{return JsonSerializer.Deserialize<AiConfiguration>(File.ReadAllText(FilePath))??new();}catch(IOException){return new();}catch(JsonException){return new();}}
    public static void Save(AiConfiguration config){if(config.Enabled)config.Validate();Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);File.WriteAllText(FilePath+".tmp",JsonSerializer.Serialize(config));File.Move(FilePath+".tmp",FilePath,true);}
    public static IAiProvider Create(){var config=Load();return new JsonChatProvider(client,config,config.Enabled&&config.UseCredential?WindowsCredentials.Load():null);}
}
