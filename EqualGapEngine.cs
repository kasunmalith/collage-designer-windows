namespace CollageDesigner;

public static class EqualGapEngine
{
    const double Epsilon=0.000001;

    // Geometry is stored before the rendering gap is inset from each touching edge.
    // Touching slots therefore render with exactly the configured Photo Gap.
    public static bool TryEqualize(IReadOnlyList<LayoutSlot> original,int width,int height,int margin,int gap,out List<LayoutSlot> result)
    {
        result=LayoutResizeEngine.Copy(original);
        if(original.Count<2||width<=2*margin||height<=2*margin)return false;
        Pack(original,result,true);
        Pack(original,result,false);
        for(int i=0;i<result.Count;i++)
        {
            var s=result[i];var r=Rendering.SlotRect(s,width,height,margin,gap);
            if(s.Width<=0||s.Height<=0||r.Width<1||r.Height<1||r.Left<-.5||r.Top<-.5||r.Right>width+.5||r.Bottom>height+.5)return false;
            for(int j=0;j<i;j++)
            {
                var t=result[j];
                if(s.X<t.X+t.Width-Epsilon&&t.X<s.X+s.Width-Epsilon&&s.Y<t.Y+t.Height-Epsilon&&t.Y<s.Y+s.Height-Epsilon)return false;
            }
        }
        return result.Where((s,i)=>Math.Abs(s.X-original[i].X)>Epsilon||Math.Abs(s.Y-original[i].Y)>Epsilon).Any();
    }

    static void Pack(IReadOnlyList<LayoutSlot> original,List<LayoutSlot> result,bool horizontal)
    {
        double Start(LayoutSlot s)=>horizontal?s.X:s.Y;
        double End(LayoutSlot s)=>horizontal?s.X+s.Width:s.Y+s.Height;
        double CrossStart(LayoutSlot s)=>horizontal?s.Y:s.X;
        double CrossEnd(LayoutSlot s)=>horizontal?s.Y+s.Height:s.X+s.Width;
        foreach(int index in Enumerable.Range(0,original.Count).OrderBy(i=>Start(original[i])))
        {
            // Use the nearest frame(s) that occupied the same row or column
            // before equalizing. This keeps the original order and frame sizes.
            double nearest=double.NegativeInfinity;
            for(int other=0;other<original.Count;other++)
            {
                if(other==index||End(original[other])>Start(original[index])+Epsilon)continue;
                if(CrossStart(original[other])>=CrossEnd(original[index])-Epsilon||CrossStart(original[index])>=CrossEnd(original[other])-Epsilon)continue;
                nearest=Math.Max(nearest,End(original[other]));
            }
            if(double.IsNegativeInfinity(nearest))continue;
            double target=double.NegativeInfinity;
            for(int other=0;other<original.Count;other++)
            {
                if(other==index||Math.Abs(End(original[other])-nearest)>Epsilon)continue;
                if(CrossStart(original[other])>=CrossEnd(original[index])-Epsilon||CrossStart(original[index])>=CrossEnd(original[other])-Epsilon)continue;
                target=Math.Max(target,End(result[other]));
            }
            if(!double.IsNegativeInfinity(target))
            {
                if(horizontal)result[index].X=target;
                else result[index].Y=target;
            }
        }
    }
}
