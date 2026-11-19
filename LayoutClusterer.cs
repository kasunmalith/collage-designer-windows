namespace CollageDesigner;

public sealed class LearnedLayoutSample
{
    public double Ratio {get;set;}
    public double Position {get;set;}
    public List<LayoutSlot> Slots {get;set;}=[];
    public int UsageCount {get;set;}=1;
}

public static class LayoutClusterer
{
    static double Distance(IReadOnlyList<LayoutSlot> a,IReadOnlyList<LayoutSlot> b)
    {
        if(a.Count!=b.Count)return double.PositiveInfinity;
        double sum=0;for(int i=0;i<a.Count;i++)sum+=Math.Abs(a[i].X-b[i].X)+Math.Abs(a[i].Y-b[i].Y)+Math.Abs(a[i].Width-b[i].Width)+Math.Abs(a[i].Height-b[i].Height);
        return sum/(a.Count*4);
    }
    public static List<LearnedLayoutSample> Cluster(IEnumerable<LearnedLayoutSample> samples,double tolerance=.035)
    {
        var clusters=new List<LearnedLayoutSample>();
        foreach(var sample in samples)
        {
            if(sample.Slots.Count==0)continue;
            var nearest=clusters.Where(c=>c.Slots.Count==sample.Slots.Count&&Math.Abs(c.Ratio-sample.Ratio)<.035)
                .Select(c=>(Cluster:c,Distance:Distance(c.Slots,sample.Slots))).OrderBy(x=>x.Distance).FirstOrDefault();
            if(nearest.Cluster==null||nearest.Distance>tolerance)
            {clusters.Add(new(){Ratio=sample.Ratio,Position=sample.Position,Slots=LayoutResizeEngine.Copy(sample.Slots),UsageCount=sample.UsageCount});continue;}
            var cluster=nearest.Cluster;int total=cluster.UsageCount+sample.UsageCount;
            cluster.Position=(cluster.Position*cluster.UsageCount+sample.Position*sample.UsageCount)/total;
            for(int i=0;i<cluster.Slots.Count;i++)
            {
                cluster.Slots[i].X=(cluster.Slots[i].X*cluster.UsageCount+sample.Slots[i].X*sample.UsageCount)/total;
                cluster.Slots[i].Y=(cluster.Slots[i].Y*cluster.UsageCount+sample.Slots[i].Y*sample.UsageCount)/total;
                cluster.Slots[i].Width=(cluster.Slots[i].Width*cluster.UsageCount+sample.Slots[i].Width*sample.UsageCount)/total;
                cluster.Slots[i].Height=(cluster.Slots[i].Height*cluster.UsageCount+sample.Slots[i].Height*sample.UsageCount)/total;
            }
            cluster.UsageCount=total;
        }
        return clusters;
    }
}
