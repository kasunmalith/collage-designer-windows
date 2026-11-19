using Microsoft.Data.Sqlite;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CollageDesigner;

public sealed class TrainingAlbumInfo
{
    public string TrainingId {get;set;}="";
    public string Label {get;set;}="";
    public string SourceFolder {get;set;}="";
    public List<string> PhotoFiles {get;set;}=[];
    public List<string> SheetFiles {get;set;}=[];
    public double? RatioFamily {get;set;}
    public string Status {get;set;}="READY";
    public string Problem {get;set;}="";
    public string Fingerprint {get;set;}="";
}

public sealed class TrainingSheetRecord
{
    public long Id {get;set;}
    public int Number {get;set;}
    public string SourcePath {get;set;}="";
    public string Status {get;set;}="";
    public int PhotoCount {get;set;}
}
public sealed class TrainingSlotRecord
{
    public long Id {get;set;}
    public LayoutSlot Slot {get;set;}=new();
    public string? PhotoPath {get;set;}
    public double Confidence {get;set;}
}
public sealed record TrainingDashboard(int Albums,int Sheets,int UsedPhotos,int MatchedPhotos,int ValidatedSheets,int NeedsReview,string RatioFamilies);

public static class RatioClassifier
{
    public static readonly double[] KnownFamilies=[2.0,2.4,2.5,3.0];
    public static double? Classify(double ratio,double tolerance=.03)
    {
        double nearest=KnownFamilies.OrderBy(v=>Math.Abs(v-ratio)).First();
        return Math.Abs(nearest-ratio)<=tolerance?nearest:null;
    }
}

public static class TrainingFolderScanner
{
    static readonly string[] Jpg=[".jpg",".jpeg"];
    static List<string> Photos(string folder)=>NaturalPhotoOrder.Sort(Directory.EnumerateFiles(folder).Where(p=>Jpg.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase))).ToList();
    static string? Child(string parent,string name)=>Directory.EnumerateDirectories(parent).FirstOrDefault(p=>Path.GetFileName(p).Equals(name,StringComparison.OrdinalIgnoreCase));
    public static List<TrainingAlbumInfo> Scan(string root,double tolerance=.03,CancellationToken cancellationToken=default)
    {
        if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
        var albums=new List<TrainingAlbumInfo>();
        foreach(string folder in NaturalPhotoOrder.Sort(Directory.EnumerateDirectories(root)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item=new TrainingAlbumInfo{Label=Path.GetFileName(folder),SourceFolder=Path.GetFullPath(folder)};
            string? used=Child(folder,"Used Photos"),sheets=Child(folder,"All Sheets");
            if(used==null||sheets==null){item.Status="NEEDS ATTENTION";item.Problem=used==null?"Missing Used Photos folder":"Missing All Sheets folder";albums.Add(item);continue;}
            item.PhotoFiles=Photos(used);item.SheetFiles=Photos(sheets);
            if(item.PhotoFiles.Count==0||item.SheetFiles.Count==0){item.Status="NEEDS ATTENTION";item.Problem=item.PhotoFiles.Count==0?"No JPGs in Used Photos":"No JPGs in All Sheets";albums.Add(item);continue;}
            var ratios=new List<double>();
            try
            {
                foreach(string sheet in item.SheetFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();var size=Rendering.ReadSize(sheet);
                    var family=RatioClassifier.Classify((double)size.Width/size.Height,tolerance);
                    if(family==null){item.Status="NEEDS ATTENTION";item.Problem=$"Unrecognized ratio: {Path.GetFileName(sheet)}";break;}
                    ratios.Add(family.Value);
                }
                if(item.Status=="READY"&&ratios.Distinct().Count()!=1){item.Status="NEEDS ATTENTION";item.Problem="Inconsistent sheet ratios";}
                if(item.Status=="READY")item.RatioFamily=ratios[0];
            }
            catch(Exception ex) when(ex is not OperationCanceledException){item.Status="NEEDS ATTENTION";item.Problem=$"Could not read sheet: {ex.Message}";}
            using var sha=SHA256.Create();var sb=new StringBuilder();
            foreach(string path in item.PhotoFiles.Concat(item.SheetFiles))
            {var file=new FileInfo(path);sb.Append(path).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append('\n');}
            item.Fingerprint=Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            albums.Add(item);
        }
        return albums;
    }
}

