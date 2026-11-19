using System.IO;
using System.Text.Json;

namespace CollageDesigner;

public sealed class ExportSettings
{
    public string? LastExportPath {get;set;}
    public bool QuickSaveEnabled {get;set;}
}

public static class ExportSettingsStore
{
    public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Energetic Edge","Collage Designer","export-settings.json");

    public static ExportSettings Load(string? path=null)
    {
        try{return JsonSerializer.Deserialize<ExportSettings>(File.ReadAllText(path??DefaultPath))??new ExportSettings();}
        catch{return new ExportSettings();}
    }

    public static void Save(ExportSettings settings,string? path=null)
    {
        path??=DefaultPath;string? folder=Path.GetDirectoryName(path);
        if(!string.IsNullOrEmpty(folder))Directory.CreateDirectory(folder);
        string temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(settings));
        File.Move(temporary,path,true);
    }
}
