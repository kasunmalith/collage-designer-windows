using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CollageDesigner;

public sealed class AlbumSize
{
    public int HeightInches {get;set;}
    public int WidthInches {get;set;}
    public override string ToString()=> $"{HeightInches} × {WidthInches} inches ({(double)WidthInches/HeightInches:0.##}:1)";
}

public sealed class AlbumSheet
{
    public List<int> PhotoIndices {get;set;}=[];
    public List<LayoutSlot> Slots {get;set;}=[];
    public int LayoutVariant {get;set;}
    public bool Approved {get;set;}
}

public sealed class AlbumProject
{
    public int HeightInches {get;set;}=12;
    public int WidthInches {get;set;}=30;
    public int Dpi {get;set;}=300;
    public int SheetCount {get;set;}=20;
    public double FoldSafeZoneInches {get;set;}=.5;
    public bool ShowFoldGuide {get;set;}=true;
    public int Gap {get;set;}=12;
    public int Margin {get;set;}=0;
    public string Background {get;set;}="#FFFFFFFF";
    public List<PhotoEntry> Photos {get;set;}=[];
    public List<AlbumSheet> Sheets {get;set;}=[];
    public List<int> Unassigned {get;set;}=[];
    public int PixelWidth=>WidthInches*Dpi;
    public int PixelHeight=>HeightInches*Dpi;
    public double Ratio=>(double)WidthInches/HeightInches;
}

public static class NaturalPhotoOrder
{
    static readonly Regex Parts=new(@"\d+|\D+",RegexOptions.Compiled);
    public static IEnumerable<string> Sort(IEnumerable<string> paths)=>paths.OrderBy(p=>Path.GetFileName(p),Comparer<string>.Create(Compare));
    public static int Compare(string? a,string? b)
    {
        a??="";b??="";var left=Parts.Matches(a);var right=Parts.Matches(b);
        for(int i=0;i<Math.Min(left.Count,right.Count);i++)
        {
            string x=left[i].Value,y=right[i].Value;
            bool xn=char.IsDigit(x[0]),yn=char.IsDigit(y[0]);
            int c;
            if(xn&&yn)
            {
                string xx=x.TrimStart('0'),yy=y.TrimStart('0');
                c=xx.Length.CompareTo(yy.Length);if(c==0)c=string.CompareOrdinal(xx,yy);
                if(c==0)c=x.Length.CompareTo(y.Length);
            }
            else c=string.Compare(x,y,StringComparison.OrdinalIgnoreCase);
            if(c!=0)return c;
        }
        return left.Count.CompareTo(right.Count);
    }
}

public static class AlbumEngine
{
    public static readonly AlbumSize[] Presets=[
        new(){HeightInches=8,WidthInches=16},new(){HeightInches=8,WidthInches=20},new(){HeightInches=8,WidthInches=24},
        new(){HeightInches=10,WidthInches=20},new(){HeightInches=10,WidthInches=24},new(){HeightInches=12,WidthInches=24},
        new(){HeightInches=12,WidthInches=30},new(){HeightInches=12,WidthInches=36}];

    public static AlbumProject Generate(AlbumSize size,int sheetCount,IReadOnlyList<string> photoPaths,int dpi=300,Func<double,int,double,IReadOnlyList<double>,List<LayoutSlot>?>? learnedLayout=null)
    {
        if(sheetCount is <10 or >35)throw new ArgumentOutOfRangeException(nameof(sheetCount),"Choose 10–35 sheets.");
        if(size.WidthInches<=0||size.HeightInches<=0||dpi is <72 or >600)throw new ArgumentException("Invalid album size or DPI.");
        if(photoPaths.Count<sheetCount)throw new ArgumentException("Import at least one selected photo per sheet.");
        if((long)size.WidthInches*dpi*size.HeightInches*dpi>220_000_000)throw new ArgumentException("The selected size and DPI exceed the render limit.");
        var project=new AlbumProject{WidthInches=size.WidthInches,HeightInches=size.HeightInches,SheetCount=sheetCount,Dpi=dpi,
            Photos=NaturalPhotoOrder.Sort(photoPaths).Select(p=>new PhotoEntry{Path=Path.GetFullPath(p)}).ToList()};
        // Smooth variation around the average while preserving contiguous story order.
        int total=project.Photos.Count;
        var aspects=project.Photos.Select(p=>
        {
            try{if(File.Exists(p.Path)){var s=Rendering.ReadSize(p.Path);p.Width=s.Width;p.Height=s.Height;return (double)s.Width/s.Height;}}
            catch{}
            return 1.5;
        }).ToArray();
        var weights=Enumerable.Range(0,sheetCount).Select(i=>1+.18*Math.Sin(i*1.75)-.12*Math.Cos(i*2.31)).ToArray();
        double sum=weights.Sum();var counts=weights.Select(w=>Math.Max(1,(int)Math.Floor(total*w/sum))).ToArray();
        while(counts.Sum()<total){int i=Enumerable.Range(0,sheetCount).OrderByDescending(j=>total*weights[j]/sum-counts[j]).First();counts[i]++;}
        while(counts.Sum()>total){int i=Enumerable.Range(0,sheetCount).Where(j=>counts[j]>1).OrderByDescending(j=>counts[j]-total*weights[j]/sum).First();counts[i]--;}
        int offset=0;
        for(int i=0;i<sheetCount;i++)
        {
            var sheet=new AlbumSheet{PhotoIndices=Enumerable.Range(offset,counts[i]).ToList(),LayoutVariant=i%3};
            var learned=learnedLayout?.Invoke(project.Ratio,counts[i],(double)(i+1)/sheetCount,aspects.Skip(offset).Take(counts[i]).ToArray());
            if(learned is { Count: >0 }&&learned.Count==counts[i]&&learned.All(s=>s.X>=0&&s.Y>=0&&s.Width>0&&s.Height>0&&s.X+s.Width<=1.000001&&s.Y+s.Height<=1.000001))
            {sheet.Slots=LayoutResizeEngine.Copy(learned);sheet.LayoutVariant=-1;}
            else sheet.Slots=AlbumLayouts.Build(counts[i],sheet.LayoutVariant);
            project.Sheets.Add(sheet);offset+=counts[i];
        }
        if(offset!=total||!ValidateUsage(project))throw new InvalidOperationException("Album photo assignment failed.");
        return project;
    }

