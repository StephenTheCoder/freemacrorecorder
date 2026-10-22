using System.Text.Json;
namespace MacroStudio.Core;
public sealed class LibraryPreference {public bool Favorite {get;set;}public string Hotkey {get;set;}="";}
public sealed record LibraryEntry(string Path,string RelativePath,string Name,bool Favorite,string Hotkey){public string Display=>$"{(Favorite?"* ":"")}{Name} / {RelativePath}{(string.IsNullOrEmpty(Hotkey)?"":" ["+Hotkey+"]")}";}
public sealed class LocalLibrary
{
    public string Root {get;}
    readonly string indexFile;
    public Dictionary<string,LibraryPreference> Preferences {get;private set;}=new(StringComparer.OrdinalIgnoreCase);
    public LocalLibrary(string? directory=null){var home=directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacroStudio");Root=Path.Combine(home,"Library");indexFile=Path.Combine(home,"library-index.json");Directory.CreateDirectory(Root);try{Preferences=new(JsonSerializer.Deserialize<Dictionary<string,LibraryPreference>>(File.ReadAllText(indexFile))??[],StringComparer.OrdinalIgnoreCase);}catch(JsonException){}catch(IOException){}}
    public IReadOnlyList<LibraryEntry> Search(string query="",bool favoritesOnly=false){var list=new List<LibraryEntry>();foreach(var path in Directory.EnumerateFiles(Root,"*.json",SearchOption.AllDirectories).Take(5000)){try{var doc=MacroStore.Parse(File.ReadAllText(path));var relative=Path.GetRelativePath(Root,path);var pref=Preferences.GetValueOrDefault(relative)??new();if((!favoritesOnly||pref.Favorite)&&$"{doc.Name} {relative}".Contains(query,StringComparison.OrdinalIgnoreCase))list.Add(new(path,relative,doc.Name,pref.Favorite,pref.Hotkey));}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException){}}return list.OrderByDescending(e=>e.Favorite).ThenBy(e=>e.RelativePath,StringComparer.OrdinalIgnoreCase).ToList();}
    public void SavePreference(string relative,bool favorite,string hotkey){relative=Path.GetRelativePath(Root,Resolve(relative));Preferences[relative]=new(){Favorite=favorite,Hotkey=hotkey};var temp=indexFile+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(Preferences));File.Move(temp,indexFile,true);}
    public string Resolve(string relative){var full=Path.GetFullPath(relative,Root);if(!full.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Library path must stay in the library folder.");return full;}
    public void CreateFolder(string relative)=>Directory.CreateDirectory(Resolve(relative));
}
