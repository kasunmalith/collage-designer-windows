using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CollageDesigner;

public static class ExportNamePlanner
{
    static readonly Regex NumberedName=new("^(.*)_(\\d+)$",RegexOptions.Compiled);

    public static string ResolveRequested(string requestedPath,IEnumerable<string>? protectedPaths=null)
    {
        string path=Path.GetFullPath(requestedPath);
        var protectedSet=new HashSet<string>((protectedPaths??[]).Select(Path.GetFullPath),StringComparer.OrdinalIgnoreCase);
        return Unavailable(path,protectedSet)?NextAvailable(path,protectedSet):path;
    }

    public static string NextAvailable(string previousPath,IEnumerable<string>? protectedPaths=null)
        =>NextAvailable(Path.GetFullPath(previousPath),new HashSet<string>((protectedPaths??[]).Select(Path.GetFullPath),StringComparer.OrdinalIgnoreCase));

    static string NextAvailable(string previousPath,HashSet<string> protectedSet)
    {
        string folder=Path.GetDirectoryName(previousPath)??throw new InvalidDataException("Export folder is missing.");
        string stem=Path.GetFileNameWithoutExtension(previousPath),extension=Path.GetExtension(previousPath);
        if(string.IsNullOrWhiteSpace(stem)||string.IsNullOrWhiteSpace(extension))throw new InvalidDataException("Export filename is invalid.");
        var match=NumberedName.Match(stem);
        string baseName=stem;BigInteger number=1;int pad=0;bool plainNumber=false;
        if(BigInteger.TryParse(stem,NumberStyles.None,CultureInfo.InvariantCulture,out var parsed))
        {number=parsed;pad=stem.Length;plainNumber=true;}
        else if(match.Success&&!string.IsNullOrWhiteSpace(match.Groups[1].Value)&&BigInteger.TryParse(match.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out parsed))
        {baseName=match.Groups[1].Value;number=parsed;pad=match.Groups[2].Length;}
        for(int attempt=0;attempt<100000;attempt++)
        {
            number++;
            string digits=number.ToString(CultureInfo.InvariantCulture).PadLeft(pad,'0');
            string candidate=Path.Combine(folder,(plainNumber?digits:$"{baseName}_{digits}")+extension);
            if(!Unavailable(candidate,protectedSet))return candidate;
        }
        throw new IOException("Could not find an unused export filename.");
    }

    static bool Unavailable(string path,HashSet<string> protectedSet)=>protectedSet.Contains(path)||File.Exists(path)||Directory.Exists(path);
}
