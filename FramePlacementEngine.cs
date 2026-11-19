namespace CollageDesigner;

public static class FramePlacementEngine
{
    const double Epsilon=0.000001;

    public static bool TryFitInOpenArea(IReadOnlyList<LayoutSlot> original,int source,double centerX,double centerY,out List<LayoutSlot> result,int canvasWidth,int canvasHeight,int margin,int gap=0)
    {
        result=LayoutResizeEngine.Copy(original);
        if(source<0||source>=original.Count)return false;
        var bounds=Bounds(canvasWidth,canvasHeight,margin);
        if(centerX<=bounds.MinX||centerX>=bounds.MaxX||centerY<=bounds.MinY||centerY>=bounds.MaxY)return false;
        var sourceSlot=original[source];
        // Every free rectangle has edges on the canvas or on an existing frame.
        // Keep the source frame among the obstacles: the drop must land in space
        // that was visibly empty before the move.
        var xs=original.SelectMany(s=>new[]{s.X,s.X+s.Width}).Append(bounds.MinX).Append(bounds.MaxX).Where(v=>v>=bounds.MinX-Epsilon&&v<=bounds.MaxX+Epsilon).Distinct().Order().ToArray();
        var ys=original.SelectMany(s=>new[]{s.Y,s.Y+s.Height}).Append(bounds.MinY).Append(bounds.MaxY).Where(v=>v>=bounds.MinY-Epsilon&&v<=bounds.MaxY+Epsilon).Distinct().Order().ToArray();
        double bestArea=0,bestX=0,bestY=0,bestWidth=0,bestHeight=0;
        double minWidth=(gap+36.0)/(canvasWidth-2.0*margin),minHeight=(gap+36.0)/(canvasHeight-2.0*margin);
        foreach(double left in xs.Where(v=>v<centerX-Epsilon))foreach(double right in xs.Where(v=>v>centerX+Epsilon))
        {
            double width=Math.Min(sourceSlot.Width,right-left);if(width<minWidth)continue;
            foreach(double top in ys.Where(v=>v<centerY-Epsilon))foreach(double bottom in ys.Where(v=>v>centerY+Epsilon))
            {
                double height=Math.Min(sourceSlot.Height,bottom-top),area=width*height;
                if(height<minHeight||area<=bestArea+Epsilon)continue;
                bool overlaps=original.Any(s=>left<s.X+s.Width-Epsilon&&s.X<right-Epsilon&&top<s.Y+s.Height-Epsilon&&s.Y<bottom-Epsilon);
                if(overlaps)continue;
                bestArea=area;bestWidth=width;bestHeight=height;
                bestX=Math.Clamp(centerX-width/2,left,right-width);bestY=Math.Clamp(centerY-height/2,top,bottom-height);
            }
        }
        if(bestArea<=0)return false;
        var moving=result[source];moving.X=bestX;moving.Y=bestY;moving.Width=bestWidth;moving.Height=bestHeight;
        var frame=Rendering.SlotRect(moving,canvasWidth,canvasHeight,margin,gap);
        return frame.Width>=1&&frame.Height>=1&&frame.Left>=-.5&&frame.Top>=-.5&&frame.Right<=canvasWidth+.5&&frame.Bottom<=canvasHeight+.5;
    }

    public static (double MinX,double MaxX,double MinY,double MaxY) Bounds(int canvasWidth,int canvasHeight,int margin)
    {
        if(canvasWidth<=2*margin||canvasHeight<=2*margin)throw new ArgumentOutOfRangeException(nameof(margin));
        double x=(double)margin/(canvasWidth-2*margin),y=(double)margin/(canvasHeight-2*margin);
        return (-x,1+x,-y,1+y);
    }

    public static bool TryTranslate(IReadOnlyList<LayoutSlot> original,int source,double deltaX,double deltaY,out List<LayoutSlot> result,int canvasWidth=1,int canvasHeight=1,int margin=0)
    {
        result=LayoutResizeEngine.Copy(original);
        if(source<0||source>=original.Count)return false;
        var slot=original[source];
        if(slot.Width<=0||slot.Height<=0)return false;
        return TryPosition(original,source,slot.X+deltaX,slot.Y+deltaY,out result,canvasWidth,canvasHeight,margin);
    }

    public static bool PreviewTranslate(IReadOnlyList<LayoutSlot> original,int source,double deltaX,double deltaY,out List<LayoutSlot> result,int canvasWidth,int canvasHeight,int margin)
    {
        result=LayoutResizeEngine.Copy(original);
        if(source<0||source>=original.Count)return false;
        var bounds=Bounds(canvasWidth,canvasHeight,margin);var slot=result[source];
        if(slot.Width>bounds.MaxX-bounds.MinX||slot.Height>bounds.MaxY-bounds.MinY)return false;
        slot.X=Math.Clamp(slot.X+deltaX,bounds.MinX,bounds.MaxX-slot.Width);
        slot.Y=Math.Clamp(slot.Y+deltaY,bounds.MinY,bounds.MaxY-slot.Height);
        return true;
    }

