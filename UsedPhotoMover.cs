using System.IO;
namespace CollageDesigner;

public sealed record SourcePhotoSnapshot(string Path,long Length,DateTime LastWriteUtc);
public sealed record PhotoMoveResult(string SourcePath,string? DestinationPath,string? Error)
{
    public bool Succeeded=>DestinationPath!=null;
}

public static class UsedPhotoMover
{
    public static IReadOnlyList<SourcePhotoSnapshot> Capture(IEnumerable<string> paths)=>paths.Select(path=>
    {
        var fullPath=Path.GetFullPath(path);var info=new FileInfo(fullPath);
        if(!info.Exists)throw new FileNotFoundException("A source photo is missing.",fullPath);
        return new SourcePhotoSnapshot(fullPath,info.Length,info.LastWriteTimeUtc);
    }).ToList();

    public static IReadOnlyList<PhotoMoveResult> Move(IEnumerable<SourcePhotoSnapshot> photos)
    {
        var results=new List<PhotoMoveResult>();
        foreach(var photo in photos)
        {
            try{results.Add(MoveOne(photo));}
            catch(Exception ex){results.Add(new PhotoMoveResult(photo.Path,null,ex.Message));}
        }
        return results;
    }

    static PhotoMoveResult MoveOne(SourcePhotoSnapshot photo)
    {
        var source=new FileInfo(photo.Path);
        if(!source.Exists)return new(photo.Path,null,"Source photo is missing.");
        if(source.Length!=photo.Length||source.LastWriteTimeUtc!=photo.LastWriteUtc)return new(photo.Path,null,"Source photo changed during export; it was left in place.");
        var parent=source.Directory??throw new IOException("Source folder is unavailable.");
        if(parent.Name.Equals("used photos",StringComparison.OrdinalIgnoreCase))return new(photo.Path,photo.Path,null);
        string usedDirectory=Path.Combine(parent.FullName,"used photos");Directory.CreateDirectory(usedDirectory);
        string stem=Path.GetFileNameWithoutExtension(source.Name),extension=source.Extension;
        for(int suffix=0;suffix<10000;suffix++)
        {
            string fileName=suffix==0?source.Name:$"{stem}_{suffix}{extension}";
            string destination=Path.Combine(usedDirectory,fileName);
            if(File.Exists(destination)||Directory.Exists(destination))continue;
            try{File.Move(photo.Path,destination,false);return new(photo.Path,destination,null);}
            catch(IOException) when(File.Exists(destination)||Directory.Exists(destination)){continue;}
        }
        return new(photo.Path,null,"Could not find an unused filename in the used photos folder.");
    }
}
