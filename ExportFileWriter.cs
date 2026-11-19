using System.IO;

namespace CollageDesigner;

public static class ExportFileWriter
{
    public static async Task<string> ExportAsync(ProjectData snapshot,IReadOnlyList<LayoutTemplate> layouts,string requestedPath,IProgress<int>? progress=null)
    {
        string fullPath=Path.GetFullPath(requestedPath),extension=Path.GetExtension(fullPath).ToLowerInvariant();
        if(extension is not (".jpg" or ".jpeg" or ".png" or ".psd"))throw new InvalidDataException("Choose JPG, PNG, or PSD for export.");
        string folder=Path.GetDirectoryName(fullPath)??throw new InvalidDataException("Export folder is missing.");
        if(!Directory.Exists(folder))throw new DirectoryNotFoundException($"Export folder is missing: {folder}");
        string temporary=Path.Combine(folder,$".collage-export-{Guid.NewGuid():N}{extension}");
        try
        {
            if(extension==".psd")await Task.Run(()=>PsdExporter.Export(snapshot,layouts,temporary,progress));
            else
            {
                snapshot.JpegQuality=extension==".png"?-1:snapshot.JpegQuality;
                var bytes=await Task.Run(()=>Rendering.Render(snapshot,layouts,false,1100,progress));
                await using var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                await output.WriteAsync(bytes);
            }
            return Commit(temporary,fullPath,snapshot.Photos.Select(p=>p.Path));
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    public static string Commit(string temporaryPath,string requestedPath,IEnumerable<string>? protectedPaths=null)
    {
        string candidate=ExportNamePlanner.ResolveRequested(requestedPath,protectedPaths);
        for(int attempt=0;attempt<100000;attempt++)
        {
            try{File.Move(temporaryPath,candidate,false);return candidate;}
            catch(IOException) when(File.Exists(candidate)||Directory.Exists(candidate))
            {candidate=ExportNamePlanner.NextAvailable(candidate,protectedPaths);}
        }
        throw new IOException("Could not find an unused export filename.");
    }
}
