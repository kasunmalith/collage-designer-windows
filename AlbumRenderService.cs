using System.IO;
namespace CollageDesigner;

public static class AlbumRenderService
{
    public static (ProjectData Data,IReadOnlyList<LayoutTemplate> Layouts) Build(AlbumProject album,int sheetIndex)
    {
        var sheet=album.Sheets[sheetIndex];
        var data=new ProjectData{Unit="Inches",Width=album.WidthInches,Height=album.HeightInches,Dpi=album.Dpi,
            Gap=album.Gap,Margin=album.Margin,Background=album.Background,JpegQuality=95,
            Photos=sheet.PhotoIndices.Select(i=>album.Photos[i]).ToList(),Assignments=Enumerable.Range(0,sheet.PhotoIndices.Count).ToList()};
        var template=new LayoutTemplate{Id="album-sheet",Name=$"Sheet {sheetIndex+1}",PhotoCount=sheet.PhotoIndices.Count,Slots=LayoutResizeEngine.Copy(sheet.Slots)};
        return (data,new[]{template});
    }

    public static async Task<IReadOnlyList<string>> ExportAll(AlbumProject album,string folder,IProgress<int>? progress=null,CancellationToken cancellationToken=default)
    {
        if(!AlbumEngine.ValidateUsage(album)||album.Unassigned.Count!=0)throw new InvalidOperationException("Assign every selected photo before export.");
        if(album.Photos.Any(p=>!File.Exists(p.Path)))throw new FileNotFoundException("Some original photos are missing.");
        Directory.CreateDirectory(folder);
        if(Enumerable.Range(1,album.Sheets.Count).Any(i=>File.Exists(Path.Combine(folder,$"{i:D3}.jpg"))))
        {
            int version=2;string candidate;
            do{candidate=Path.Combine(folder,$"Album Export {version++}");}while(Directory.Exists(candidate));
            folder=candidate;Directory.CreateDirectory(folder);
        }
        var outputs=new List<string>();
        for(int i=0;i<album.Sheets.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (data,layouts)=Build(album,i);
            string target=Path.Combine(folder,$"{i+1:D3}.jpg");
            outputs.Add(await ExportFileWriter.ExportAsync(data,layouts,target));
            progress?.Report((i+1)*100/album.Sheets.Count);
        }
        return outputs;
    }
}
