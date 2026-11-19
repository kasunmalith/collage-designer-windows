using System.Text.Json;
using System.IO;
namespace CollageDesigner;
public sealed class EditorViewModel
{
    static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    public ProjectData Data {get;private set;}=new();
    public string? ProjectPath {get;set;}
    public int SelectedSlot {get;set;}=-1;
    public IReadOnlyList<LayoutTemplate> Layouts=>LayoutEngine.ForCount(Data.Photos.Count,Data.LayoutCollection);
    public IReadOnlyList<LayoutSlot> CurrentSlots=>Layouts.Count==0?[]:LayoutResizeEngine.Slots(Data,Layouts[Data.LayoutIndex]);
    readonly Stack<string> undo=new(),redo=new();
    public bool CanUndo=>undo.Count>0;public bool CanRedo=>redo.Count>0;
    public void Remember(){undo.Push(JsonSerializer.Serialize(Data,Options));if(undo.Count>100){var recent=undo.Take(100).Reverse().ToArray();undo.Clear();foreach(var item in recent)undo.Push(item);}redo.Clear();}
    public bool Undo(){if(!CanUndo)return false;redo.Push(JsonSerializer.Serialize(Data,Options));Data=JsonSerializer.Deserialize<ProjectData>(undo.Pop())!;return true;}
    public bool Redo(){if(!CanRedo)return false;undo.Push(JsonSerializer.Serialize(Data,Options));Data=JsonSerializer.Deserialize<ProjectData>(redo.Pop())!;return true;}
    public void SetData(ProjectData data){Data=data;undo.Clear();redo.Clear();SelectedSlot=-1;Normalize();}
    public void Normalize(){if(Data.LayoutCollection!="RandomSpace")Data.LayoutCollection="Normal";Data.LayoutIndex=Math.Clamp(Data.LayoutIndex,0,Math.Max(0,Layouts.Count-1));while(Data.Assignments.Count<Data.Photos.Count)Data.Assignments.Add(Data.Assignments.Count);if(Data.Assignments.Count>Data.Photos.Count)Data.Assignments.RemoveRange(Data.Photos.Count,Data.Assignments.Count-Data.Photos.Count);}
    public PhotoEntry? SelectedPhoto=>SelectedSlot>=0&&SelectedSlot<Data.Assignments.Count&&Data.Assignments[SelectedSlot]>=0&&Data.Assignments[SelectedSlot]<Data.Photos.Count?Data.Photos[Data.Assignments[SelectedSlot]]:null;
    public void ClearPhotos(){if(Data.Photos.Count==0)return;Remember();Data.Photos.Clear();Data.Assignments.Clear();Data.CustomLayouts.Clear();Data.LayerOrder.Clear();Data.LayoutIndex=0;SelectedSlot=-1;}
    public void RemoveMovedPhotos(IEnumerable<string> movedPaths)
    {
        var moved=new HashSet<string>(movedPaths,StringComparer.OrdinalIgnoreCase);if(moved.Count==0)return;
        var oldToNew=new Dictionary<int,int>();var remaining=new List<PhotoEntry>();
        for(int i=0;i<Data.Photos.Count;i++)if(!moved.Contains(Data.Photos[i].Path)){oldToNew[i]=remaining.Count;remaining.Add(Data.Photos[i]);}
        Data.Assignments=Data.Assignments.Where(oldToNew.ContainsKey).Select(i=>oldToNew[i]).ToList();Data.Photos=remaining;
        Data.CustomLayouts.Clear();Data.LayerOrder.Clear();Data.LayoutIndex=0;SelectedSlot=-1;
        // Undo must not resurrect paths that have physically moved on disk.
        undo.Clear();redo.Clear();
    }
    public void Save(string path){File.WriteAllText(path,JsonSerializer.Serialize(Data,Options));ProjectPath=path;}
    public static ProjectData Load(string path)=>JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(path))??throw new InvalidDataException("Project file is empty.");
}
