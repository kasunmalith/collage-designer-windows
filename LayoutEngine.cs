namespace CollageDesigner;
public static class LayoutEngine
{
    public static IReadOnlyList<LayoutTemplate> ForCount(int n,string collection)=>collection=="RandomSpace"?RandomSpaceLayoutEngine.ForCount(n):ForCount(n);
    static LayoutSlot S(double x,double y,double w,double h)=>new(){X=x,Y=y,Width=w,Height=h};
    static LayoutTemplate T(int n,string name,params LayoutSlot[] slots)=>new(){Id=$"{n}-{name.ToLowerInvariant().Replace(' ','-')}",Name=name,PhotoCount=n,Slots=slots.ToList()};
    static List<LayoutSlot> Grid(int n,int cols,bool reverse=false)
    {
        int rows=(int)Math.Ceiling((double)n/cols); var a=new List<LayoutSlot>();
        for(int i=0;i<n;i++){int j=reverse?n-1-i:i; int row=j/cols,col=j%cols; int onRow=Math.Min(cols,n-row*cols); a.Add(S((double)col/onRow,(double)row/rows,1.0/onRow,1.0/rows));} return a;
    }
    static List<LayoutSlot> Feature(int n,bool vertical,bool after,double ratio,int featureCount=1)
    {
        var a=new List<LayoutSlot>(); int rest=n-featureCount;
        if(vertical){for(int i=0;i<featureCount;i++)a.Add(S(after?1-ratio:0,(double)i/featureCount,ratio,1.0/featureCount)); for(int i=0;i<rest;i++)a.Add(S(after?0:ratio,(double)i/rest,1-ratio,1.0/rest));}
        else {for(int i=0;i<featureCount;i++)a.Add(S((double)i/featureCount,after?1-ratio:0,1.0/featureCount,ratio)); for(int i=0;i<rest;i++)a.Add(S((double)i/rest,after?0:ratio,1.0/rest,1-ratio));} return a;
    }
    static List<LayoutSlot> Mosaic(int n,int cols,bool featureLeft,double featureRatio)
    {
        var a=new List<LayoutSlot>{S(featureLeft?0:1-featureRatio,0,featureRatio,1)}; var g=Grid(n-1,cols);
        foreach(var s in g)a.Add(S((featureLeft?featureRatio:0)+s.X*(1-featureRatio),s.Y,s.Width*(1-featureRatio),s.Height)); return a;
    }
    public static IReadOnlyList<LayoutTemplate> ForCount(int n)
    {
        if(n<1||n>12)return [];
        var a=new List<LayoutTemplate>();
        void Add(string name,IEnumerable<LayoutSlot> slots)=>a.Add(T(n,name,slots.ToArray()));
        if(n==1){Add("Full canvas",[S(0,0,1,1)]);return a;}
        if(n==2){foreach(bool vertical in new[]{true,false})foreach(double ratio in new[]{.5,.7,.3})Add($"{(vertical?"Side by side":"Stacked")} {ratio:0%}",Feature(n,vertical,false,ratio));return a;}
        if(n==3){Add("Three columns",Grid(n,3));Add("Three rows",Feature(n,false,false,1.0/3,1)); foreach(bool vertical in new[]{true,false})foreach(bool after in new[]{false,true})foreach(double ratio in new[]{.55,.67})Add($"Feature {(vertical?(after?"right":"left"):(after?"bottom":"top"))} {ratio:0%}",Feature(n,vertical,after,ratio));return a;}
        if(n==4){Add("Classic grid",Grid(n,2));Add("Four columns",Grid(n,4)); foreach(bool vertical in new[]{true,false})foreach(bool after in new[]{false,true})foreach(double ratio in new[]{.52,.65})Add($"Editorial {(vertical?(after?"right":"left"):(after?"bottom":"top"))} {ratio:0%}",Feature(n,vertical,after,ratio));return a;}
        int wanted=n<=6?8:n<=8?6:n<=10?5:4;
        var variants=new List<(string,List<LayoutSlot>)>{("Balanced grid",Grid(n,(int)Math.Ceiling(Math.Sqrt(n)))),("Wide rhythm",Grid(n,3)),("Vertical rhythm",Grid(n,2)),("Reversed rhythm",Grid(n,3,true)),("Feature left",Mosaic(n,2,true,.45)),("Feature right",Mosaic(n,2,false,.45)),("Tall feature left",Mosaic(n,3,true,.38)),("Tall feature right",Mosaic(n,3,false,.38))};
        foreach(var (name,slots) in variants.Take(wanted))Add(name,slots);return a;
    }
}
