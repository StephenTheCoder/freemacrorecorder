using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MacroStudio.Core;
public static class PortablePackage
{
    const long MaxBytes=256L*1024*1024;
    public static async Task ExportAsync(string package,MacroDocument document,string directory,CancellationToken token=default){
        await MacroProgram.BuildAsync(document.Actions,directory,token:token);
        var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);var macros=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var chain=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes=0;
        void Add(string name,byte[] content){if(files.ContainsKey(name))return;bytes+=content.Length;if(bytes>MaxBytes||files.Count>=2000)throw new InvalidDataException("Portable package exceeds 256 MB or 2000 files.");files.Add(name,content);}
        string Relative(string from,string to)=>Path.GetRelativePath(string.IsNullOrEmpty(Path.GetDirectoryName(from))?".":Path.GetDirectoryName(from)!,to).Replace('\\','/');
        async Task<string> Asset(string path,string folder,string from){
            if(path.Contains("${"))return path;
            var full=Path.GetFullPath(path,folder);if(new FileInfo(full).Length>100*1024*1024)throw new InvalidDataException("Package asset exceeds 100 MB.");var content=await File.ReadAllBytesAsync(full,token);var ext=Path.GetExtension(full).ToLowerInvariant();if(ext.Length>8||ext.Any(c=>c!='.'&&!char.IsAsciiLetterOrDigit(c)))ext=".bin";var name="assets/"+Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()+ext;Add(name,content);return Relative(from,name);
        }
        async Task Write(MacroDocument source,string folder,string name,int depth){
            token.ThrowIfCancellationRequested();if(depth>12)throw new InvalidDataException("Macro package call depth exceeds 12.");var doc=MacroStore.Parse(MacroStore.Serialize(source));doc.FormatVersion=2;
            foreach(var a in doc.Actions){
                if(!string.IsNullOrWhiteSpace(a.Detection.Image))a.Detection.Image=await Asset(a.Detection.Image,folder,name);
                if(a.Kind==ActionKind.SetVariable&&a.Data.Type==VariableType.Image&&!string.IsNullOrWhiteSpace(a.Data.Value))a.Data.Value=await Asset(a.Data.Value,folder,name);
                if(a.Kind is ActionKind.ReadText or ActionKind.ReadCsv or ActionKind.AiImage&&!a.Data.File.Contains("${"))a.Data.File=await Asset(a.Data.File,folder,name);
                if(a.Kind==ActionKind.CallMacro){var full=Path.GetFullPath(a.Flow.MacroFile,folder);if(!chain.Add(full))throw new InvalidDataException("Recursive macro package call.");if(!macros.TryGetValue(full,out var child)){child=$"macros/{macros.Count+1:0000}.json";macros[full]=child;await Write(await MacroStore.LoadAsync(full),Path.GetDirectoryName(full)!,child,depth+1);}chain.Remove(full);a.Flow.MacroFile=Relative(name,child);}
            }
            Add(name,Encoding.UTF8.GetBytes(MacroStore.Serialize(doc)));
        }
        await Write(document,Path.GetFullPath(directory),"macro.json",0);
        var temp=Path.GetFullPath(package)+"."+Guid.NewGuid()+".tmp";
        try{using(var stream=File.Create(temp))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create)){foreach(var(name,content)in files.OrderBy(f=>f.Key)){var entry=zip.CreateEntry(name,CompressionLevel.Optimal);await using var target=entry.Open();await target.WriteAsync(content,token);}}File.Move(temp,package,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static async Task<string> ImportAsync(string package,string destination,CancellationToken token=default){
        destination=Path.GetFullPath(destination);if(Directory.Exists(destination)||File.Exists(destination))throw new InvalidDataException("Import destination must be a new folder.");
        using var archive=ZipFile.OpenRead(package);if(archive.Entries.Count>2000)throw new InvalidDataException("Package exceeds 2000 files.");var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long bytes=0;
        foreach(var entry in archive.Entries){var name=entry.FullName;if(name.Length>240||name.Contains('\\')||name.Contains(':')||name.StartsWith('/')||name.Split('/').Any(p=>p is ".." or "." or "")||!names.Add(name)||entry.Length>100*1024*1024||(entry.ExternalAttributes>>16&0xF000)==0xA000)throw new InvalidDataException("Unsafe or duplicate portable package entry: "+name);bytes+=entry.Length;if(bytes>MaxBytes)throw new InvalidDataException("Package exceeds 256 MB expanded size.");}
        if(!names.Contains("macro.json"))throw new InvalidDataException("Package needs macro.json.");
        // Validate every JSON document and require asset/call paths to stay inside and exist in the archive.
        foreach(var entry in archive.Entries.Where(e=>e.FullName=="macro.json"||(e.FullName.StartsWith("macros/",StringComparison.OrdinalIgnoreCase)&&e.FullName.EndsWith(".json",StringComparison.OrdinalIgnoreCase)))){using var reader=new StreamReader(entry.Open());var doc=MacroStore.Parse(await reader.ReadToEndAsync(token));foreach(var a in doc.Actions){var refs=new List<string>();if(!string.IsNullOrWhiteSpace(a.Detection.Image))refs.Add(a.Detection.Image);if(a.Kind==ActionKind.CallMacro)refs.Add(a.Flow.MacroFile);if(a.Kind==ActionKind.SetVariable&&a.Data.Type==VariableType.Image)refs.Add(a.Data.Value);if(a.Kind is ActionKind.ReadText or ActionKind.ReadCsv or ActionKind.AiImage)refs.Add(a.Data.File);foreach(var path in refs.Where(p=>!p.Contains("${"))){var full=Path.GetFullPath(path,Path.Combine(destination,Path.GetDirectoryName(entry.FullName)??""));if(!full.StartsWith(destination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!names.Contains(Path.GetRelativePath(destination,full).Replace('\\','/')))throw new InvalidDataException("Package reference escapes the package or is missing: "+path);}}}
        Directory.CreateDirectory(destination);
        foreach(var entry in archive.Entries){token.ThrowIfCancellationRequested();var path=Path.Combine(destination,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write);await using var input=entry.Open();await input.CopyToAsync(output,token);}
        var root=Path.Combine(destination,"macro.json");await MacroProgram.BuildAsync((await MacroStore.LoadAsync(root)).Actions,destination,root,token);return root;
    }
}
