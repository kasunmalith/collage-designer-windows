using CollageDesigner;
using SkiaSharp;
using System.Text.Json;
using System.Buffers.Binary;
var root=Path.Combine(Path.GetTempPath(),"CollageDesignerTests");Directory.CreateDirectory(root);
var exportNames=Path.Combine(root,"export-name-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(exportNames);
string Numeric(string name)=>Path.GetFileName(ExportNamePlanner.NextAvailable(Path.Combine(exportNames,name)));
if(Numeric("1.jpg")!="2.jpg"||Numeric("009.png")!="010.png"||Numeric("album.jpg")!="album_2.jpg"||Numeric("album_2.psd")!="album_3.psd")throw new Exception("Export name increment failed");
var originalExport=Path.Combine(exportNames,"safe.jpg");var secondExport=Path.Combine(exportNames,"safe_2.jpg");
File.WriteAllBytes(originalExport,[1,2,3]);File.WriteAllBytes(secondExport,[4,5,6]);
var pendingExport=Path.Combine(exportNames,"pending.jpg");File.WriteAllBytes(pendingExport,[7,8,9]);
var committedExport=ExportFileWriter.Commit(pendingExport,originalExport);
if(Path.GetFileName(committedExport)!="safe_3.jpg"||!File.ReadAllBytes(originalExport).SequenceEqual(new byte[]{1,2,3})||!File.ReadAllBytes(secondExport).SequenceEqual(new byte[]{4,5,6})||!File.ReadAllBytes(committedExport).SequenceEqual(new byte[]{7,8,9}))throw new Exception("Export replaced an existing file");
var exportSettingsPath=Path.Combine(exportNames,"settings.json");ExportSettingsStore.Save(new ExportSettings{LastExportPath=committedExport,QuickSaveEnabled=true},exportSettingsPath);
var savedExportSettings=ExportSettingsStore.Load(exportSettingsPath);if(savedExportSettings.LastExportPath!=committedExport||!savedExportSettings.QuickSaveEnabled)throw new Exception("Export settings did not persist");
if(!PhotoShiftEngine.InsertAt(new[]{0,1,2},0,2).SequenceEqual(new[]{1,2,0})||!PhotoShiftEngine.InsertAt(new[]{0,1,2},2,0).SequenceEqual(new[]{2,0,1})||!PhotoShiftEngine.InsertAt(new[]{0,1,2},1,1).SequenceEqual(new[]{0,1,2}))throw new Exception("Photo insertion did not shift neighboring photos");
var resizeFrames=new List<LayoutSlot>{new(){X=.1,Y=.1,Width=.2,Height=.25},new(){X=.5,Y=.4,Width=.2,Height=.25},new(){X=.8,Y=.7,Width=.1,Height=.1}};
if(!FramePlacementEngine.TryNudge(resizeFrames,new[]{0},2,0,false,out var nudged,1000,1000,0)||Math.Abs(nudged[0].X-.102)>1e-8||Math.Abs(nudged[0].Y-.1)>1e-8||Math.Abs(nudged[1].X-.5)>1e-8)throw new Exception("Arrow nudge must move selected frame by 2 px");
if(!FramePlacementEngine.TryNudge(resizeFrames,new[]{0,1},0,-2,true,out var groupNudged,1000,1000,0)||Math.Abs(groupNudged[0].Y-.098)>1e-8||Math.Abs(groupNudged[1].Y-.398)>1e-8)throw new Exception("Group arrow nudge failed");
if(FramePlacementEngine.TryNudge(new List<LayoutSlot>{new(){X=0,Y=0,Width=.3,Height=.3}},new[]{0},-2,0,true,out _,1000,1000,0))throw new Exception("Arrow nudge crossed canvas edge");
if(!LayoutResizeEngine.TryResizeSelected(resizeFrames,new[]{0,1},true,true,.05,false,1000,1000,0,0,out var resizedGroup)||Math.Abs(resizedGroup[0].Width-.25)>1e-8||Math.Abs(resizedGroup[1].Width-.25)>1e-8||Math.Abs(resizedGroup[2].Width-.1)>1e-8)throw new Exception("Group frame resize failed");
if(!LayoutResizeEngine.TryResizeSelected(resizeFrames,new[]{0,1},true,false,-.03,true,1000,1000,0,0,out var centeredGroup)||Math.Abs(centeredGroup[0].X-.07)>1e-8||Math.Abs(centeredGroup[0].Width-.26)>1e-8||Math.Abs(centeredGroup[1].X-.47)>1e-8||Math.Abs(centeredGroup[1].Width-.26)>1e-8)throw new Exception("Alt symmetric group resize failed");
var photos=new List<PhotoEntry>();
for(int i=0;i<12;i++)
{
    var path=Path.Combine(root,$"photo{i}.jpg");using var bitmap=new SKBitmap(100+i*3,140+i*3);using(var c=new SKCanvas(bitmap)){c.Clear(new SKColor((byte)(i*17),70,130));}using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Jpeg,95);File.WriteAllBytes(path,data.ToArray());var size=Rendering.ReadSize(path);if(size.Width!=100+i*3)throw new Exception("JPEG dimension check failed");photos.Add(new PhotoEntry{Path=path,Width=size.Width,Height=size.Height,Preview=Rendering.MakePreview(path)});
}
if(!NaturalPhotoOrder.Sort(new[]{"WAR_10.jpg","WAR_2.jpg","WAR_1.jpg"}).SequenceEqual(new[]{"WAR_1.jpg","WAR_2.jpg","WAR_10.jpg"}))throw new Exception("Natural photo order failed");
var album96=AlbumEngine.Generate(new AlbumSize{HeightInches=12,WidthInches=30},20,Enumerable.Range(1,96).Select(i=>$"WAR_{i:D3}.jpg").ToArray());
var usage96=AlbumEngine.Usage(album96);
if(album96.Sheets.Count!=20||usage96.Used!=96||usage96.Unused!=0||usage96.Duplicates!=0||album96.Ratio!=2.5||album96.PixelWidth!=9000||album96.PixelHeight!=3600||!AlbumEngine.ValidateUsage(album96))throw new Exception("96-photo album generation failed");
if(album96.Sheets.SelectMany(s=>s.PhotoIndices).Where((p,i)=>p!=i).Any()||album96.Sheets.Select(s=>s.PhotoIndices.Count).Distinct().Count()<2)throw new Exception("Album story order or density variation failed");
var firstSheetBefore=album96.Sheets[0].PhotoIndices.ToArray();AlbumEngine.MovePhoto(album96,0,0,1,0);
if(album96.Sheets[1].PhotoIndices[0]!=firstSheetBefore[0]||!AlbumEngine.ValidateUsage(album96))throw new Exception("Cross-sheet photo move lost a photo");
var savedAlbumPath=Path.Combine(exportNames,"test.albumproject");AlbumEngine.Save(album96,savedAlbumPath);if(!AlbumEngine.ValidateUsage(AlbumEngine.Load(savedAlbumPath)))throw new Exception("Album project persistence failed");
if(RatioClassifier.Classify(2.498)!=2.5||RatioClassifier.Classify(2.402)!=2.4||RatioClassifier.Classify(2.8)!=null)throw new Exception("Training ratio classification failed");
var trainingRoot=Path.Combine(exportNames,"All Albums");
foreach(var couple in new[]{"Couple A","Couple B"})
{
    var used=Path.Combine(trainingRoot,couple,"Used Photos");var sheets=Path.Combine(trainingRoot,couple,"All Sheets");Directory.CreateDirectory(used);Directory.CreateDirectory(sheets);
    File.Copy(photos[0].Path,Path.Combine(used,"WAR_001.jpg"));
    using var bitmap=new SKBitmap(500,200);using(var c=new SKCanvas(bitmap)){c.Clear(SKColors.White);}using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Jpeg,90);
    File.WriteAllBytes(Path.Combine(sheets,"001.jpg"),data.ToArray());
}
var trainingScan=TrainingFolderScanner.Scan(trainingRoot);var catalog=new TrainingCatalog(Path.Combine(exportNames,"training.sqlite"));catalog.SaveScan(trainingScan);
if(trainingScan.Count!=2||trainingScan.Any(a=>a.Status!="READY"||a.RatioFamily!=2.5||a.SheetFiles.Count!=1||a.PhotoFiles.Count!=1)||trainingScan[0].TrainingId!="TRN-000001"||trainingScan[1].TrainingId!="TRN-000002")throw new Exception("Bulk training scan failed");
foreach(var item in trainingScan)catalog.SaveSheetMetadata(item);
var trainingStats=catalog.Statistics();if(trainingStats.Albums!=2||trainingStats.Sheets!=2||trainingStats.Photos!=2||trainingStats.NeedsReview!=2||!catalog.NeedsAnalysis(trainingScan[0]))throw new Exception("Training SQLite indexing failed");
using(var segmentationImage=new SKBitmap(500,200))
{
    using var canvas=new SKCanvas(segmentationImage);canvas.Clear(SKColors.White);using var paint=new SKPaint{Color=SKColors.DarkRed};canvas.DrawRect(new SKRect(20,20,220,180),paint);paint.Color=SKColors.DarkBlue;canvas.DrawRect(new SKRect(280,20,480,180),paint);
    if(TrainingSheetAnalyzer.DetectRegions(segmentationImage).Count!=2)throw new Exception("Flattened sheet region detection failed");
}
var matchPhotoPath=Path.Combine(exportNames,"pattern-source.jpg");var matchSheetPath=Path.Combine(exportNames,"pattern-sheet.jpg");
using(var pattern=new SKBitmap(300,200))
{
    using(var canvas=new SKCanvas(pattern)){canvas.Clear(SKColors.Navy);using var brush=new SKPaint();for(int k=0;k<18;k++){brush.Color=new SKColor((byte)(k*13),(byte)(255-k*11),(byte)(k*7));canvas.DrawCircle(15+k*16,25+(k*37)%150,10+k%7,brush);}}
    using(var image=SKImage.FromBitmap(pattern))using(var jpg=image.Encode(SKEncodedImageFormat.Jpeg,95))File.WriteAllBytes(matchPhotoPath,jpg.ToArray());
    using var final=new SKBitmap(600,250);using(var canvas=new SKCanvas(final)){canvas.Clear(SKColors.White);canvas.DrawBitmap(pattern,new SKRect(31,0,269,200),new SKRect(70,20,320,230));}
    using var finalImage=SKImage.FromBitmap(final);using var finalJpg=finalImage.Encode(SKEncodedImageFormat.Jpeg,95);File.WriteAllBytes(matchSheetPath,finalJpg.ToArray());
}
using(var preview=Rendering.LoadUpright(matchPhotoPath,260))
{
    var matchResult=TrainingSheetAnalyzer.Analyze(matchSheetPath,[(matchPhotoPath,preview)]);
    if(matchResult.Regions.Count!=1||matchResult.Regions[0].MatchedPhotoPath!=matchPhotoPath)throw new Exception($"Crop-resistant training photo match failed: regions={matchResult.Regions.Count}, confidence={matchResult.Regions.FirstOrDefault()?.Confidence}, path={matchResult.Regions.FirstOrDefault()?.MatchedPhotoPath}");
}
var analyzed=new SheetAnalysis{SheetPath=trainingScan[0].SheetFiles[0],Status="AUTO MATCHED",Regions=[new DetectedPhotoRegion{Slot=new LayoutSlot{X=0,Y=0,Width=1,Height=1},MatchedPhotoPath=trainingScan[0].PhotoFiles[0],Confidence=.99}]};
catalog.SaveAnalysis(trainingScan[0],[analyzed]);
if(catalog.NeedsAnalysis(trainingScan[0]))throw new Exception("Training analysis fingerprint was not saved");
var trainingSheet=catalog.Sheets(trainingScan[0].SourceFolder).Single();if(!catalog.ValidateSheet(trainingSheet.Id)||catalog.TrainValidatedLayouts()!=1||catalog.Recommend(2.5,1,.5,new[]{1.5})?.Count!=1)throw new Exception("Validated style layout training failed");
catalog.SaveSheetMetadata(trainingScan[0]);catalog.SaveAnalysis(trainingScan[0],[new SheetAnalysis{SheetPath=matchSheetPath,Regions=[]}]);
if(catalog.Sheets(trainingScan[0].SourceFolder).Single().Status!="VALIDATED"||catalog.Slots(trainingSheet.Id).Count!=1)throw new Exception("Rescan erased validated sheet corrections");
File.SetLastWriteTimeUtc(trainingScan[0].SheetFiles[0],DateTime.UtcNow.AddMinutes(2));
var changedScan=TrainingFolderScanner.Scan(trainingRoot);catalog.SaveScan(changedScan);catalog.SaveSheetMetadata(changedScan[0]);
if(catalog.Sheets(changedScan[0].SourceFolder).Single().Status!="NEEDS REVIEW"||catalog.Slots(trainingSheet.Id).Count!=0)throw new Exception("Changed training sheet was not invalidated for review");
foreach(var sheet in album96.Sheets)sheet.Approved=true;
if(catalog.AddApprovedAlbum(album96)!=20||catalog.AddApprovedAlbum(album96)!=0||catalog.TrainValidatedLayouts() is <1 or >21)throw new Exception("Approved album feedback was not saved idempotently");
double portraitScore=LayoutRanker.Score([new LayoutSlot{X=0,Y=0,Width=.2,Height=1}],2.5,.5,.5,[.5]);
double landscapeScore=LayoutRanker.Score([new LayoutSlot{X=0,Y=0,Width=.8,Height=1}],2.5,.5,.5,[.5]);
if(portraitScore>=landscapeScore)throw new Exception("Learned layout orientation ranking failed");
var printAlbum=AlbumEngine.Generate(new AlbumSize{HeightInches=8,WidthInches=16},10,photos.Take(10).Select(p=>p.Path).ToArray(),72);
var printSheet=AlbumRenderService.Build(printAlbum,0);using var printed=SKBitmap.Decode(Rendering.Render(printSheet.Data,printSheet.Layouts,false));
if(printed.Width!=1152||printed.Height!=576||!AlbumEngine.ValidateUsage(printAlbum))throw new Exception("Original-resolution album spread render failed");
var printFolder=Path.Combine(exportNames,"printed");var firstPrint=await AlbumRenderService.ExportAll(printAlbum,printFolder);
var secondPrint=await AlbumRenderService.ExportAll(printAlbum,printFolder);
if(firstPrint.Count!=10||secondPrint.Count!=10||Path.GetFileName(firstPrint[0])!="001.jpg"||Path.GetFileName(secondPrint[0])!="001.jpg"||Path.GetDirectoryName(firstPrint[0])==Path.GetDirectoryName(secondPrint[0])||!File.Exists(firstPrint[0])||!File.Exists(secondPrint[0]))throw new Exception("Album export overwrite protection failed");
int[] expected={0,1,6,10,10,8,8,6,6,5,5,4,4};
for(int n=1;n<=12;n++)
{
    var templates=LayoutEngine.ForCount(n);if(templates.Count!=expected[n])throw new Exception($"Template count {n}: {templates.Count}");
    foreach(var template in templates){if(template.Slots.Count!=n)throw new Exception("Slot count mismatch");foreach(var s in template.Slots)if(s.X<0||s.Y<0||s.Width<=0||s.Height<=0||s.X+s.Width>1.00001||s.Y+s.Height>1.00001)throw new Exception("Invalid normalized geometry");}
    var p=new ProjectData{Photos=photos.Take(n).ToList(),Assignments=Enumerable.Range(0,n).ToList(),Width=2160,Height=2700,Gap=3};var bytes=Rendering.Render(p,templates,true,450);using var rendered=SKBitmap.Decode(bytes);if(rendered.Width!=360||rendered.Height!=450)throw new Exception("Preview dimensions wrong");
    var random=LayoutEngine.ForCount(n,"RandomSpace");if(random.Count<2||random.Any(t=>t.Slots.Count!=n||!t.Id.StartsWith($"random-{n}-")))throw new Exception($"Random space designs missing for {n} photos");
    foreach(var design in random)
    {
        for(int i=0;i<design.Slots.Count;i++)
        {
            var s=design.Slots[i];var rect=Rendering.SlotRect(s,2160,2700,40,20);
            if(s.X<0||s.Y<0||s.Width<=0||s.Height<=0||s.X+s.Width>1.000001||s.Y+s.Height>1.000001||rect.Width<1||rect.Height<1)throw new Exception($"Invalid random space geometry: {design.Name}");
            for(int j=0;j<i;j++){var t=design.Slots[j];if(s.X<t.X+t.Width-.000001&&t.X<s.X+s.Width-.000001&&s.Y<t.Y+t.Height-.000001&&t.Y<s.Y+s.Height-.000001)throw new Exception($"Overlapping random space frames: {design.Name}");}
        }
    }
    if(n is 2 or 3 or 4 or 6 or 7 or 10)
    {
        p.LayoutCollection="RandomSpace";p.Gap=20;p.Margin=40;
        using var randomPreview=SKBitmap.Decode(Rendering.Render(p,random,true,450));
        if(randomPreview.Width!=360||randomPreview.Height!=450)throw new Exception("Random space preview dimensions wrong");
        foreach(int designIndex in Enumerable.Range(0,Math.Min(3,random.Count)))
        {p.LayoutIndex=designIndex;File.WriteAllBytes(Path.Combine(root,$"random-{n}-{designIndex}.png"),Rendering.Render(p,random,true,450));}
    }
}
var savedRandomProject=new ProjectData{Photos=photos.Take(7).ToList(),Assignments=Enumerable.Range(0,7).ToList(),LayoutCollection="RandomSpace",LayoutIndex=1};
var loadedRandomProject=JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(savedRandomProject))!;
var randomEditor=new EditorViewModel();randomEditor.SetData(loadedRandomProject);
if(randomEditor.Data.LayoutCollection!="RandomSpace"||randomEditor.CurrentSlots.Count!=7||randomEditor.Layouts[1].Id!=LayoutEngine.ForCount(7,"RandomSpace")[1].Id)throw new Exception("Random space layout did not survive project save and reopen");
var randomPsdData=new ProjectData{Photos=photos.Take(2).ToList(),Assignments=[0,1],LayoutCollection="RandomSpace",LayoutIndex=1,Width=160,Height=120,Gap=2};
var randomPsdPath=Path.Combine(root,"test-random-layout.psd");PsdExporter.Export(randomPsdData,LayoutEngine.ForCount(2,randomPsdData.LayoutCollection),randomPsdPath);
if(BinaryPrimitives.ReadInt16BigEndian(File.ReadAllBytes(randomPsdPath).AsSpan(70,2))!=3)throw new Exception("Random space PSD lost its photo layers");
var project=new ProjectData{Photos=photos.Take(4).ToList(),Assignments=[3,2,1,0],Unit="Inches",Width=12,Height=15,Dpi=300,Gap=10,Margin=100};project.Photos[1].Transform=new(){Scale=2.5,PanX=-.5,PanY=.75};
var settingsPath=Path.Combine(root,"canvas-settings-test.json");CanvasSettingsStore.Save(project,settingsPath);
var savedCanvas=CanvasSettingsStore.Load(settingsPath)??throw new Exception("Canvas size preferences were not saved");
if(savedCanvas.Unit!="Inches"||savedCanvas.Width!=12||savedCanvas.Height!=15||savedCanvas.Dpi!=300)throw new Exception("Saved canvas size preferences changed");
File.WriteAllText(settingsPath,"{broken json");if(CanvasSettingsStore.Load(settingsPath)!=null)throw new Exception("Corrupt canvas preferences were accepted");
foreach(var preset in new[]{(4,6),(8,12),(9,16),(4,5),(5,5)})if(!CanvasSettingsStore.IsValid(new CanvasSettings{Unit="Inches",Width=preset.Item1,Height=preset.Item2,Dpi=300}))throw new Exception("A requested size preset was rejected");
if(project.PixelWidth!=3600||project.PixelHeight!=4500)throw new Exception("DPI math wrong");var json=JsonSerializer.Serialize(project);var loaded=JsonSerializer.Deserialize<ProjectData>(json)!;if(loaded.Photos[1].Transform.Scale!=2.5||loaded.Assignments[0]!=3||loaded.Margin!=100)throw new Exception("Project roundtrip failed");
var export=new ProjectData{Photos=photos.Take(4).ToList(),Assignments=[0,1,2,3],Width=2160,Height=2700,Gap=3};var output=Rendering.Render(export,LayoutEngine.ForCount(4),false);using var imageOut=SKBitmap.Decode(output);if(imageOut.Width!=2160||imageOut.Height!=2700)throw new Exception("Export dimensions wrong");File.WriteAllBytes(Path.Combine(root,"test-export.jpg"),output);
var layered=new ProjectData{Photos=photos.Take(2).ToList(),Assignments=[0,1],Width=160,Height=120,Gap=4,Dpi=300,Background="#FFE8E8E8"};
var psdPath=Path.Combine(root,"test-layered.psd");PsdExporter.Export(layered,LayoutEngine.ForCount(2),psdPath);
layered.JpegQuality=-1;File.WriteAllBytes(Path.Combine(root,"test-layered-reference.png"),Rendering.Render(layered,LayoutEngine.ForCount(2),false));
var psdBytes=File.ReadAllBytes(psdPath);
if(System.Text.Encoding.ASCII.GetString(psdBytes,0,4)!="8BPS"||BinaryPrimitives.ReadUInt32BigEndian(psdBytes.AsSpan(14,4))!=120||BinaryPrimitives.ReadUInt32BigEndian(psdBytes.AsSpan(18,4))!=160)throw new Exception("Layered PSD header is invalid");
if(BinaryPrimitives.ReadInt16BigEndian(psdBytes.AsSpan(70,2))!=3||!System.Text.Encoding.ASCII.GetString(psdBytes).Contains("Background"))throw new Exception("PSD did not contain separate photo and background layers");
var sinhalaPhoto=Path.Combine(root,"මංගල ඡායාරූපය.jpg");File.Copy(photos[0].Path,sinhalaPhoto,true);
var layeredCustom=new ProjectData{Photos=[new PhotoEntry{Path=sinhalaPhoto,Transform=new(){Scale=1.3,PanX=.4,PanY=-.3}},photos[1]],Assignments=[0,1],Width=160,Height=120,Gap=3,Margin=10,Dpi=300,Background="#80E8E8E8"};
layeredCustom.CustomLayouts[LayoutEngine.ForCount(2)[0].Id]=[new LayoutSlot{X=0,Y=0,Width=.35,Height=.7},new LayoutSlot{X=.5,Y=.15,Width=.4,Height=.85}];
PsdExporter.Export(layeredCustom,LayoutEngine.ForCount(2),Path.Combine(root,"test-layered-custom.psd"));
layeredCustom.JpegQuality=-1;File.WriteAllBytes(Path.Combine(root,"test-layered-custom-reference.png"),Rendering.Render(layeredCustom,LayoutEngine.ForCount(2),false));
var halves=LayoutEngine.ForCount(2)[0].Slots;var left=Rendering.SlotRect(halves[0],6000,7500,1000,500);var right=Rendering.SlotRect(halves[1],6000,7500,1000,500);if(Math.Abs(right.Left-left.Right-500)>0.01||left.Left!=1000||right.Right!=5000)throw new Exception("Large gap/margin geometry wrong");
var invalid=new ProjectData{Photos=photos.Take(2).ToList(),Assignments=[0,1],Width=2160,Height=2700,Gap=500,Margin=1000};try{Rendering.Render(invalid,LayoutEngine.ForCount(2),true);throw new Exception("Invalid geometry was accepted");}catch(InvalidOperationException){}
var two=LayoutEngine.ForCount(2)[0];var divider=LayoutResizeEngine.Find(two.Slots,.5,.5,.01,.01)??throw new Exception("Divider not found");if(!divider.Vertical||divider.Before.Count!=1||divider.After.Count!=1)throw new Exception("Wrong divider selection");
var uneven=new List<LayoutSlot>{new(){X=0,Y=0,Width=.3,Height=1},new(){X=.55,Y=0,Width=.35,Height=1}};
if(!EqualGapEngine.TryEqualize(uneven,2160,2700,0,40,out var even)||Math.Abs(even[1].X-.3)>1e-8)throw new Exception("Manual photo gap was not equalized");
var evenLeft=Rendering.SlotRect(even[0],2160,2700,0,40);var evenRight=Rendering.SlotRect(even[1],2160,2700,0,40);
if(Math.Abs(evenRight.Left-evenLeft.Right-40)>0.01||Math.Abs(uneven[1].X-.55)>1e-8)throw new Exception("Equal gap geometry or original snapshot was changed incorrectly");
var unevenGrid=new List<LayoutSlot>{new(){X=0,Y=0,Width=.4,Height=.4},new(){X=.55,Y=0,Width=.35,Height=.4},new(){X=0,Y=.55,Width=.4,Height=.35},new(){X=.55,Y=.55,Width=.35,Height=.35}};
if(!EqualGapEngine.TryEqualize(unevenGrid,2160,2700,0,20,out var evenGrid)||Math.Abs(evenGrid[1].X-.4)>1e-8||Math.Abs(evenGrid[2].Y-.4)>1e-8||Math.Abs(evenGrid[3].X-.4)>1e-8||Math.Abs(evenGrid[3].Y-.4)>1e-8)throw new Exception("Two-dimensional manual gaps were not equalized");
if(!LayoutResizeEngine.TryMove(two.Slots,divider,.7,2160,2700,0,3,out var resized))throw new Exception("Divider did not move");if(Math.Abs(resized[0].Width-.7)>1e-8||Math.Abs(resized[1].X-.7)>1e-8||Math.Abs(resized[1].Width-.3)>1e-8)throw new Exception("Neighbor did not resize with photo");
var four=LayoutEngine.ForCount(4)[0];var gridDivider=LayoutResizeEngine.Find(four.Slots,.5,.25,.01,.01)??throw new Exception("Grid divider not found");if(gridDivider.Before.Count!=1||gridDivider.After.Count!=1)throw new Exception("Individual row divider selection failed");if(!LayoutResizeEngine.TryMove(four.Slots,gridDivider,.65,2160,2700,0,3,out var gridResized))throw new Exception("Grid resize failed");if(Math.Abs(gridResized[0].Width-.65)>1e-8||Math.Abs(gridResized[1].X-.65)>1e-8||Math.Abs(gridResized[2].Width-.5)>1e-8)throw new Exception("Only the selected row and its neighbor should resize");
var one=LayoutEngine.ForCount(1)[0].Slots;
foreach(var edge in new[]{(x:0.0,y:.5,vertical:true,target:.2),(x:1.0,y:.5,vertical:true,target:.8),(x:.5,y:0.0,vertical:false,target:.2),(x:.5,y:1.0,vertical:false,target:.8)})
{
    var handle=LayoutResizeEngine.Find(one,edge.x,edge.y,.01,.01)??throw new Exception("An outer frame edge was not found");if(handle.Vertical!=edge.vertical)throw new Exception("Wrong resize direction");
    if(!LayoutResizeEngine.TryMove(one,handle,edge.target,2160,2700,0,3,out var resizedEdgeSlots))throw new Exception("An outer frame edge did not resize");
    double movedEdge=edge.vertical?(edge.x==0?resizedEdgeSlots[0].X:resizedEdgeSlots[0].X+resizedEdgeSlots[0].Width):(edge.y==0?resizedEdgeSlots[0].Y:resizedEdgeSlots[0].Y+resizedEdgeSlots[0].Height);
    if(Math.Abs(movedEdge-edge.target)>1e-8)throw new Exception("Outer edge moved to wrong coordinate");
}
var openSlots=new List<LayoutSlot>{new(){X=0,Y=0,Width=.25,Height=.4},new(){X=.5,Y=0,Width=.25,Height=.4}};
if(!FramePlacementEngine.TryTranslate(openSlots,0,.65,.5,out var movedFrame)||Math.Abs(movedFrame[0].X-.65)>1e-8||Math.Abs(movedFrame[0].Y-.5)>1e-8)throw new Exception("Frame could not move into open background");
if(Math.Abs(openSlots[0].X)>1e-8||Math.Abs(openSlots[0].Y)>1e-8)throw new Exception("Live move mutated its original snapshot");
if(FramePlacementEngine.TryTranslate(openSlots,0,.5,0,out _))throw new Exception("Overlapping frame move was accepted");
if(!FramePlacementEngine.PreviewTranslate(openSlots,0,.5,0,out var movingPreview,600,750,0)||Math.Abs(movingPreview[0].X-.5)>1e-8)throw new Exception("Ordinary drag did not preview frame movement over another frame");
if(Math.Abs(openSlots[0].X)>1e-8)throw new Exception("Frame drag preview changed its original geometry");
var groupSlots=new List<LayoutSlot>{new(){X=.05,Y=.05,Width=.2,Height=.2},new(){X=.30,Y=.05,Width=.2,Height=.2},new(){X=.55,Y=.55,Width=.2,Height=.2}};
if(!FramePlacementEngine.TryTranslateMany(groupSlots,new[]{0,1},.05,.25,out var groupMoved,600,750,0)||Math.Abs(groupMoved[0].X-.10)>1e-8||Math.Abs(groupMoved[1].X-.35)>1e-8||Math.Abs(groupMoved[0].Y-.30)>1e-8||Math.Abs(groupMoved[1].Y-.30)>1e-8)throw new Exception("Selected photo frames did not move together");
if(FramePlacementEngine.TryTranslateMany(groupSlots,new[]{0,1},.25,.45,out _,600,750,0))throw new Exception("Group move overlapped an unselected photo");
if(!FramePlacementEngine.PreviewTranslateMany(groupSlots,new[]{0,1},.25,.45,out var groupPreview,600,750,0)||Math.Abs(groupPreview[0].X-.30)>1e-8)throw new Exception("Group move did not update the live preview");
if(Math.Abs(groupSlots[0].X-.05)>1e-8)throw new Exception("Group drag mutated its original geometry");
if(!FramePlacementEngine.TryTranslateFree(groupSlots,new[]{0,1},.25,.45,out var freeMoved,600,750,0)||Math.Abs(freeMoved[1].X-.55)>1e-8||Math.Abs(freeMoved[1].Width-groupSlots[1].Width)>1e-8)throw new Exception("Random space photo could not move freely over another frame without resizing");
var stackData=new ProjectData{Photos=photos.Take(2).ToList(),Assignments=[0,1],LayoutCollection="RandomSpace",Width=160,Height=120,Gap=0};
var stackTemplate=LayoutEngine.ForCount(2,"RandomSpace")[0];stackData.CustomLayouts[stackTemplate.Id]=[new LayoutSlot{X=.1,Y=.1,Width=.6,Height=.6},new LayoutSlot{X=.3,Y=.3,Width=.6,Height=.6}];
LayerStackEngine.BringToFront(stackData,2,new[]{0});if(!LayerStackEngine.Order(stackData,2).SequenceEqual(new[]{1,0}))throw new Exception("Moved random space photo was not brought to front");
var stackRoundtrip=JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(stackData))!;if(!stackRoundtrip.LayerOrder.SequenceEqual(new[]{1,0}))throw new Exception("Random space layer order did not persist");
using(var stackImage=SKBitmap.Decode(Rendering.Render(stackRoundtrip,LayoutEngine.ForCount(2,"RandomSpace"),false)))if(stackImage.GetPixel(70,55).Red!=0)throw new Exception("Random space front photo was not rendered above the overlapping photo");
PsdExporter.Export(stackRoundtrip,LayoutEngine.ForCount(2,"RandomSpace"),Path.Combine(root,"test-random-overlap.psd"));
if(!FramePlacementEngine.TryCenterAt(openSlots,1,.65,.75,out var thumbnailPlaced)||Math.Abs(thumbnailPlaced[1].Y-.55)>1e-8)throw new Exception("Thumbnail could not place a frame in background");
var centerSlots=new List<LayoutSlot>{new(){X=0,Y=0,Width=.2,Height=.2},new(){X=.8,Y=.8,Width=.2,Height=.2}};
if(!FramePlacementEngine.TryCenterAt(centerSlots,0,.5,.5,out var centeredPhoto,2160,2700,0)||Math.Abs(centeredPhoto[0].X-.4)>1e-8||Math.Abs(centeredPhoto[0].Y-.4)>1e-8)throw new Exception("Selected photo frame could not center in the collage");
if(Math.Abs(centerSlots[0].X)>1e-8||Math.Abs(centerSlots[0].Y)>1e-8)throw new Exception("Center action mutated original layout");
var blockedCenter=new List<LayoutSlot>{new(){X=0,Y=0,Width=.2,Height=.2},new(){X=.4,Y=.4,Width=.2,Height=.2}};
if(FramePlacementEngine.TryCenterAt(blockedCenter,0,.5,.5,out _,2160,2700,0))throw new Exception("Center action overlapped an occupied frame");
if(!FramePlacementEngine.TryTranslate(openSlots,0,-.2,.5,out var marginPlaced,600,750,100)||Math.Abs(marginPlaced[0].X+.2)>1e-8)throw new Exception("Frame could not move into visible outer margin");
var marginFrame=Rendering.SlotRect(marginPlaced[0],600,750,100,3);if(marginFrame.Left<0||marginFrame.Right>600)throw new Exception("Frame placed outside canvas");
var whiteBand=new List<LayoutSlot>{new(){X=0,Y=0,Width=.5,Height=.34},new(){X=.5,Y=0,Width=.5,Height=.34},new(){X=0,Y=.66,Width=1.0/3,Height=.34},new(){X=1.0/3,Y=.66,Width=1.0/3,Height=.34},new(){X=2.0/3,Y=.66,Width=1.0/3,Height=.34}};
if(!FramePlacementEngine.TryFitInOpenArea(whiteBand,4,.82,.5,out var fittedBand,2160,2700,0,3))throw new Exception("Photo could not move into the screenshot-like white band");
if(fittedBand[4].Y<.34-1e-7||fittedBand[4].Y+fittedBand[4].Height>.66+1e-7||fittedBand[4].Height>=whiteBand[4].Height)throw new Exception("Frame did not resize to fit the white band");
if(Math.Abs(whiteBand[4].Y-.66)>1e-8)throw new Exception("Fitting mutated original layout");
var placedProject=new ProjectData{Photos=photos.Take(2).ToList(),Assignments=[0,1],Width=600,Height=750};placedProject.CustomLayouts[two.Id]=movedFrame;
var placedReload=JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(placedProject))!;if(Math.Abs(LayoutResizeEngine.Slots(placedReload,two)[0].X-.65)>1e-8)throw new Exception("Moved frame did not persist");
using var placedImage=SKBitmap.Decode(Rendering.Render(placedReload,LayoutEngine.ForCount(2),false));if(placedImage.Width!=600||placedImage.Height!=750)throw new Exception("Moved frame export failed");
var custom=new ProjectData{Photos=photos.Take(2).ToList(),Assignments=[0,1],Width=600,Height=750};custom.CustomLayouts[two.Id]=resized;var customJson=JsonSerializer.Serialize(custom);var reopened=JsonSerializer.Deserialize<ProjectData>(customJson)!;if(Math.Abs(LayoutResizeEngine.Slots(reopened,two)[0].Width-.7)>1e-8)throw new Exception("Resized frames not saved");using var customOutput=SKBitmap.Decode(Rendering.Render(reopened,LayoutEngine.ForCount(2),false));if(customOutput.Width!=600||customOutput.Height!=750)throw new Exception("Custom layout export failed");
var editor=new EditorViewModel();editor.SetData(reopened);editor.ClearPhotos();if(editor.Data.Photos.Count!=0||editor.Data.Assignments.Count!=0||editor.Data.CustomLayouts.Count!=0)throw new Exception("Remove All Photos did not clear the collage");if(!File.Exists(photos[0].Path))throw new Exception("Remove All Photos touched source files");if(!editor.Undo()||editor.Data.Photos.Count!=2||editor.Data.CustomLayouts.Count!=1)throw new Exception("Remove All Photos undo failed");
var moveRoot=Path.Combine(root,"used-photo-move-tests",Guid.NewGuid().ToString("N"));var folderA=Path.Combine(moveRoot,"A");var folderB=Path.Combine(moveRoot,"B");Directory.CreateDirectory(folderA);Directory.CreateDirectory(folderB);
var a=Path.Combine(folderA,"photo.jpg");var b=Path.Combine(folderB,"photo.jpg");File.Copy(photos[0].Path,a);File.Copy(photos[1].Path,b);Directory.CreateDirectory(Path.Combine(folderA,"used photos"));var occupied=Path.Combine(folderA,"used photos","photo.jpg");File.WriteAllText(occupied,"existing");
var captured=UsedPhotoMover.Capture([a,b]);var movedResults=UsedPhotoMover.Move(captured);if(movedResults.Count!=2||movedResults.Any(r=>!r.Succeeded)||File.Exists(a)||File.Exists(b)||File.ReadAllText(occupied)!="existing")throw new Exception("Used photo move or no-overwrite rule failed");if(!File.Exists(Path.Combine(folderA,"used photos","photo_1.jpg"))||!File.Exists(Path.Combine(folderB,"used photos","photo.jpg")))throw new Exception("Used photo folders incorrect");
var changed=Path.Combine(folderB,"changed.jpg");File.Copy(photos[2].Path,changed);var before=UsedPhotoMover.Capture([changed]);File.AppendAllText(changed,"changed after snapshot");var rejected=UsedPhotoMover.Move(before);if(rejected[0].Succeeded||!File.Exists(changed))throw new Exception("Changed source was moved");
var cleanupVm=new EditorViewModel();cleanupVm.SetData(new ProjectData{Photos=[new(){Path=a},new(){Path=changed}],Assignments=[0,1],MoveUsedPhotosAfterExport=true});cleanupVm.RemoveMovedPhotos([a]);if(cleanupVm.Data.Photos.Count!=1||cleanupVm.Data.Photos[0].Path!=changed||cleanupVm.CanUndo)throw new Exception("Moved photos were not removed safely from editor");if(!JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(cleanupVm.Data))!.MoveUsedPhotosAfterExport)throw new Exception("Export move option not saved");
Console.WriteLine("PASS: normal and random space layouts for 1-12 photos, freeform and group frame movement, stacking order persistence, preview rendering, layered PSD export, saved canvas defaults, requested size presets, project persistence, equal photo gaps, centered photo frame and collision guard, normal frame-drag preview, four-sided frame resize, fitting a photo into a white band, undoable clear, collision-safe used-photo moves, changed-source protection, and post-export editor cleanup");
