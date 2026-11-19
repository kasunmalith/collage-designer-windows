namespace CollageDesigner;

public static class PhotoShiftEngine
{
    public static List<int> InsertAt(IReadOnlyList<int> assignments,int from,int target)
    {
        if(from<0||target<0||from>=assignments.Count||target>=assignments.Count)
            throw new ArgumentOutOfRangeException(nameof(target));
        var result=assignments.ToList();
        int moved=result[from];
        result.RemoveAt(from);
        result.Insert(target,moved);
        return result;
    }
}
