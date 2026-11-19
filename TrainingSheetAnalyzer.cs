using SkiaSharp;
using System.IO;

namespace CollageDesigner;

public sealed class DetectedPhotoRegion
{
    public LayoutSlot Slot {get;set;}=new();
    public string? MatchedPhotoPath {get;set;}
    public double Confidence {get;set;}
}

public sealed class SheetAnalysis
{
    public string SheetPath {get;set;}="";
    public List<DetectedPhotoRegion> Regions {get;set;}=[];
    public string Status {get;set;}="NEEDS REVIEW";
}

public static class TrainingSheetAnalyzer
{
    static double ColorDistance(SKColor a,SKColor b)
    {
        double dr=a.Red-b.Red,dg=a.Green-b.Green,db=a.Blue-b.Blue;
        return Math.Sqrt(dr*dr+dg*dg+db*db);
    }
    static SKColor Average(params SKColor[] colors)=>new((byte)colors.Average(c=>c.Red),(byte)colors.Average(c=>c.Green),(byte)colors.Average(c=>c.Blue));
    public static List<LayoutSlot> DetectRegions(SKBitmap sheet)
    {
        int w=sheet.Width,h=sheet.Height;
        if(w<30||h<30)return [];
        var corners=new[]{sheet.GetPixel(2,2),sheet.GetPixel(w-3,2),sheet.GetPixel(2,h-3),sheet.GetPixel(w-3,h-3)};
        // If the corners are photographs or a complex background, require review.
        if(corners.Any(c=>ColorDistance(c,corners[0])>28))return [new(){X=0,Y=0,Width=1,Height=1}];
        var bg=Average(corners);int step=Math.Max(1,(int)Math.Ceiling((double)Math.Max(w,h)/600));
        int sw=(w+step-1)/step,sh=(h+step-1)/step;var mask=new bool[sw*sh];
        for(int y=0;y<sh;y++)for(int x=0;x<sw;x++)mask[y*sw+x]=ColorDistance(sheet.GetPixel(Math.Min(w-1,x*step),Math.Min(h-1,y*step)),bg)>42;
        var seen=new bool[mask.Length];var regions=new List<LayoutSlot>();
        for(int start=0;start<mask.Length;start++)
        {
            if(!mask[start]||seen[start])continue;
            var queue=new Queue<int>();queue.Enqueue(start);seen[start]=true;int minX=sw,minY=sh,maxX=0,maxY=0,count=0;
            while(queue.Count>0)
            {
                int cell=queue.Dequeue(),x=cell%sw,y=cell/sw;count++;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                foreach(int next in new[]{cell-1,cell+1,cell-sw,cell+sw})
                {
                    if(next<0||next>=mask.Length||seen[next]||!mask[next])continue;
                    if(Math.Abs(next%sw-x)+Math.Abs(next/sw-y)!=1)continue;
                    seen[next]=true;queue.Enqueue(next);
                }
            }
            double boxArea=(double)(maxX-minX+1)*(maxY-minY+1)/(sw*sh);
            if(boxArea<.008||count<35)continue;
            regions.Add(new(){X=(double)minX/sw,Y=(double)minY/sh,Width=(double)(maxX-minX+1)/sw,Height=(double)(maxY-minY+1)/sh});
        }
        return regions.OrderBy(r=>r.Y).ThenBy(r=>r.X).ToList();
    }

    static float[] Feature(SKBitmap bitmap,SKRect source)
    {
        using var target=new SKBitmap(12,12);using(var canvas=new SKCanvas(target))
        {canvas.Clear(SKColors.Black);using var paint=new SKPaint{FilterQuality=SKFilterQuality.High};canvas.DrawBitmap(bitmap,source,new SKRect(0,0,12,12),paint);}
        var values=new float[144];double mean=0;
        for(int y=0;y<12;y++)for(int x=0;x<12;x++){var c=target.GetPixel(x,y);float v=(float)(.2126*c.Red+.7152*c.Green+.0722*c.Blue);values[y*12+x]=v;mean+=v;}
        mean/=values.Length;double variance=0;foreach(float v in values)variance+=(v-mean)*(v-mean);double sd=Math.Sqrt(variance/values.Length);
        for(int i=0;i<values.Length;i++)values[i]=(float)((values[i]-mean)/Math.Max(15,sd));
        return values;
    }
    static double Similarity(float[] a,float[] b)
    {
        double sum=0;for(int i=0;i<a.Length;i++){double d=a[i]-b[i];sum+=d*d;}
        return Math.Clamp(1-sum/(a.Length*3),0,1);
    }
    static double BestCropMatch(float[] target,SKBitmap photo,double aspect)
    {
        double best=0;double sourceAspect=(double)photo.Width/photo.Height;
        foreach(double scale in new[]{1.0,.8,.62,.46})
        {
            double cropW=sourceAspect>aspect?photo.Height*aspect*scale:photo.Width*scale;
            double cropH=sourceAspect>aspect?photo.Height*scale:photo.Width/aspect*scale;
            foreach(double px in new[]{0.0,.5,1.0})foreach(double py in new[]{0.0,.5,1.0})
            {
                float left=(float)((photo.Width-cropW)*px),top=(float)((photo.Height-cropH)*py);
                var feature=Feature(photo,new SKRect(left,top,left+(float)cropW,top+(float)cropH));
                best=Math.Max(best,Similarity(target,feature));
            }
        }
        return best;
    }
    public static SheetAnalysis Analyze(string sheetPath,IReadOnlyList<(string Path,SKBitmap Preview)> candidates,CancellationToken token=default)
    {
        using var sheet=Rendering.LoadUpright(sheetPath,900);var analysis=new SheetAnalysis{SheetPath=sheetPath};
        var slots=DetectRegions(sheet);
        foreach(var slot in slots)
        {
            token.ThrowIfCancellationRequested();
            var region=new DetectedPhotoRegion{Slot=slot};
            if(slot.Width>=.999&&slot.Height>=.999){analysis.Regions.Add(region);continue;}
            var rect=new SKRect((float)(slot.X*sheet.Width),(float)(slot.Y*sheet.Height),(float)((slot.X+slot.Width)*sheet.Width),(float)((slot.Y+slot.Height)*sheet.Height));
            var feature=Feature(sheet,rect);double best=0,second=0;string? bestPath=null;
            foreach(var candidate in candidates)
            {
                double score=BestCropMatch(feature,candidate.Preview,(double)rect.Width/rect.Height);
                if(score>best){second=best;best=score;bestPath=candidate.Path;}else second=Math.Max(second,score);
            }
            region.Confidence=best;
            if(best>=.90&&best-second>=.035)region.MatchedPhotoPath=bestPath;
            analysis.Regions.Add(region);
        }
        analysis.Status=analysis.Regions.Count>0&&analysis.Regions.All(r=>r.MatchedPhotoPath!=null)?"AUTO MATCHED":"NEEDS REVIEW";
        return analysis;
    }
}