    public static bool PreviewTranslateMany(IReadOnlyList<LayoutSlot> original,IReadOnlyCollection<int> sources,double deltaX,double deltaY,out List<LayoutSlot> result,int canvasWidth,int canvasHeight,int margin)
    {
        result=LayoutResizeEngine.Copy(original);
        if(sources.Count==0||sources.Any(i=>i<0||i>=original.Count))return false;
        var bounds=Bounds(canvasWidth,canvasHeight,margin);
        double left=sources.Min(i=>original[i].X),right=sources.Max(i=>original[i].X+original[i].Width);
        double top=sources.Min(i=>original[i].Y),bottom=sources.Max(i=>original[i].Y+original[i].Height);
        if(right-left>bounds.MaxX-bounds.MinX||bottom-top>bounds.MaxY-bounds.MinY)return false;
        deltaX=Math.Clamp(deltaX,bounds.MinX-left,bounds.MaxX-right);
        deltaY=Math.Clamp(deltaY,bounds.MinY-top,bounds.MaxY-bottom);
        foreach(int i in sources){result[i].X=original[i].X+deltaX;result[i].Y=original[i].Y+deltaY;}
        return true;
    }

    public static bool TryTranslateMany(IReadOnlyList<LayoutSlot> original,IReadOnlyCollection<int> sources,double deltaX,double deltaY,out List<LayoutSlot> result,int canvasWidth,int canvasHeight,int margin)
    {
        if(!PreviewTranslateMany(original,sources,deltaX,deltaY,out result,canvasWidth,canvasHeight,margin))return false;
        bool changed=false;
        foreach(int i in sources)if(Math.Abs(result[i].X-original[i].X)>=Epsilon||Math.Abs(result[i].Y-original[i].Y)>=Epsilon){changed=true;break;}
        if(!changed)return false;
        var selected=new HashSet<int>(sources);
        foreach(int i in sources)
        {
            var moving=result[i];
            for(int j=0;j<original.Count;j++)
            {
                if(selected.Contains(j))continue;
                var other=original[j];
                if(moving.X<other.X+other.Width-Epsilon&&other.X<moving.X+moving.Width-Epsilon&&moving.Y<other.Y+other.Height-Epsilon&&other.Y<moving.Y+moving.Height-Epsilon)return false;
            }
        }
        return true;
    }

    public static bool TryTranslateFree(IReadOnlyList<LayoutSlot> original,IReadOnlyCollection<int> sources,double deltaX,double deltaY,out List<LayoutSlot> result,int canvasWidth,int canvasHeight,int margin)
    {
        if(!PreviewTranslateMany(original,sources,deltaX,deltaY,out result,canvasWidth,canvasHeight,margin))return false;
        foreach(int i in sources)if(Math.Abs(result[i].X-original[i].X)>=Epsilon||Math.Abs(result[i].Y-original[i].Y)>=Epsilon)return true;
        return false;
    }

    public static bool TryNudge(IReadOnlyList<LayoutSlot> original,IReadOnlyCollection<int> sources,int pixelX,int pixelY,bool allowOverlap,out List<LayoutSlot> result,int canvasWidth,int canvasHeight,int margin)
    {
        double innerWidth=canvasWidth-2.0*margin,innerHeight=canvasHeight-2.0*margin;
        result=LayoutResizeEngine.Copy(original);
        if(innerWidth<=0||innerHeight<=0)return false;
        double dx=pixelX/innerWidth,dy=pixelY/innerHeight;
        return allowOverlap
            ?TryTranslateFree(original,sources,dx,dy,out result,canvasWidth,canvasHeight,margin)
            :TryTranslateMany(original,sources,dx,dy,out result,canvasWidth,canvasHeight,margin);
    }

    public static bool TryCenterAt(IReadOnlyList<LayoutSlot> original,int source,double centerX,double centerY,out List<LayoutSlot> result,int canvasWidth=1,int canvasHeight=1,int margin=0)
    {
        result=LayoutResizeEngine.Copy(original);
        if(source<0||source>=original.Count)return false;
        var bounds=Bounds(canvasWidth,canvasHeight,margin);
        if(centerX<bounds.MinX||centerX>bounds.MaxX||centerY<bounds.MinY||centerY>bounds.MaxY)return false;
        var slot=original[source];
        return TryPosition(original,source,centerX-slot.Width/2,centerY-slot.Height/2,out result,canvasWidth,canvasHeight,margin);
    }

    public static bool TryCenterFree(IReadOnlyList<LayoutSlot> original,int source,double centerX,double centerY,out List<LayoutSlot> result,int canvasWidth,int canvasHeight,int margin)
    {
        result=LayoutResizeEngine.Copy(original);
        if(source<0||source>=original.Count)return false;
        var slot=original[source];
        return TryTranslateFree(original,new[]{source},centerX-slot.Width/2-slot.X,centerY-slot.Height/2-slot.Y,out result,canvasWidth,canvasHeight,margin);
    }

    static bool TryPosition(IReadOnlyList<LayoutSlot> original,int source,double x,double y,out List<LayoutSlot> result,int canvasWidth,int canvasHeight,int margin)
    {
        result=LayoutResizeEngine.Copy(original);
        var slot=original[source];
        var bounds=Bounds(canvasWidth,canvasHeight,margin);
        if(slot.Width>bounds.MaxX-bounds.MinX||slot.Height>bounds.MaxY-bounds.MinY)return false;
        double newX=Math.Clamp(x,bounds.MinX,bounds.MaxX-slot.Width),newY=Math.Clamp(y,bounds.MinY,bounds.MaxY-slot.Height);
        if(Math.Abs(newX-slot.X)<Epsilon&&Math.Abs(newY-slot.Y)<Epsilon)return false;
        var moving=result[source];moving.X=newX;moving.Y=newY;
        for(int i=0;i<original.Count;i++)
        {
            if(i==source)continue;var other=original[i];
            if(newX<other.X+other.Width-Epsilon&&other.X<newX+slot.Width-Epsilon&&newY<other.Y+other.Height-Epsilon&&other.Y<newY+slot.Height-Epsilon)return false;
        }
        return true;
    }
}
