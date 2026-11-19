namespace CollageDesigner;

public static class LayoutRanker
{
    public static double Score(IReadOnlyList<LayoutSlot> slots,double spreadRatio,double candidatePosition,double targetPosition,IReadOnlyList<double> photoAspects)
    {
        if(slots.Count!=photoAspects.Count)return double.PositiveInfinity;
        double score=2*Math.Abs(candidatePosition-targetPosition);
        for(int i=0;i<slots.Count;i++)
        {
            var slot=slots[i];if(slot.Width<=0||slot.Height<=0)return double.PositiveInfinity;
            double slotAspect=slot.Width*spreadRatio/slot.Height;
            score+=.25*Math.Abs(Math.Log(Math.Max(.1,photoAspects[i])/slotAspect));
        }
        return score;
    }
}
