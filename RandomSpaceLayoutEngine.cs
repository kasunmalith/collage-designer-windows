namespace CollageDesigner;

public static class RandomSpaceLayoutEngine
{
    static LayoutSlot S(double x,double y,double width,double height)=>new(){X=x,Y=y,Width=width,Height=height};

    public static IReadOnlyList<LayoutTemplate> ForCount(int count)
    {
        if(count<1||count>12)return [];
        var designs=new List<LayoutTemplate>();
        void Add(string id,string name,params LayoutSlot[] slots)
        {
            if(slots.Length!=count)throw new InvalidOperationException("Random space layout photo count mismatch.");
            designs.Add(new LayoutTemplate{Id=$"random-{count}-{id}",Name=name,PhotoCount=count,Slots=slots.ToList()});
        }

        switch(count)
        {
            case 1:
                Add("centered","Centered portrait",S(.19,.08,.62,.84));
                Add("offset","Offset feature",S(.08,.12,.72,.76));
                break;
            case 2:
                Add("floating-pair","Floating pair",S(.14,.07,.72,.39),S(.02,.55,.96,.39));
                Add("diagonal-pair","Diagonal pair",S(.05,.09,.44,.44),S(.51,.48,.44,.44));
                Add("airy-pair","Airy vertical pair",S(.08,.06,.55,.37),S(.37,.57,.55,.37));
                break;
            case 3:
                Add("staircase","Stepped triptych",S(0,.10,.33,.41),S(.33,.31,.34,.41),S(.67,.51,.33,.41));
                Add("right-feature","Feature right, pair left",S(.31,0,.69,1),S(.07,.04,.24,.40),S(.07,.49,.24,.40));
                Add("left-feature","Feature left, pair right",S(0,0,.69,1),S(.69,.09,.25,.36),S(.69,.55,.25,.36));
                break;
            case 4:
                Add("feature-stack","Feature left, three stack",S(0,0,.62,1),S(.69,.07,.26,.26),S(.69,.37,.26,.26),S(.69,.67,.26,.26));
                Add("four-bands","Four cinematic bands",S(0,0,1,.25),S(0,.25,1,.25),S(0,.5,1,.25),S(0,.75,1,.25));
                Add("floating-four","Floating four",S(.05,.04,.41,.40),S(.54,.14,.41,.36),S(.05,.56,.41,.36),S(.54,.58,.41,.37));
                break;
            case 5:
                Add("editorial-five","Editorial five",S(.04,.16,.28,.32),S(.04,.51,.28,.35),S(.37,.06,.30,.37),S(.37,.47,.30,.43),S(.71,.28,.25,.45));
                Add("staircase-five","Five staggered tiles",S(.03,.04,.29,.29),S(.35,.18,.29,.29),S(.68,.32,.29,.29),S(.03,.54,.29,.37),S(.35,.64,.29,.32));
                Add("feature-five","Feature and four",S(.04,.08,.48,.84),S(.58,.05,.36,.19),S(.58,.28,.36,.19),S(.58,.52,.36,.19),S(.58,.76,.36,.19));
                break;
            case 6:
                Add("three-columns","Three staggered columns",S(0,.12,.31,.34),S(0,.53,.31,.34),S(.37,0,.26,.29),S(.37,.35,.26,.29),S(.37,.70,.26,.29),S(.69,.12,.31,.75));
                Add("editorial-six","Floating editorial six",S(.04,.15,.31,.33),S(.04,.51,.31,.35),S(.38,.07,.30,.26),S(.38,.36,.25,.24),S(.38,.63,.25,.29),S(.68,.35,.28,.38));
                Add("central-cascade","Central cascade",S(.30,.04,.23,.26),S(.08,.31,.36,.20),S(.53,.23,.24,.28),S(.13,.55,.27,.26),S(.42,.53,.36,.20),S(.48,.76,.26,.21));
                break;
            default:
                if(count==7)Add("three-staggered-columns","Three staggered columns",S(0,.12,.31,.34),S(0,.53,.31,.34),S(.37,0,.26,.29),S(.37,.35,.26,.29),S(.37,.70,.26,.29),S(.69,.12,.31,.34),S(.69,.53,.31,.34));
                Add("airy-gallery","Airy gallery",Gallery(count,3,false));
                Add("staggered-gallery","Staggered gallery",Gallery(count,3,true));
                Add("feature-column","Feature with floating grid",FeatureGrid(count));
                Add("cinematic-bands","Cinematic bands",Bands(count));
                break;
        }
        return designs;
    }

    static LayoutSlot[] Gallery(int count,int columns,bool stagger)
    {
        int rows=(int)Math.Ceiling((double)count/columns);
        var slots=new LayoutSlot[count];
        for(int i=0;i<count;i++)
        {
            int row=i/columns,column=i%columns;
            double cellWidth=1.0/columns,cellHeight=1.0/rows;
            double x=(column+(stagger&&row%2==1?.12:.07))*cellWidth;
            double y=(row+(stagger&&column%2==1?.19:.08))*cellHeight;
            slots[i]=S(x,y,cellWidth*.83,cellHeight*(stagger?.68:.80));
        }
        return slots;
    }

    static LayoutSlot[] FeatureGrid(int count)
    {
        var slots=new LayoutSlot[count];slots[0]=S(.03,.08,.42,.84);
        int remaining=count-1,rows=(int)Math.Ceiling(remaining/2.0);
        for(int i=0;i<remaining;i++)
        {
            int row=i/2,column=i%2;double cellHeight=1.0/rows;
            slots[i+1]=S(.51+column*.24,(row+.08)*cellHeight,.20,cellHeight*.79);
        }
        return slots;
    }

    static LayoutSlot[] Bands(int count)
    {
        var slots=new LayoutSlot[count];double band=1.0/count;
        for(int i=0;i<count;i++)slots[i]=S(i%2==0?0:.08,i*band,i%2==0?1:.84,band*.90);
        return slots;
    }
}
