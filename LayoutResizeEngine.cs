namespace CollageDesigner;

public sealed class LayoutDivider
{
    public bool Vertical {get;init;}
    public double Position {get;init;}
    public double From {get;init;}
    public double To {get;init;}
    public List<int> Before {get;init;}=[];
    public List<int> After {get;init;}=[];
}

public static class LayoutResizeEngine
{
    const double Epsilon=0.000001;
    public static List<LayoutSlot> Copy(IReadOnlyList<LayoutSlot> slots)=>slots.Select(s=>new LayoutSlot{X=s.X,Y=s.Y,Width=s.Width,Height=s.Height}).ToList();
    public static IReadOnlyList<LayoutSlot> Slots(ProjectData data,LayoutTemplate template)=>data.CustomLayouts.TryGetValue(template.Id,out var saved)&&saved.Count==template.Slots.Count?saved:template.Slots;
    static double Start(LayoutSlot s,bool vertical)=>vertical?s.Y:s.X;
    static double End(LayoutSlot s,bool vertical)=>vertical?s.Y+s.Height:s.X+s.Width;
    static double EdgeBefore(LayoutSlot s,bool vertical)=>vertical?s.X+s.Width:s.Y+s.Height;
    static double EdgeAfter(LayoutSlot s,bool vertical)=>vertical?s.X:s.Y;
    static bool Overlaps(double a,double b,double c,double d)=>a<d-Epsilon&&c<b-Epsilon;
    static bool Contains(double a,double b,double point)=>point>=a-Epsilon&&point<=b+Epsilon;

    public static LayoutDivider? Find(IReadOnlyList<LayoutSlot> slots,double x,double y,double toleranceX,double toleranceY)
    {
        LayoutDivider? best=null;double bestDistance=double.MaxValue;
        foreach(bool vertical in new[]{true,false})
        {
            double along=vertical?x:y,across=vertical?y:x,tolerance=vertical?toleranceX:toleranceY;
            foreach(double line in slots.SelectMany(s=>new[]{EdgeBefore(s,vertical),EdgeAfter(s,vertical)}).Distinct())
            {
                double distance=Math.Abs(along-line);if(distance>tolerance||distance>=bestDistance)continue;
                var before=Enumerable.Range(0,slots.Count).Where(i=>Math.Abs(EdgeBefore(slots[i],vertical)-line)<Epsilon).ToList();
                var after=Enumerable.Range(0,slots.Count).Where(i=>Math.Abs(EdgeAfter(slots[i],vertical)-line)<Epsilon).ToList();
                if(before.Count==0&&after.Count==0)continue;
                var chosenBefore=new HashSet<int>();var chosenAfter=new HashSet<int>();
                // Seed the frame edge under the pointer, then include any neighboring
                // edge with a positive span overlap. Touching corners stay independent.
                foreach(int i in before.Where(i=>Contains(Start(slots[i],vertical),End(slots[i],vertical),across)))chosenBefore.Add(i);
                foreach(int i in after.Where(i=>Contains(Start(slots[i],vertical),End(slots[i],vertical),across)))chosenAfter.Add(i);
                if(chosenBefore.Count==0&&chosenAfter.Count==0)continue;
                double from=Math.Min(chosenBefore.Concat(chosenAfter).Min(i=>Start(slots[i],vertical)),across);
                double to=Math.Max(chosenBefore.Concat(chosenAfter).Max(i=>End(slots[i],vertical)),across);
                bool changed;
                do
                {
                    changed=false;
                    foreach(int i in before.Where(i=>Overlaps(Start(slots[i],vertical),End(slots[i],vertical),from,to)))
                        if(chosenBefore.Add(i)){from=Math.Min(from,Start(slots[i],vertical));to=Math.Max(to,End(slots[i],vertical));changed=true;}
                    foreach(int i in after.Where(i=>Overlaps(Start(slots[i],vertical),End(slots[i],vertical),from,to)))
                        if(chosenAfter.Add(i)){from=Math.Min(from,Start(slots[i],vertical));to=Math.Max(to,End(slots[i],vertical));changed=true;}
                }while(changed);
                best=new LayoutDivider{Vertical=vertical,Position=line,From=from,To=to,Before=chosenBefore.Order().ToList(),After=chosenAfter.Order().ToList()};bestDistance=distance;
            }
        }
        return best;
    }