    public static bool ValidateUsage(AlbumProject album)
    {
        var used=album.Sheets.SelectMany(s=>s.PhotoIndices).Concat(album.Unassigned).ToArray();
        return used.Length==album.Photos.Count&&used.Distinct().Count()==used.Length&&used.All(i=>i>=0&&i<album.Photos.Count)
            &&album.Sheets.Count==album.SheetCount&&album.Sheets.All(s=>s.PhotoIndices.Count==s.Slots.Count);
    }
    public static (int Used,int Unused,int Duplicates) Usage(AlbumProject album)
    {
        var used=album.Sheets.SelectMany(s=>s.PhotoIndices).ToArray();
        return (used.Length,album.Unassigned.Count,used.Length-used.Distinct().Count());
    }
    public static void MovePhoto(AlbumProject album,int sourceSheet,int sourceSlot,int targetSheet,int targetSlot)
    {
        var source=album.Sheets[sourceSheet];var target=album.Sheets[targetSheet];int photo=source.PhotoIndices[sourceSlot];
        source.PhotoIndices.RemoveAt(sourceSlot);
        target.PhotoIndices.Insert(Math.Clamp(targetSlot,0,target.PhotoIndices.Count),photo);
        Reflow(source);if(source!=target)Reflow(target);
    }
    public static void SwapPhotos(AlbumProject album,int firstSheet,int firstSlot,int secondSheet,int secondSlot)
    {
        var a=album.Sheets[firstSheet];var b=album.Sheets[secondSheet];
        (a.PhotoIndices[firstSlot],b.PhotoIndices[secondSlot])=(b.PhotoIndices[secondSlot],a.PhotoIndices[firstSlot]);
        a.Approved=false;b.Approved=false;
    }
    public static void Reflow(AlbumSheet sheet){if(sheet.LayoutVariant<0)sheet.LayoutVariant=0;sheet.Slots=AlbumLayouts.Build(sheet.PhotoIndices.Count,sheet.LayoutVariant);sheet.Approved=false;}
    public static void Save(AlbumProject project,string path)
    {
        if(!ValidateUsage(project))throw new InvalidDataException("Album has missing or duplicate photo assignments.");
        File.WriteAllText(path,JsonSerializer.Serialize(project,new JsonSerializerOptions{WriteIndented=true}));
    }
    public static AlbumProject Load(string path)
    {
        var project=JsonSerializer.Deserialize<AlbumProject>(File.ReadAllText(path))??throw new InvalidDataException("Empty album project.");
        if(!ValidateUsage(project))throw new InvalidDataException("Album contains missing or duplicate photo assignments.");
        return project;
    }
}

public static class AlbumLayouts
{
    public static List<LayoutSlot> Build(int count,int variant)
    {
        if(count==0)return [];
        if(count==1)return [new(){X=0,Y=0,Width=1,Height=1}];
        variant=((variant%3)+3)%3;
        if(variant==1&&count>=3)
        {
            var slots=new List<LayoutSlot>{new(){X=0,Y=0,Width=.55,Height=1}};
            int others=count-1,cols=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(others*.55))),heroRows=(int)Math.Ceiling((double)others/cols);
            for(int i=0;i<others;i++)slots.Add(new(){X=.55+.45*(i%cols)/cols,Y=(double)(i/cols)/heroRows,Width=.45/cols,Height=1.0/heroRows});
            return slots;
        }
        int columns=variant==2?Math.Max(1,(int)Math.Ceiling(Math.Sqrt(count*2.2))):Math.Max(1,(int)Math.Ceiling(Math.Sqrt(count*2.5)));
        int rows=(int)Math.Ceiling((double)count/columns);
        var result=new List<LayoutSlot>();
        for(int row=0;row<rows;row++)
        {
            int used=Math.Min(columns,count-row*columns);
            for(int col=0;col<used;col++)result.Add(new(){X=(double)col/used,Y=(double)row/rows,Width=1.0/used,Height=1.0/rows});
        }
        return result;
    }
}
