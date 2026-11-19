namespace CollageDesigner;

public static class LayerStackEngine
{
    public static List<int> Order(ProjectData data,int count)
    {
        var order=new List<int>(count);
        foreach(int slot in data.LayerOrder)if(slot>=0&&slot<count&&!order.Contains(slot))order.Add(slot);
        for(int slot=0;slot<count;slot++)if(!order.Contains(slot))order.Add(slot);
        return order;
    }

    public static void BringToFront(ProjectData data,int count,IReadOnlyCollection<int> selected)
    {
        var order=Order(data,count);
        var group=new HashSet<int>(selected);
        data.LayerOrder=order.Where(i=>!group.Contains(i)).Concat(order.Where(group.Contains)).ToList();
    }
}