    public static bool TryMove(IReadOnlyList<LayoutSlot> original,LayoutDivider divider,double position,int canvasWidth,int canvasHeight,int margin,int gap,out List<LayoutSlot> result)
    {
        result=Copy(original);double inner=divider.Vertical?canvasWidth-2.0*margin:canvasHeight-2.0*margin;
        if(inner<=0)return false;
        double minimum=Math.Max(.015,(gap+16)/inner);
        double outer=(double)margin/inner;
        double lower=divider.Before.Count>0?divider.Before.Max(i=>(divider.Vertical?original[i].X:original[i].Y)+minimum):-outer;
        double upper=divider.After.Count>0?divider.After.Min(i=>(divider.Vertical?original[i].X+original[i].Width:original[i].Y+original[i].Height)-minimum):1+outer;
        if(lower>=upper)return false;position=Math.Clamp(position,lower,upper);
        foreach(int i in divider.Before)
        {
            if(divider.Vertical)result[i].Width=position-original[i].X;
            else result[i].Height=position-original[i].Y;
        }
        foreach(int i in divider.After)
        {
            if(divider.Vertical){result[i].X=position;result[i].Width=original[i].X+original[i].Width-position;}
            else {result[i].Y=position;result[i].Height=original[i].Y+original[i].Height-position;}
        }
        return result.All(s=>s.Width>0&&s.Height>0&&Rendering.SlotRect(s,canvasWidth,canvasHeight,margin,gap) is var r&&r.Width>=1&&r.Height>=1);
    }

    public static bool TryResizeSelected(IReadOnlyList<LayoutSlot> original,IReadOnlyCollection<int> selected,bool vertical,bool trailingEdge,double delta,bool symmetric,int canvasWidth,int canvasHeight,int margin,int gap,out List<LayoutSlot> result)
    {
        result=Copy(original);
        if(selected.Count==0||selected.Any(i=>i<0||i>=original.Count))return false;
        double inner=vertical?canvasWidth-2.0*margin:canvasHeight-2.0*margin;
        if(inner<=0)return false;
        double minimum=Math.Max(.015,(gap+16)/inner),outer=(double)margin/inner;
        double lower=double.NegativeInfinity,upper=double.PositiveInfinity;
        void Bound(double value,double factor,double min,double max)
        {
            if(factor>0){lower=Math.Max(lower,(min-value)/factor);upper=Math.Min(upper,(max-value)/factor);}
            else if(factor<0){lower=Math.Max(lower,(max-value)/factor);upper=Math.Min(upper,(min-value)/factor);}
        }
        foreach(int i in selected)
        {
            var slot=original[i];double start=vertical?slot.X:slot.Y,size=vertical?slot.Width:slot.Height;
            // The pointer moves the chosen edge by delta. Alt moves the opposite
            // edge by the same distance in the other direction.
            double startFactor=trailingEdge?(symmetric?-1:0):1;
            double sizeFactor=(trailingEdge?1:-1)*(symmetric?2:1);
            Bound(start,startFactor,-outer,1+outer);
            Bound(start+size,startFactor+sizeFactor,-outer,1+outer);
            Bound(size,sizeFactor,minimum,double.PositiveInfinity);
        }
        if(lower>upper)return false;
        delta=Math.Clamp(delta,lower,upper);
        foreach(int i in selected)
        {
            var slot=result[i];double startFactor=trailingEdge?(symmetric?-1:0):1;
            double sizeFactor=(trailingEdge?1:-1)*(symmetric?2:1);
            if(vertical){slot.X+=startFactor*delta;slot.Width+=sizeFactor*delta;}
            else {slot.Y+=startFactor*delta;slot.Height+=sizeFactor*delta;}
        }
        return result.All(s=>s.Width>0&&s.Height>0&&Rendering.SlotRect(s,canvasWidth,canvasHeight,margin,gap) is var r&&r.Width>=1&&r.Height>=1&&r.Left>=-.5&&r.Top>=-.5&&r.Right<=canvasWidth+.5&&r.Bottom<=canvasHeight+.5);
    }
}
