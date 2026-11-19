using System.Text.Json;
using System.IO;

namespace CollageDesigner;

public sealed class CanvasSettings
{
    public string Unit {get;set;}="Pixels";
    public double Width {get;set;}=2160;
    public double Height {get;set;}=2700;
    public int Dpi {get;set;}=300;
}

public static class CanvasSettingsStore
{
    public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Energetic Edge","Collage Designer","canvas-settings.json");

    public static CanvasSettings? Load(string? path=null)
    {
        try
        {
            path??=DefaultPath;
            if(!File.Exists(path))return null;
            var settings=JsonSerializer.Deserialize<CanvasSettings>(File.ReadAllText(path));
            return settings is not null&&IsValid(settings)?settings:null;
        }
        catch{return null;}
    }

    public static bool IsValid(CanvasSettings settings)
    {
        if(settings.Unit is not ("Pixels" or "Inches")||!double.IsFinite(settings.Width)||!double.IsFinite(settings.Height)||settings.Width<=0||settings.Height<=0||settings.Dpi is <1 or >1200)return false;
        double factor=settings.Unit=="Inches"?settings.Dpi:1;
        double width=settings.Width*factor,height=settings.Height*factor;
        return width>=1&&height>=1&&width<=30000&&height<=30000&&width*height<=220_000_000;
    }

    public static void Save(ProjectData data,string? path=null)
    {
        var settings=new CanvasSettings{Unit=data.Unit,Width=data.Width,Height=data.Height,Dpi=data.Dpi};
        if(!IsValid(settings))throw new InvalidDataException("Invalid canvas settings.");
        path??=DefaultPath;
        string? folder=Path.GetDirectoryName(path);
        if(!string.IsNullOrEmpty(folder))Directory.CreateDirectory(folder);
        string temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(settings));
        File.Move(temporary,path,true);
    }
}