public sealed class TrainingCatalog
{
    public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Energetic Edge","Collage Designer","training.sqlite");
    readonly string path;
    public TrainingCatalog(string? path=null){this.path=path??DefaultPath;Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);Initialize();}
    SqliteConnection Open(){var db=new SqliteConnection($"Data Source={path}");db.Open();return db;}
    void Initialize()
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="""
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS TrainingAlbum (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Label TEXT NOT NULL, SourceFolder TEXT NOT NULL UNIQUE,
              Ratio REAL, TotalSheets INTEGER NOT NULL DEFAULT 0, TotalUsedPhotos INTEGER NOT NULL DEFAULT 0,
              Status TEXT NOT NULL, Problem TEXT NOT NULL DEFAULT '', Fingerprint TEXT NOT NULL DEFAULT '',
              AnalyzedFingerprint TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS TrainingSheet (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, AlbumId INTEGER NOT NULL REFERENCES TrainingAlbum(Id) ON DELETE CASCADE,
              SheetNumber INTEGER NOT NULL, SourcePath TEXT NOT NULL, Ratio REAL, NormalizedPosition REAL,
              PhotoCount INTEGER NOT NULL DEFAULT 0, Status TEXT NOT NULL DEFAULT 'WAITING',
              SourceStamp TEXT NOT NULL DEFAULT '', ManualEdited INTEGER NOT NULL DEFAULT 0,
              UNIQUE(AlbumId,SheetNumber));
            CREATE TABLE IF NOT EXISTS TrainingPhoto (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, AlbumId INTEGER NOT NULL REFERENCES TrainingAlbum(Id) ON DELETE CASCADE,
              SourcePath TEXT NOT NULL, StoryOrder INTEGER NOT NULL, Width INTEGER NOT NULL, Height INTEGER NOT NULL,
              AspectRatio REAL NOT NULL, Orientation TEXT NOT NULL, UNIQUE(AlbumId,StoryOrder));
            CREATE TABLE IF NOT EXISTS TrainingSlot (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, SheetId INTEGER NOT NULL REFERENCES TrainingSheet(Id) ON DELETE CASCADE,
              OriginalPhotoPath TEXT, X REAL NOT NULL, Y REAL NOT NULL, Width REAL NOT NULL, Height REAL NOT NULL,
              CrossesCenterFold INTEGER NOT NULL DEFAULT 0, MatchConfidence REAL,
              SourceOrientation TEXT, EstimatedCropScale REAL, EstimatedPanX REAL, EstimatedPanY REAL,
              CHECK(X>=0 AND Y>=0 AND Width>0 AND Height>0 AND X+Width<=1.000001 AND Y+Height<=1.000001));
            CREATE TABLE IF NOT EXISTS LearnedLayout (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Ratio REAL NOT NULL, PhotoCount INTEGER NOT NULL,
              SheetPosition REAL NOT NULL, SlotsJson TEXT NOT NULL, UsageCount INTEGER NOT NULL DEFAULT 1,
              SourceSheetId INTEGER REFERENCES TrainingSheet(Id));
            CREATE TABLE IF NOT EXISTS AlbumFeedback (
              Fingerprint TEXT PRIMARY KEY, Ratio REAL NOT NULL, PhotoCount INTEGER NOT NULL,
              SheetPosition REAL NOT NULL, SlotsJson TEXT NOT NULL, AddedUtc TEXT NOT NULL);
            """;cmd.ExecuteNonQuery();
        EnsureColumn(db,"TrainingSheet","SourceStamp","TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db,"TrainingSheet","ManualEdited","INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db,"TrainingSlot","SourceOrientation","TEXT");
        EnsureColumn(db,"TrainingSlot","EstimatedCropScale","REAL");
        EnsureColumn(db,"TrainingSlot","EstimatedPanX","REAL");
        EnsureColumn(db,"TrainingSlot","EstimatedPanY","REAL");
    }
    static void EnsureColumn(SqliteConnection db,string table,string column,string definition)
    {
        using var info=db.CreateCommand();info.CommandText=$"PRAGMA table_info({table})";bool exists=false;
        using(var reader=info.ExecuteReader())while(reader.Read())if(reader.GetString(1)==column)exists=true;
        if(exists)return;using var alter=db.CreateCommand();alter.CommandText=$"ALTER TABLE {table} ADD COLUMN {column} {definition}";alter.ExecuteNonQuery();
    }
    public List<TrainingAlbumInfo> SaveScan(List<TrainingAlbumInfo> albums)
    {
        using var db=Open();using var tx=db.BeginTransaction();
        foreach(var album in albums)
        {
            using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
                INSERT INTO TrainingAlbum(Label,SourceFolder,Ratio,TotalSheets,TotalUsedPhotos,Status,Problem,Fingerprint)
                VALUES($label,$source,$ratio,$sheets,$photos,$status,$problem,$fingerprint)
                ON CONFLICT(SourceFolder) DO UPDATE SET Label=excluded.Label,Ratio=excluded.Ratio,
                TotalSheets=excluded.TotalSheets,TotalUsedPhotos=excluded.TotalUsedPhotos,Status=excluded.Status,
                Problem=excluded.Problem,Fingerprint=excluded.Fingerprint;
                SELECT Id FROM TrainingAlbum WHERE SourceFolder=$source;
                """;
            cmd.Parameters.AddWithValue("$label",album.Label);cmd.Parameters.AddWithValue("$source",album.SourceFolder);
            cmd.Parameters.AddWithValue("$ratio",(object?)album.RatioFamily??DBNull.Value);cmd.Parameters.AddWithValue("$sheets",album.SheetFiles.Count);
            cmd.Parameters.AddWithValue("$photos",album.PhotoFiles.Count);cmd.Parameters.AddWithValue("$status",album.Status);
            cmd.Parameters.AddWithValue("$problem",album.Problem);cmd.Parameters.AddWithValue("$fingerprint",album.Fingerprint);
            int id=Convert.ToInt32(cmd.ExecuteScalar());album.TrainingId=$"TRN-{id:D6}";
        }
        tx.Commit();return albums;
    }
    public bool NeedsAnalysis(TrainingAlbumInfo album)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT AnalyzedFingerprint FROM TrainingAlbum WHERE SourceFolder=$source";cmd.Parameters.AddWithValue("$source",album.SourceFolder);
        return (string?)cmd.ExecuteScalar()!=album.Fingerprint;
    }
    public void SaveSheetMetadata(TrainingAlbumInfo album)
    {
        if(album.Status!="READY")return;
        using var db=Open();using var tx=db.BeginTransaction();
        using(var clear=db.CreateCommand()){clear.Transaction=tx;clear.CommandText="DELETE FROM TrainingPhoto WHERE AlbumId=(SELECT Id FROM TrainingAlbum WHERE SourceFolder=$source)";clear.Parameters.AddWithValue("$source",album.SourceFolder);clear.ExecuteNonQuery();}
        for(int i=0;i<album.PhotoFiles.Count;i++)
        {
            try
            {
                var size=Rendering.ReadSize(album.PhotoFiles[i]);double aspect=(double)size.Width/size.Height;
                string orientation=aspect>1.7?"Panoramic":aspect>1.1?"Landscape":aspect<.9?"Portrait":"Square-ish";
                using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
                    INSERT INTO TrainingPhoto(AlbumId,SourcePath,StoryOrder,Width,Height,AspectRatio,Orientation)
                    VALUES((SELECT Id FROM TrainingAlbum WHERE SourceFolder=$source),$path,$order,$width,$height,$aspect,$orientation)
                    """;
                cmd.Parameters.AddWithValue("$source",album.SourceFolder);cmd.Parameters.AddWithValue("$path",album.PhotoFiles[i]);cmd.Parameters.AddWithValue("$order",i);
                cmd.Parameters.AddWithValue("$width",size.Width);cmd.Parameters.AddWithValue("$height",size.Height);cmd.Parameters.AddWithValue("$aspect",aspect);cmd.Parameters.AddWithValue("$orientation",orientation);cmd.ExecuteNonQuery();
            }
            catch{ /* Keep the album's sheet metadata; the unreadable photo needs review. */ }
        }
        for(int i=0;i<album.SheetFiles.Count;i++)
        {
            var file=new FileInfo(album.SheetFiles[i]);string stamp=$"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            using var lookup=db.CreateCommand();lookup.Transaction=tx;lookup.CommandText="SELECT Id,SourceStamp FROM TrainingSheet WHERE AlbumId=(SELECT Id FROM TrainingAlbum WHERE SourceFolder=$source) AND SheetNumber=$number";
            lookup.Parameters.AddWithValue("$source",album.SourceFolder);lookup.Parameters.AddWithValue("$number",i+1);
            long id=0;string? oldStamp=null;using(var reader=lookup.ExecuteReader())if(reader.Read()){id=reader.GetInt64(0);oldStamp=reader.GetString(1);}
            if(id==0)
            {
                using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
                    INSERT INTO TrainingSheet(AlbumId,SheetNumber,SourcePath,Ratio,NormalizedPosition,Status,SourceStamp)
                    VALUES((SELECT Id FROM TrainingAlbum WHERE SourceFolder=$source),$number,$path,$ratio,$position,'NEEDS REVIEW',$stamp)
                    """;
                cmd.Parameters.AddWithValue("$source",album.SourceFolder);cmd.Parameters.AddWithValue("$number",i+1);cmd.Parameters.AddWithValue("$path",album.SheetFiles[i]);
                cmd.Parameters.AddWithValue("$ratio",album.RatioFamily!.Value);cmd.Parameters.AddWithValue("$position",(double)(i+1)/album.SheetFiles.Count);cmd.Parameters.AddWithValue("$stamp",stamp);cmd.ExecuteNonQuery();
            }
            else
            {
                bool changed=oldStamp!=stamp;
                if(changed){using var clear=db.CreateCommand();clear.Transaction=tx;clear.CommandText="DELETE FROM TrainingSlot WHERE SheetId=$id";clear.Parameters.AddWithValue("$id",id);clear.ExecuteNonQuery();}
                using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText=changed
                    ?"UPDATE TrainingSheet SET SourcePath=$path,Ratio=$ratio,NormalizedPosition=$position,SourceStamp=$stamp,Status='NEEDS REVIEW',PhotoCount=0,ManualEdited=0 WHERE Id=$id"
                    :"UPDATE TrainingSheet SET Ratio=$ratio,NormalizedPosition=$position WHERE Id=$id";
                cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$ratio",album.RatioFamily!.Value);
                cmd.Parameters.AddWithValue("$position",(double)(i+1)/album.SheetFiles.Count);
                if(changed){cmd.Parameters.AddWithValue("$path",album.SheetFiles[i]);cmd.Parameters.AddWithValue("$stamp",stamp);}
                cmd.ExecuteNonQuery();
            }
        }
        using(var trim=db.CreateCommand()){trim.Transaction=tx;trim.CommandText="DELETE FROM TrainingSheet WHERE AlbumId=(SELECT Id FROM TrainingAlbum WHERE SourceFolder=$source) AND SheetNumber>$count";trim.Parameters.AddWithValue("$source",album.SourceFolder);trim.Parameters.AddWithValue("$count",album.SheetFiles.Count);trim.ExecuteNonQuery();}
        tx.Commit();
    }
    public void SaveAnalysis(TrainingAlbumInfo album,IReadOnlyList<SheetAnalysis> analyses)
    {
        using var db=Open();using var tx=db.BeginTransaction();
        for(int i=0;i<analyses.Count;i++)
        {
            using var lookup=db.CreateCommand();lookup.Transaction=tx;lookup.CommandText="SELECT Id,Status,ManualEdited FROM TrainingSheet WHERE AlbumId=(SELECT Id FROM TrainingAlbum WHERE SourceFolder=$source) AND SheetNumber=$number";
            lookup.Parameters.AddWithValue("$source",album.SourceFolder);lookup.Parameters.AddWithValue("$number",i+1);
            long sheetId;bool preserve;
            using(var reader=lookup.ExecuteReader()){if(!reader.Read())throw new InvalidDataException("Sheet metadata is missing.");sheetId=reader.GetInt64(0);preserve=reader.GetString(1)=="VALIDATED"||reader.GetInt32(2)==1;}
            if(preserve)continue;
            using(var clear=db.CreateCommand()){clear.Transaction=tx;clear.CommandText="DELETE FROM TrainingSlot WHERE SheetId=$sheet";clear.Parameters.AddWithValue("$sheet",sheetId);clear.ExecuteNonQuery();}
            foreach(var region in analyses[i].Regions)
            {
                using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
                    INSERT INTO TrainingSlot(SheetId,OriginalPhotoPath,X,Y,Width,Height,CrossesCenterFold,MatchConfidence)
                    VALUES($sheet,$photo,$x,$y,$width,$height,$fold,$confidence)
                    """;
                cmd.Parameters.AddWithValue("$sheet",sheetId);cmd.Parameters.AddWithValue("$photo",(object?)region.MatchedPhotoPath??DBNull.Value);
                cmd.Parameters.AddWithValue("$x",region.Slot.X);cmd.Parameters.AddWithValue("$y",region.Slot.Y);
                cmd.Parameters.AddWithValue("$width",region.Slot.Width);cmd.Parameters.AddWithValue("$height",region.Slot.Height);
                cmd.Parameters.AddWithValue("$fold",region.Slot.X<.5&&region.Slot.X+region.Slot.Width>.5?1:0);
                cmd.Parameters.AddWithValue("$confidence",region.Confidence);cmd.ExecuteNonQuery();
            }
            using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE TrainingSheet SET PhotoCount=$count,Status=$status WHERE Id=$sheet";cmd.Parameters.AddWithValue("$count",analyses[i].Regions.Count);cmd.Parameters.AddWithValue("$status",analyses[i].Status);cmd.Parameters.AddWithValue("$sheet",sheetId);cmd.ExecuteNonQuery();}
        }
        using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE TrainingAlbum SET AnalyzedFingerprint=$fingerprint WHERE SourceFolder=$source";cmd.Parameters.AddWithValue("$fingerprint",album.Fingerprint);cmd.Parameters.AddWithValue("$source",album.SourceFolder);cmd.ExecuteNonQuery();}
        tx.Commit();
    }
    public (int Albums,int Sheets,int Photos,int NeedsReview) Statistics()
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT (SELECT COUNT(*) FROM TrainingAlbum),(SELECT COUNT(*) FROM TrainingSheet),(SELECT COALESCE(SUM(TotalUsedPhotos),0) FROM TrainingAlbum),(SELECT COUNT(*) FROM TrainingSheet WHERE Status='NEEDS REVIEW')";
        using var reader=cmd.ExecuteReader();reader.Read();return(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3));
    }
    public int LearnedLayoutCount(){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM LearnedLayout";return Convert.ToInt32(cmd.ExecuteScalar());}
    public TrainingDashboard Dashboard()
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="""
            SELECT (SELECT COUNT(*) FROM TrainingAlbum),
                   (SELECT COUNT(*) FROM TrainingSheet),
                   (SELECT COALESCE(SUM(TotalUsedPhotos),0) FROM TrainingAlbum),
                   (SELECT COUNT(*) FROM TrainingSlot WHERE OriginalPhotoPath IS NOT NULL),
                   (SELECT COUNT(*) FROM TrainingSheet WHERE Status='VALIDATED'),
                   (SELECT COUNT(*) FROM TrainingSheet WHERE Status='NEEDS REVIEW')
            """;
        int[] values=new int[6];using(var reader=cmd.ExecuteReader()){reader.Read();for(int i=0;i<6;i++)values[i]=reader.GetInt32(i);}
        using var ratioCmd=db.CreateCommand();ratioCmd.CommandText="SELECT DISTINCT Ratio FROM TrainingAlbum WHERE Ratio IS NOT NULL ORDER BY Ratio";
        var families=new List<string>();using(var reader=ratioCmd.ExecuteReader())while(reader.Read())families.Add(reader.GetDouble(0).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture));
        return new(values[0],values[1],values[2],values[3],values[4],values[5],string.Join(", ",families));
    }
    public List<TrainingSheetRecord> Sheets(string albumFolder)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT Id,SheetNumber,SourcePath,Status,PhotoCount FROM TrainingSheet WHERE AlbumId=(SELECT Id FROM TrainingAlbum WHERE SourceFolder=$source) ORDER BY SheetNumber";cmd.Parameters.AddWithValue("$source",albumFolder);
        using var reader=cmd.ExecuteReader();var result=new List<TrainingSheetRecord>();
        while(reader.Read())result.Add(new(){Id=reader.GetInt64(0),Number=reader.GetInt32(1),SourcePath=reader.GetString(2),Status=reader.GetString(3),PhotoCount=reader.GetInt32(4)});
        return result;
    }
    public List<TrainingSlotRecord> Slots(long sheetId)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT Id,OriginalPhotoPath,X,Y,Width,Height,MatchConfidence FROM TrainingSlot WHERE SheetId=$sheet ORDER BY Id";cmd.Parameters.AddWithValue("$sheet",sheetId);
        using var reader=cmd.ExecuteReader();var result=new List<TrainingSlotRecord>();
        while(reader.Read())result.Add(new(){Id=reader.GetInt64(0),PhotoPath=reader.IsDBNull(1)?null:reader.GetString(1),Slot=new(){X=reader.GetDouble(2),Y=reader.GetDouble(3),Width=reader.GetDouble(4),Height=reader.GetDouble(5)},Confidence=reader.IsDBNull(6)?0:reader.GetDouble(6)});
        return result;
    }
    public void SetSlotPhoto(long slotId,string photoPath)
    {
        using var db=Open();using var tx=db.BeginTransaction();
        using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE TrainingSlot SET OriginalPhotoPath=$photo,MatchConfidence=1 WHERE Id=$id";cmd.Parameters.AddWithValue("$photo",photoPath);cmd.Parameters.AddWithValue("$id",slotId);cmd.ExecuteNonQuery();}
        using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE TrainingSheet SET ManualEdited=1,Status='NEEDS REVIEW' WHERE Id=(SELECT SheetId FROM TrainingSlot WHERE Id=$id)";cmd.Parameters.AddWithValue("$id",slotId);cmd.ExecuteNonQuery();}
        tx.Commit();
    }
    public void AddSlot(long sheetId,LayoutSlot slot,string? photoPath)
    {
        if(slot.X<0||slot.Y<0||slot.Width<=0||slot.Height<=0||slot.X+slot.Width>1.000001||slot.Y+slot.Height>1.000001)throw new ArgumentOutOfRangeException(nameof(slot));
        using var db=Open();using var tx=db.BeginTransaction();
        using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="INSERT INTO TrainingSlot(SheetId,OriginalPhotoPath,X,Y,Width,Height,CrossesCenterFold,MatchConfidence) VALUES($sheet,$photo,$x,$y,$w,$h,$fold,$confidence)";
            cmd.Parameters.AddWithValue("$sheet",sheetId);cmd.Parameters.AddWithValue("$photo",(object?)photoPath??DBNull.Value);cmd.Parameters.AddWithValue("$x",slot.X);cmd.Parameters.AddWithValue("$y",slot.Y);cmd.Parameters.AddWithValue("$w",slot.Width);cmd.Parameters.AddWithValue("$h",slot.Height);cmd.Parameters.AddWithValue("$fold",slot.X<.5&&slot.X+slot.Width>.5?1:0);cmd.Parameters.AddWithValue("$confidence",photoPath==null?0:1);cmd.ExecuteNonQuery();}
        using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE TrainingSheet SET PhotoCount=(SELECT COUNT(*) FROM TrainingSlot WHERE SheetId=$sheet),Status='NEEDS REVIEW',ManualEdited=1 WHERE Id=$sheet";cmd.Parameters.AddWithValue("$sheet",sheetId);cmd.ExecuteNonQuery();}
        tx.Commit();
    }
    public void DeleteSlot(long slotId,long sheetId)
    {
        using var db=Open();using var tx=db.BeginTransaction();using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="DELETE FROM TrainingSlot WHERE Id=$id AND SheetId=$sheet";cmd.Parameters.AddWithValue("$id",slotId);cmd.Parameters.AddWithValue("$sheet",sheetId);cmd.ExecuteNonQuery();}
        using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE TrainingSheet SET PhotoCount=(SELECT COUNT(*) FROM TrainingSlot WHERE SheetId=$sheet),Status='NEEDS REVIEW',ManualEdited=1 WHERE Id=$sheet";cmd.Parameters.AddWithValue("$sheet",sheetId);cmd.ExecuteNonQuery();}tx.Commit();
    }
    public bool ValidateSheet(long sheetId)
    {
        var slots=Slots(sheetId);if(slots.Count==0||slots.Any(s=>s.PhotoPath==null)||slots.Select(s=>s.PhotoPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=slots.Count)return false;
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE TrainingSheet SET Status='VALIDATED',PhotoCount=$count,ManualEdited=1 WHERE Id=$id";cmd.Parameters.AddWithValue("$id",sheetId);cmd.Parameters.AddWithValue("$count",slots.Count);cmd.ExecuteNonQuery();return true;
    }
    public int TrainValidatedLayouts()
    {
        using var db=Open();using var tx=db.BeginTransaction();
        var samples=new List<LearnedLayoutSample>();
        using var sheetCmd=db.CreateCommand();sheetCmd.Transaction=tx;sheetCmd.CommandText="SELECT Id,Ratio,NormalizedPosition FROM TrainingSheet WHERE Status='VALIDATED' AND Ratio IS NOT NULL";
        var sheets=new List<(long Id,double Ratio,double Position)>();using(var reader=sheetCmd.ExecuteReader())while(reader.Read())sheets.Add((reader.GetInt64(0),reader.GetDouble(1),reader.GetDouble(2)));
        foreach(var sheet in sheets)
        {
            using var slotCmd=db.CreateCommand();slotCmd.Transaction=tx;slotCmd.CommandText="SELECT X,Y,Width,Height FROM TrainingSlot WHERE SheetId=$sheet ORDER BY Id";slotCmd.Parameters.AddWithValue("$sheet",sheet.Id);
            var slots=new List<LayoutSlot>();using(var reader=slotCmd.ExecuteReader())while(reader.Read())slots.Add(new(){X=reader.GetDouble(0),Y=reader.GetDouble(1),Width=reader.GetDouble(2),Height=reader.GetDouble(3)});
            if(slots.Count==0)continue;
            samples.Add(new(){Ratio=sheet.Ratio,Position=sheet.Position,Slots=slots});
        }
        using(var feedback=db.CreateCommand())
        {
            feedback.Transaction=tx;feedback.CommandText="SELECT Ratio,SheetPosition,SlotsJson FROM AlbumFeedback";
            using var reader=feedback.ExecuteReader();while(reader.Read())
            {
                var slots=System.Text.Json.JsonSerializer.Deserialize<List<LayoutSlot>>(reader.GetString(2));
                if(slots!=null)samples.Add(new(){Ratio=reader.GetDouble(0),Position=reader.GetDouble(1),Slots=slots});
            }
        }
        var clusters=LayoutClusterer.Cluster(samples);
        using(var clear=db.CreateCommand()){clear.Transaction=tx;clear.CommandText="DELETE FROM LearnedLayout";clear.ExecuteNonQuery();}
        foreach(var cluster in clusters)
        {
            using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO LearnedLayout(Ratio,PhotoCount,SheetPosition,SlotsJson,UsageCount) VALUES($ratio,$count,$position,$slots,$usage)";
            cmd.Parameters.AddWithValue("$ratio",cluster.Ratio);cmd.Parameters.AddWithValue("$count",cluster.Slots.Count);cmd.Parameters.AddWithValue("$position",cluster.Position);
            cmd.Parameters.AddWithValue("$slots",System.Text.Json.JsonSerializer.Serialize(cluster.Slots));cmd.Parameters.AddWithValue("$usage",cluster.UsageCount);cmd.ExecuteNonQuery();
        }
        tx.Commit();return clusters.Count;
    }
    public int AddApprovedAlbum(AlbumProject album)
    {
        if(!AlbumEngine.ValidateUsage(album)||album.Unassigned.Count>0||album.Sheets.Any(s=>!s.Approved))throw new InvalidOperationException("Assign all photos and approve every sheet before adding the album to your style.");
        using var db=Open();using var tx=db.BeginTransaction();int inserted=0;
        for(int i=0;i<album.Sheets.Count;i++)
        {
            var sheet=album.Sheets[i];string slotsJson=System.Text.Json.JsonSerializer.Serialize(sheet.Slots);
            string identity=string.Join('|',album.Photos.Select(p=>p.Path))+"|"+i+"|"+slotsJson;
            string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
                INSERT OR IGNORE INTO AlbumFeedback(Fingerprint,Ratio,PhotoCount,SheetPosition,SlotsJson,AddedUtc)
                VALUES($hash,$ratio,$count,$position,$slots,$added)
                """;
            cmd.Parameters.AddWithValue("$hash",fingerprint);cmd.Parameters.AddWithValue("$ratio",album.Ratio);
            cmd.Parameters.AddWithValue("$count",sheet.PhotoIndices.Count);cmd.Parameters.AddWithValue("$position",(double)(i+1)/album.Sheets.Count);
            cmd.Parameters.AddWithValue("$slots",slotsJson);cmd.Parameters.AddWithValue("$added",DateTime.UtcNow.ToString("O"));inserted+=cmd.ExecuteNonQuery();
        }
        tx.Commit();return inserted;
    }
    public List<LayoutSlot>? Recommend(double ratio,int photoCount,double position,IReadOnlyList<double> photoAspects)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT SlotsJson,SheetPosition,UsageCount FROM LearnedLayout WHERE ABS(Ratio-$ratio)<0.035 AND PhotoCount=$count";
        cmd.Parameters.AddWithValue("$ratio",ratio);cmd.Parameters.AddWithValue("$count",photoCount);
        using var reader=cmd.ExecuteReader();List<LayoutSlot>? best=null;double bestScore=double.PositiveInfinity;
        while(reader.Read())
        {
            var slots=System.Text.Json.JsonSerializer.Deserialize<List<LayoutSlot>>(reader.GetString(0));if(slots==null)continue;
            double score=LayoutRanker.Score(slots,ratio,reader.GetDouble(1),position,photoAspects)-.06*Math.Log(1+reader.GetInt32(2));
            if(score<bestScore){bestScore=score;best=slots;}
        }
        return best;
    }
}
