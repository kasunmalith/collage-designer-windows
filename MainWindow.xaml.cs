using Microsoft.Win32;
using SkiaSharp;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using DataFormats = System.Windows.DataFormats;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
namespace CollageDesigner;
public partial class MainWindow : Window
{
    static readonly (double Width,double Height)[] CanvasPresets=[(4,6),(8,12),(9,16),(4,5),(5,5)];
    readonly EditorViewModel vm=new();readonly SemaphoreSlim previewSemaphore=new(1,1);readonly HashSet<int> selectedSlots=[];List<int> dragGroup=[],resizeGroup=[];bool updating,light,dragging,panning,resizing,resizeRecorded,resizeTrailing,exporting,cropDragging,dragHadCustom,thumbnailDragActive,thumbnailHadCustom,thumbnailPreviewChanged;int dragSlot=-1,dragShiftPreviewTarget=-1;double resizeStart;Point startPoint,lastPoint;LayoutDivider? activeDivider,hoveredDivider;List<LayoutSlot>? resizeOriginal,dragOriginalSlots,thumbnailOriginalSlots;List<int>? dragOriginalAssignments;CancellationTokenSource? previewToken;string? dragLayoutId,thumbnailLayoutId,lastExportPath;
    public MainWindow(){InitializeComponent();if(CanvasSettingsStore.Load() is { } saved){vm.Data.Unit=saved.Unit;vm.Data.Width=saved.Width;vm.Data.Height=saved.Height;vm.Data.Dpi=saved.Dpi;}var exportSettings=ExportSettingsStore.Load();lastExportPath=exportSettings.LastExportPath;updating=true;QuickSaveCheck.IsChecked=exportSettings.QuickSaveEnabled;updating=false;RefreshUi();}
    void PersistCanvasSettings(){try{CanvasSettingsStore.Save(vm.Data);}catch(Exception ex){Status.Text=$"Could not save canvas defaults: {ex.Message}";}}
    void PersistExportSettings(){try{ExportSettingsStore.Save(new ExportSettings{LastExportPath=lastExportPath,QuickSaveEnabled=QuickSaveCheck.IsChecked==true});}catch(Exception ex){Status.Text=$"Could not save export defaults: {ex.Message}";}}
    void Window_Closing(object? sender,System.ComponentModel.CancelEventArgs e){PersistCanvasSettings();PersistExportSettings();}
    static BitmapImage Bitmap(byte[] bytes){var b=new BitmapImage();using var ms=new MemoryStream(bytes);b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.StreamSource=ms;b.EndInit();b.Freeze();return b;}
    void Error(Exception ex)=>MessageBox.Show(this,ex is OutOfMemoryException?"Not enough memory for this canvas. Try a smaller export size.":ex.Message,"Collage Designer",MessageBoxButton.OK,MessageBoxImage.Warning);
    void Busy(string message,bool value){Status.Text=message;Progress.Visibility=value?Visibility.Visible:Visibility.Collapsed;if(!value)Progress.Value=0;}
    static string? PickJpg(){var d=new OpenFileDialog{Filter="JPEG photos|*.jpg;*.jpeg",CheckFileExists=true};return d.ShowDialog()==true?d.FileName:null;}
    async Task AddPhotos(IEnumerable<string> paths)
    {
        var chosen=paths.Where(p=>new[]{".jpg",".jpeg"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Distinct(StringComparer.OrdinalIgnoreCase).Where(p=>!vm.Data.Photos.Any(x=>string.Equals(x.Path,p,StringComparison.OrdinalIgnoreCase))).Take(12-vm.Data.Photos.Count).ToList();
        if(chosen.Count==0)return;vm.Remember();Busy("Loading photos…",true);int success=0;
        foreach(var path in chosen)
        {
            try{var result=await Task.Run(()=>{var size=Rendering.ReadSize(path);var preview=Rendering.MakePreview(path);return(size,preview);});vm.Data.Photos.Add(new(){Path=path,Width=result.size.Width,Height=result.size.Height,Preview=result.preview});success++;Progress.Value=100.0*success/chosen.Count;}
            catch(Exception ex){MessageBox.Show(this,$"Could not import {Path.GetFileName(path)}: {ex.Message}","Import error",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        vm.Normalize();vm.Data.LayoutIndex=0;vm.Data.LayerOrder.Clear();Busy($"Loaded {success} photo(s).",false);RefreshUi();QueuePreview();
    }
    async void Import_Click(object sender,RoutedEventArgs e){var d=new OpenFileDialog{Multiselect=true,Filter="JPEG photos|*.jpg;*.jpeg",CheckFileExists=true};if(d.ShowDialog()==true)await AddPhotos(d.FileNames);}
    async void Window_Drop(object sender,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop)){var files=(string[])e.Data.GetData(DataFormats.FileDrop);await AddPhotos(files);}}
    void RefreshUi()
    {
        updating=true;var d=vm.Data;var layouts=vm.Layouts;if(vm.SelectedSlot<0||vm.SelectedSlot>=d.Assignments.Count){selectedSlots.Clear();vm.SelectedSlot=-1;}else{selectedSlots.RemoveWhere(i=>i<0||i>=d.Assignments.Count);if(!selectedSlots.Contains(vm.SelectedSlot)){selectedSlots.Clear();selectedSlots.Add(vm.SelectedSlot);}}CanvasPresetBox.SelectedIndex=PresetIndex(d);UnitBox.SelectedIndex=d.Unit=="Inches"?1:0;WidthBox.Text=d.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);HeightBox.Text=d.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);DpiBox.Text=d.Dpi.ToString();LayoutCategoryBox.SelectedIndex=d.LayoutCollection=="RandomSpace"?1:0;LayoutOptionBox.ItemsSource=layouts;LayoutOptionBox.SelectedIndex=layouts.Count>0?d.LayoutIndex:-1;GapSlider.Value=d.Gap;MarginSlider.Value=d.Margin;QualitySlider.Value=d.JpegQuality;GapBox.Text=d.Gap.ToString();MarginBox.Text=d.Margin.ToString();MoveUsedPhotosCheck.IsChecked=d.MoveUsedPhotosAfterExport;QualityValue.Text=d.JpegQuality.ToString();BackgroundBox.SelectedIndex=d.Background.ToUpperInvariant() switch{"#FFFFFFFF"=>0,"#FFE8E8E8"=>1,"#FF000000"=>2,_=>3};
        CountText.Text=$"{d.Photos.Count} / 12";ResolutionText.Text=$"{d.PixelWidth:N0} × {d.PixelHeight:N0} px";LayoutText.Text=layouts.Count>0?$"{d.LayoutIndex+1} / {layouts.Count}  ·  {layouts[d.LayoutIndex].Name}":"Import photos";
        PhotoList.Items.Clear();for(int i=0;i<d.Photos.Count;i++){var p=d.Photos[i];var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,3,0,3)};if(p.Preview!=null)row.Children.Add(new Image{Source=Bitmap(p.Preview),Width=55,Height=55,Stretch=Stretch.UniformToFill});row.Children.Add(new TextBlock{Text=$"{i+1}. {Path.GetFileName(p.Path)}\n{p.Width} × {p.Height}",Width=145,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(8,6,0,0)});PhotoList.Items.Add(new ListBoxItem{Content=row,Tag=i,Background=Brushes.Transparent});}
        foreach(int slot in selectedSlots)if(slot<d.Assignments.Count&&d.Assignments[slot]>=0&&d.Assignments[slot]<PhotoList.Items.Count)((ListBoxItem)PhotoList.Items[d.Assignments[slot]]).IsSelected=true;
        SelectedText.Text=vm.SelectedPhoto is { } photo?Path.GetFileName(photo.Path):"Click a photo in the collage";CenterPhotoButton.IsEnabled=vm.SelectedPhoto!=null;ZoomSlider.Value=vm.SelectedPhoto?.Transform.Scale??1;PreviewLabel.Text=$"{d.PixelWidth:N0} × {d.PixelHeight:N0}  ·  {vm.Layouts.Count switch{0=>"No layout",_=>vm.Layouts[d.LayoutIndex].Name}}";UpdateEqualGapsButton();updating=false;DrawSelection();
    }
    void UpdateEqualGapsButton()=>EqualGapsButton.Visibility=vm.Data.Photos.Count>1&&vm.Layouts.Count>0&&vm.Data.CustomLayouts.ContainsKey(vm.Layouts[vm.Data.LayoutIndex].Id)?Visibility.Visible:Visibility.Collapsed;
    void EqualGaps_Click(object sender,RoutedEventArgs e)
    {
        if(vm.Layouts.Count==0)return;
        if(!EqualGapEngine.TryEqualize(vm.CurrentSlots,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin,vm.Data.Gap,out var equalized))
        {Status.Text="These frames cannot be moved to equalize their gaps without overlap or leaving the canvas.";return;}
        vm.Remember();vm.Data.CustomLayouts[vm.Layouts[vm.Data.LayoutIndex].Id]=equalized;
        DrawSelection();QueuePreview();Status.Text="Photo gaps equalized. Undo is available.";
    }
    void QueuePreview()
    {
        previewToken?.Cancel();if(vm.Layouts.Count==0){PreviewImage.Source=null;DrawSelection();return;}
        var tokenSource=new CancellationTokenSource();previewToken=tokenSource;
        var snapshot=System.Text.Json.JsonSerializer.Deserialize<ProjectData>(System.Text.Json.JsonSerializer.Serialize(vm.Data))!;
        snapshot.PreviewFrontSlot=vm.Data.PreviewFrontSlot;snapshot.PreviewFrontSlots=vm.Data.PreviewFrontSlots.ToList();
        for(int i=0;i<snapshot.Photos.Count;i++)snapshot.Photos[i].Preview=vm.Data.Photos[i].Preview;
        _=RenderPreview(snapshot,tokenSource.Token);
    }
    async Task RenderPreview(ProjectData snapshot,CancellationToken token)
    {
        bool acquired=false;
        try{await Task.Delay(16,token);await previewSemaphore.WaitAsync(token);acquired=true;if(token.IsCancellationRequested)return;var bytes=await Task.Run(()=>Rendering.Render(snapshot,LayoutEngine.ForCount(snapshot.Photos.Count,snapshot.LayoutCollection),true));if(token.IsCancellationRequested)return;PreviewImage.Source=Bitmap(bytes);FitPreview();DrawSelection();}
        catch(OperationCanceledException){}
        catch(Exception ex){if(!token.IsCancellationRequested)Status.Text=$"Preview error: {ex.Message}";}
        finally{if(acquired)previewSemaphore.Release();}
    }
    void FitPreview(){int w=vm.Data.PixelWidth,h=vm.Data.PixelHeight;if(w<=0||h<=0)return;double factor=Math.Min((PreviewHost.ActualWidth-30)/w,(PreviewHost.ActualHeight-30)/h);factor=Math.Max(.01,factor);PreviewImage.Width=w*factor;PreviewImage.Height=h*factor;HitCanvas.Width=PreviewImage.Width;HitCanvas.Height=PreviewImage.Height;DrawSelection();}
    void PreviewHost_SizeChanged(object sender,SizeChangedEventArgs e)=>FitPreview();
    Rect UiRect(LayoutSlot slot){var r=Rendering.SlotRect(slot,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin,vm.Data.Gap);double sx=HitCanvas.Width/vm.Data.PixelWidth,sy=HitCanvas.Height/vm.Data.PixelHeight;return new Rect(r.Left*sx,r.Top*sy,r.Width*sx,r.Height*sy);}
    Rect UiRect(int slot)=>UiRect(vm.CurrentSlots[slot]);
    int Hit(Point p,IReadOnlyList<LayoutSlot>? slots=null){if(vm.Layouts.Count==0)return -1;slots??=vm.CurrentSlots;var order=vm.Data.LayoutCollection=="RandomSpace"?LayerStackEngine.Order(vm.Data,slots.Count).AsEnumerable().Reverse():Enumerable.Range(0,slots.Count);foreach(int i in order)if(UiRect(slots[i]).Contains(p))return i;return -1;}
    Point Normalized(Point p){double innerW=vm.Data.PixelWidth-2.0*vm.Data.Margin,innerH=vm.Data.PixelHeight-2.0*vm.Data.Margin;return new Point((p.X*vm.Data.PixelWidth/HitCanvas.Width-vm.Data.Margin)/innerW,(p.Y*vm.Data.PixelHeight/HitCanvas.Height-vm.Data.Margin)/innerH);}
    bool InsideCanvas(Point p)=>p.X>=0&&p.Y>=0&&p.X<=HitCanvas.Width&&p.Y<=HitCanvas.Height;
    LayoutDivider? DividerAt(Point p)
    {
        if(vm.Layouts.Count==0||HitCanvas.Width<=0||HitCanvas.Height<=0||vm.Data.PixelWidth<=2*vm.Data.Margin||vm.Data.PixelHeight<=2*vm.Data.Margin)return null;
        if(vm.Data.LayoutCollection=="RandomSpace")
        {
            int visible=Hit(p);
            IEnumerable<int> candidates=visible>=0?[visible]:LayerStackEngine.Order(vm.Data,vm.CurrentSlots.Count).AsEnumerable().Reverse();
            foreach(int i in candidates)
            {
                var rect=UiRect(i);const double tolerance=5;
                if(p.X<rect.Left-tolerance||p.X>rect.Right+tolerance||p.Y<rect.Top-tolerance||p.Y>rect.Bottom+tolerance)continue;
                double[] distances=[Math.Abs(p.X-rect.Left),Math.Abs(p.X-rect.Right),Math.Abs(p.Y-rect.Top),Math.Abs(p.Y-rect.Bottom)];
                int side=Array.IndexOf(distances,distances.Min());if(distances[side]>tolerance)continue;
                var slot=vm.CurrentSlots[i];bool vertical=side<2;
                return new LayoutDivider{Vertical=vertical,Position=side switch{0=>slot.X,1=>slot.X+slot.Width,2=>slot.Y,_=>slot.Y+slot.Height},From=vertical?slot.Y:slot.X,To=vertical?slot.Y+slot.Height:slot.X+slot.Width,Before=(side is 1 or 3)?[i]:[],After=(side is 0 or 2)?[i]:[]};
            }
            return null;
        }
        var n=Normalized(p);return LayoutResizeEngine.Find(vm.CurrentSlots,n.X,n.Y,9*vm.Data.PixelWidth/(HitCanvas.Width*(vm.Data.PixelWidth-2.0*vm.Data.Margin)),9*vm.Data.PixelHeight/(HitCanvas.Height*(vm.Data.PixelHeight-2.0*vm.Data.Margin)));
    }
    void DrawSelection()
    {
        if(HitCanvas==null)return;HitCanvas.Children.Clear();
        if(vm.Layouts.Count>0)foreach(int selected in selectedSlots.Where(i=>i>=0&&i<vm.CurrentSlots.Count)){var r=UiRect(selected);var rect=new Rectangle{Width=r.Width,Height=r.Height,Stroke=new SolidColorBrush(System.Windows.Media.Color.FromRgb(255,190,108)),StrokeThickness=2,IsHitTestVisible=false};Canvas.SetLeft(rect,r.Left);Canvas.SetTop(rect,r.Top);HitCanvas.Children.Add(rect);}
        var guide=activeDivider??hoveredDivider;if(guide==null)return;
        double left=vm.Data.Margin*HitCanvas.Width/vm.Data.PixelWidth,top=vm.Data.Margin*HitCanvas.Height/vm.Data.PixelHeight;
        double iw=(vm.Data.PixelWidth-2.0*vm.Data.Margin)*HitCanvas.Width/vm.Data.PixelWidth,ih=(vm.Data.PixelHeight-2.0*vm.Data.Margin)*HitCanvas.Height/vm.Data.PixelHeight;
        var line=new Line{Stroke=Brushes.Orange,StrokeThickness=3,IsHitTestVisible=false};
        if(guide.Vertical){line.X1=line.X2=left+guide.Position*iw;line.Y1=top+guide.From*ih;line.Y2=top+guide.To*ih;}
        else{line.Y1=line.Y2=top+guide.Position*ih;line.X1=left+guide.From*iw;line.X2=left+guide.To*iw;}
        HitCanvas.Children.Add(line);
    }
    bool Fits(int gap,int margin,int layoutIndex,int width,int height,IReadOnlyList<LayoutTemplate>? layouts=null)
    {
        if(width<=2*margin||height<=2*margin)return false;
        layouts??=vm.Layouts;if(layouts.Count==0)return true;
        return LayoutResizeEngine.Slots(vm.Data,layouts[layoutIndex]).All(s=>{var r=Rendering.SlotRect(s,width,height,margin,gap);return r.Width>=1&&r.Height>=1&&r.Left>=-.5&&r.Top>=-.5&&r.Right<=width+.5&&r.Bottom<=height+.5;});
    }
    void SelectLayout(int next)
    {
        if(next<0||next>=vm.Layouts.Count||next==vm.Data.LayoutIndex)return;
        if(!Fits(vm.Data.Gap,vm.Data.Margin,next,vm.Data.PixelWidth,vm.Data.PixelHeight)){Status.Text="Gap and margin are too large for this layout. Reduce them first.";updating=true;LayoutOptionBox.SelectedIndex=vm.Data.LayoutIndex;updating=false;return;}
        vm.Remember();vm.Data.LayoutIndex=next;vm.SelectedSlot=-1;RefreshUi();QueuePreview();
    }
    void ChangeLayout(int delta){if(vm.Layouts.Count<2)return;SelectLayout((vm.Data.LayoutIndex+delta+vm.Layouts.Count)%vm.Layouts.Count);}
    void LayoutCategory_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(updating||LayoutCategoryBox==null)return;
        string category=LayoutCategoryBox.SelectedIndex==1?"RandomSpace":"Normal";
        if(category==vm.Data.LayoutCollection)return;
        var options=LayoutEngine.ForCount(vm.Data.Photos.Count,category);
        if(options.Count>0&&!Fits(vm.Data.Gap,vm.Data.Margin,0,vm.Data.PixelWidth,vm.Data.PixelHeight,options))
        {Status.Text="Gap and margin are too large for this layout collection.";updating=true;LayoutCategoryBox.SelectedIndex=vm.Data.LayoutCollection=="RandomSpace"?1:0;updating=false;return;}
        vm.Remember();vm.Data.LayoutCollection=category;vm.Data.LayoutIndex=0;vm.SelectedSlot=-1;RefreshUi();QueuePreview();
    }
    void LayoutOption_Changed(object sender,SelectionChangedEventArgs e){if(!updating&&LayoutOptionBox!=null)SelectLayout(LayoutOptionBox.SelectedIndex);}
    void Prev_Click(object sender,RoutedEventArgs e)=>ChangeLayout(-1);void Next_Click(object sender,RoutedEventArgs e)=>ChangeLayout(1);
    void ClearSelection(){selectedSlots.Clear();vm.SelectedSlot=-1;hoveredDivider=null;RefreshUi();}
    void PreviewHost_Down(object sender,MouseButtonEventArgs e){ClearSelection();e.Handled=true;}
    void Canvas_Down(object sender,MouseButtonEventArgs e)
    {
        HitCanvas.Focus();
        Point p=e.GetPosition(HitCanvas);bool shift=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);activeDivider=shift?null:DividerAt(p);
        if(activeDivider!=null){resizeOriginal=LayoutResizeEngine.Copy(vm.CurrentSlots);resizing=true;resizeRecorded=false;int hit=Hit(p);if(hit<0)hit=activeDivider.Before.Concat(activeDivider.After).FirstOrDefault(selectedSlots.Contains,-1);if(hit<0)hit=activeDivider.Before.Concat(activeDivider.After).FirstOrDefault(-1);resizeGroup=selectedSlots.Count>1&&hit>=0&&selectedSlots.Contains(hit)?selectedSlots.Order().ToList():hit>=0?[hit]:[];var n=Normalized(p);resizeStart=activeDivider.Vertical?n.X:n.Y;if(hit>=0){var s=resizeOriginal[hit];double first=activeDivider.Vertical?s.X:s.Y,last=activeDivider.Vertical?s.X+s.Width:s.Y+s.Height;resizeTrailing=Math.Abs(resizeStart-last)<Math.Abs(resizeStart-first);}HitCanvas.Cursor=activeDivider.Vertical?Cursors.SizeWE:Cursors.SizeNS;HitCanvas.CaptureMouse();DrawSelection();e.Handled=true;return;}
        int i=Hit(p);if(i<0){ClearSelection();e.Handled=true;return;}
        if(shift){if(!selectedSlots.Add(i))selectedSlots.Remove(i);vm.SelectedSlot=selectedSlots.Count==0?-1:selectedSlots.Contains(i)?i:selectedSlots.First();RefreshUi();e.Handled=true;return;}
        if(!selectedSlots.Contains(i)){selectedSlots.Clear();selectedSlots.Add(i);}
        vm.SelectedSlot=i;RefreshUi();dragging=true;panning=false;cropDragging=Keyboard.Modifiers.HasFlag(ModifierKeys.Control);dragSlot=i;dragGroup=cropDragging?[i]:selectedSlots.Order().ToList();dragOriginalSlots=LayoutResizeEngine.Copy(vm.CurrentSlots);dragOriginalAssignments=vm.Data.Assignments.ToList();dragShiftPreviewTarget=-1;dragLayoutId=vm.Layouts[vm.Data.LayoutIndex].Id;dragHadCustom=vm.Data.CustomLayouts.ContainsKey(dragLayoutId);startPoint=lastPoint=p;HitCanvas.Cursor=cropDragging?Cursors.Hand:Cursors.SizeAll;HitCanvas.CaptureMouse();e.Handled=true;
    }
    void Canvas_Move(object sender,MouseEventArgs e)
    {
        Point p=e.GetPosition(HitCanvas);
        if(resizing&&activeDivider!=null&&resizeOriginal!=null)
        {
            var n=Normalized(p);double position=activeDivider.Vertical?n.X:n.Y;
            bool special=resizeGroup.Count>1||Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            bool success=special&&resizeGroup.Count>0?LayoutResizeEngine.TryResizeSelected(resizeOriginal,resizeGroup,activeDivider.Vertical,resizeTrailing,position-resizeStart,Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin,vm.Data.Gap,out var changed):LayoutResizeEngine.TryMove(resizeOriginal,activeDivider,position,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin,vm.Data.Gap,out changed);
            if(success)
            {if(!resizeRecorded){vm.Remember();resizeRecorded=true;}vm.Data.CustomLayouts[vm.Layouts[vm.Data.LayoutIndex].Id]=changed;double actual;if(special&&resizeGroup.Count>0){var s=changed[resizeGroup[0]];actual=activeDivider.Vertical?(resizeTrailing?s.X+s.Width:s.X):(resizeTrailing?s.Y+s.Height:s.Y);}else actual=activeDivider.Before.Count>0?(activeDivider.Vertical?changed[activeDivider.Before[0]].X+changed[activeDivider.Before[0]].Width:changed[activeDivider.Before[0]].Y+changed[activeDivider.Before[0]].Height):(activeDivider.Vertical?changed[activeDivider.After[0]].X:changed[activeDivider.After[0]].Y);activeDivider=new LayoutDivider{Vertical=activeDivider.Vertical,Position=actual,From=activeDivider.From,To=activeDivider.To,Before=activeDivider.Before,After=activeDivider.After};QueuePreview();DrawSelection();}
            return;
        }
        if(!dragging){UpdateCanvasCursor(p);return;}
        if(dragSlot<0||vm.SelectedPhoto==null||dragOriginalSlots==null)return;
        if(!panning&&(p-startPoint).Length>3){vm.Remember();panning=true;}
        if(!panning)return;
        if(cropDragging)
        {
            var r=UiRect(dragSlot);var t=vm.SelectedPhoto.Transform;double scale=t.Scale;var photo=vm.SelectedPhoto;
            double cover=Math.Max(r.Width/photo.Width,r.Height/photo.Height);double overX=Math.Max(0,photo.Width*cover*scale-r.Width),overY=Math.Max(0,photo.Height*cover*scale-r.Height);
            if(overX>0)t.PanX=Math.Clamp(t.PanX+2*(p.X-lastPoint.X)/overX,-1,1);
            if(overY>0)t.PanY=Math.Clamp(t.PanY+2*(p.Y-lastPoint.Y)/overY,-1,1);
            QueuePreview();lastPoint=p;return;
        }
        int target=Hit(p,dragOriginalSlots);
        if(dragGroup.Count==1&&dragOriginalAssignments!=null&&target>=0&&target!=dragSlot)
        {
            if(dragShiftPreviewTarget!=target)
            {RestoreOriginalDragLayout();vm.Data.Assignments=PhotoShiftEngine.InsertAt(dragOriginalAssignments,dragSlot,target);vm.Data.PreviewFrontSlot=-1;vm.Data.PreviewFrontSlots.Clear();dragShiftPreviewTarget=target;DrawSelection();QueuePreview();}
            lastPoint=p;return;
        }
        if(dragShiftPreviewTarget>=0&&dragOriginalAssignments!=null){vm.Data.Assignments=dragOriginalAssignments.ToList();dragShiftPreviewTarget=-1;QueuePreview();}
        Point drop=Normalized(p),start=Normalized(startPoint);List<LayoutSlot> placed=[];
        bool inBlank=InsideCanvas(p)&&Hit(p,dragOriginalSlots)<0;
        bool fitted=vm.Data.LayoutCollection!="RandomSpace"&&dragGroup.Count==1&&inBlank&&FramePlacementEngine.TryFitInOpenArea(dragOriginalSlots,dragSlot,drop.X,drop.Y,out placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin,vm.Data.Gap);
        bool previewed=fitted||(dragGroup.Count==1?FramePlacementEngine.PreviewTranslate(dragOriginalSlots,dragSlot,drop.X-start.X,drop.Y-start.Y,out placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin):FramePlacementEngine.PreviewTranslateMany(dragOriginalSlots,dragGroup,drop.X-start.X,drop.Y-start.Y,out placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin));
        if(!previewed)return;
        vm.Data.CustomLayouts[vm.Layouts[vm.Data.LayoutIndex].Id]=placed;vm.Data.PreviewFrontSlot=dragSlot;vm.Data.PreviewFrontSlots=dragGroup.ToList();
        DrawSelection();QueuePreview();lastPoint=p;
    }
    void UpdateCanvasCursor(Point point){var candidate=DividerAt(point);bool changed=(candidate?.Vertical!=hoveredDivider?.Vertical)||(candidate?.Position!=hoveredDivider?.Position)||(candidate?.From!=hoveredDivider?.From);hoveredDivider=candidate;HitCanvas.Cursor=candidate!=null?(candidate.Vertical?Cursors.SizeWE:Cursors.SizeNS):Hit(point)>=0?(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)?Cursors.Hand:Cursors.SizeAll):Cursors.Arrow;if(changed)DrawSelection();}
    void Canvas_Up(object sender,MouseButtonEventArgs e)
    {
        Point point=e.GetPosition(HitCanvas);
        if(resizing){resizing=false;activeDivider=null;resizeOriginal=null;resizeGroup=[];HitCanvas.ReleaseMouseCapture();UpdateCanvasCursor(point);UpdateEqualGapsButton();DrawSelection();return;}
        if(!dragging)return;
        int dest=Hit(point,dragOriginalSlots);dragging=false;HitCanvas.ReleaseMouseCapture();
        if(cropDragging)
        {
            // Ctrl-drag edits only this photo's crop, even if the pointer leaves its frame.
        }
        else if(panning&&dragGroup.Count==1&&dest>=0&&dest!=dragSlot&&dragOriginalAssignments!=null)
        {
            RestoreOriginalDragLayout();
            vm.Data.Assignments=PhotoShiftEngine.InsertAt(dragOriginalAssignments,dragSlot,dest);
            vm.SelectedSlot=dest;RefreshUi();QueuePreview();Status.Text="Photo inserted. Other photos shifted one place.";
        }
        else if(panning&&dragOriginalSlots!=null)
        {
            if(dragOriginalAssignments!=null)vm.Data.Assignments=dragOriginalAssignments.ToList();
            Point drop=Normalized(point),start=Normalized(startPoint);List<LayoutSlot> placed=[];
            bool fit=vm.Data.LayoutCollection!="RandomSpace"&&dragGroup.Count==1&&dest<0&&InsideCanvas(point)&&FramePlacementEngine.TryFitInOpenArea(dragOriginalSlots,dragSlot,drop.X,drop.Y,out placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin,vm.Data.Gap);
            bool translated=!fit&&InsideCanvas(point)&&(vm.Data.LayoutCollection=="RandomSpace"?FramePlacementEngine.TryTranslateFree(dragOriginalSlots,dragGroup,drop.X-start.X,drop.Y-start.Y,out placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin):dragGroup.Count==1?FramePlacementEngine.TryTranslate(dragOriginalSlots,dragSlot,drop.X-start.X,drop.Y-start.Y,out placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin):FramePlacementEngine.TryTranslateMany(dragOriginalSlots,dragGroup,drop.X-start.X,drop.Y-start.Y,out placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin));
            if(fit||translated){vm.Data.CustomLayouts[vm.Layouts[vm.Data.LayoutIndex].Id]=placed;if(vm.Data.LayoutCollection=="RandomSpace")LayerStackEngine.BringToFront(vm.Data,placed.Count,dragGroup);Status.Text=dragGroup.Count>1?"Selected photo frames moved.":"Photo frame moved.";}
            else{RestoreOriginalDragLayout();Status.Text=vm.Data.LayoutCollection=="RandomSpace"?"Drop inside the collage to move the frame.":"The frame cannot be placed there without overlapping another photo.";}
            DrawSelection();QueuePreview();
        }
        vm.Data.PreviewFrontSlot=-1;vm.Data.PreviewFrontSlots.Clear();dragSlot=-1;dragGroup=[];dragOriginalSlots=null;dragOriginalAssignments=null;dragShiftPreviewTarget=-1;dragLayoutId=null;cropDragging=false;QueuePreview();UpdateCanvasCursor(point);UpdateEqualGapsButton();
    }
    void RestoreOriginalDragLayout(){if(dragLayoutId==null||dragOriginalSlots==null)return;if(dragHadCustom)vm.Data.CustomLayouts[dragLayoutId]=LayoutResizeEngine.Copy(dragOriginalSlots);else vm.Data.CustomLayouts.Remove(dragLayoutId);}
    void Canvas_Wheel(object sender,MouseWheelEventArgs e){if(vm.SelectedPhoto==null)return;vm.Remember();vm.SelectedPhoto.Transform.Scale=Math.Clamp(vm.SelectedPhoto.Transform.Scale*(e.Delta>0?1.1:1/1.1),1,5);RefreshUi();QueuePreview();e.Handled=true;}
    void Canvas_DragOver(object sender,DragEventArgs e)
    {
        e.Effects=DragDropEffects.None;
        if(e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=DragDropEffects.Copy;e.Handled=true;return;}
        if(e.Data.GetDataPresent("CollagePhotoIndex"))
        {
            if(!thumbnailDragActive){thumbnailDragActive=true;thumbnailLayoutId=vm.Layouts[vm.Data.LayoutIndex].Id;thumbnailHadCustom=vm.Data.CustomLayouts.ContainsKey(thumbnailLayoutId);thumbnailOriginalSlots=LayoutResizeEngine.Copy(vm.CurrentSlots);}
            int source=(int)e.Data.GetData("CollagePhotoIndex");int from=vm.Data.Assignments.IndexOf(source);Point point=e.GetPosition(HitCanvas);int dest=Hit(point,thumbnailOriginalSlots);
            if(from>=0&&dest>=0&&dest!=from){RestoreThumbnailPreview(false);e.Effects=DragDropEffects.Move;}
            else if(from>=0&&dest<0&&thumbnailOriginalSlots!=null&&InsideCanvas(point)&&(vm.Data.LayoutCollection=="RandomSpace"?FramePlacementEngine.TryCenterFree(thumbnailOriginalSlots,from,Normalized(point).X,Normalized(point).Y,out var placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin):FramePlacementEngine.TryFitInOpenArea(thumbnailOriginalSlots,from,Normalized(point).X,Normalized(point).Y,out placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin,vm.Data.Gap)))
            {var current=vm.CurrentSlots[from];if(!thumbnailPreviewChanged||Math.Abs(current.X-placed[from].X)>.00001||Math.Abs(current.Y-placed[from].Y)>.00001){vm.Data.CustomLayouts[thumbnailLayoutId!]=placed;thumbnailPreviewChanged=true;DrawSelection();QueuePreview();}e.Effects=DragDropEffects.Move;}
            else RestoreThumbnailPreview(false);
        }
        e.Handled=true;
    }
    void RestoreThumbnailPreview(bool finish)
    {
        if(!thumbnailDragActive||thumbnailLayoutId==null||thumbnailOriginalSlots==null)return;
        if(thumbnailPreviewChanged){if(thumbnailHadCustom)vm.Data.CustomLayouts[thumbnailLayoutId]=LayoutResizeEngine.Copy(thumbnailOriginalSlots);else vm.Data.CustomLayouts.Remove(thumbnailLayoutId);DrawSelection();QueuePreview();thumbnailPreviewChanged=false;}
        if(finish){thumbnailDragActive=false;thumbnailLayoutId=null;thumbnailOriginalSlots=null;}
    }
    void Canvas_DragLeave(object sender,DragEventArgs e)=>RestoreThumbnailPreview(true);
    void Canvas_Drop(object sender,DragEventArgs e)
    {
        if(!e.Data.GetDataPresent("CollagePhotoIndex"))return;
        int source=(int)e.Data.GetData("CollagePhotoIndex");int from=vm.Data.Assignments.IndexOf(source);if(from<0)return;
        Point point=e.GetPosition(HitCanvas);var original=thumbnailOriginalSlots??LayoutResizeEngine.Copy(vm.CurrentSlots);int dest=Hit(point,original);
        if(dest>=0&&from!=dest){RestoreThumbnailPreview(true);vm.Remember();vm.Data.Assignments=PhotoShiftEngine.InsertAt(vm.Data.Assignments,from,dest);vm.SelectedSlot=dest;RefreshUi();QueuePreview();Status.Text="Photo inserted. Other photos shifted one place.";e.Handled=true;return;}
        if(dest<0&&InsideCanvas(point)&&(vm.Data.LayoutCollection=="RandomSpace"?FramePlacementEngine.TryCenterFree(original,from,Normalized(point).X,Normalized(point).Y,out var placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin):FramePlacementEngine.TryFitInOpenArea(original,from,Normalized(point).X,Normalized(point).Y,out placed,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin,vm.Data.Gap)))
        {RestoreThumbnailPreview(true);vm.Remember();vm.Data.CustomLayouts[vm.Layouts[vm.Data.LayoutIndex].Id]=placed;if(vm.Data.LayoutCollection=="RandomSpace")LayerStackEngine.BringToFront(vm.Data,placed.Count,new[]{from});vm.SelectedSlot=from;RefreshUi();QueuePreview();Status.Text="Photo placed in the background area.";e.Handled=true;}
        else if(dest<0)Status.Text=vm.Data.LayoutCollection=="RandomSpace"?"Drop inside the collage to move this frame.":"This photo frame does not fit in that open area. Resize it or choose a larger empty area.";
        else RestoreThumbnailPreview(true);
    }
    void PhotoList_PreviewMouseMove(object sender,MouseEventArgs e){if(e.LeftButton!=MouseButtonState.Pressed||PhotoList.SelectedItem is not ListBoxItem item)return;DragDrop.DoDragDrop(PhotoList,new DataObject("CollagePhotoIndex",(int)item.Tag),DragDropEffects.Move);RestoreThumbnailPreview(true);}
    void PhotoList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(updating)return;
        selectedSlots.Clear();
        foreach(ListBoxItem item in PhotoList.SelectedItems)
        {
            int slot=vm.Data.Assignments.IndexOf((int)item.Tag);
            if(slot>=0)selectedSlots.Add(slot);
        }
        int last=e.AddedItems.OfType<ListBoxItem>().Select(item=>vm.Data.Assignments.IndexOf((int)item.Tag)).LastOrDefault(-1);
        vm.SelectedSlot=selectedSlots.Contains(last)?last:selectedSlots.FirstOrDefault(-1);
        SelectedText.Text=vm.SelectedPhoto is { } photo?Path.GetFileName(photo.Path):"Click a photo in the collage";
        CenterPhotoButton.IsEnabled=vm.SelectedPhoto!=null;
        updating=true;ZoomSlider.Value=vm.SelectedPhoto?.Transform.Scale??1;updating=false;
        DrawSelection();
    }
    void SetZoom(double value){if(vm.SelectedPhoto==null)return;vm.Remember();vm.SelectedPhoto.Transform.Scale=Math.Clamp(value,1,5);RefreshUi();QueuePreview();}
    void ZoomIn_Click(object sender,RoutedEventArgs e)=>SetZoom((vm.SelectedPhoto?.Transform.Scale??1)*1.02);void ZoomOut_Click(object sender,RoutedEventArgs e)=>SetZoom((vm.SelectedPhoto?.Transform.Scale??1)/1.02);
    void Zoom_Changed(object sender,RoutedPropertyChangedEventArgs<double> e){if(updating||vm.SelectedPhoto==null)return;SetZoom(e.NewValue);}
    void Reset_Click(object sender,RoutedEventArgs e){if(vm.SelectedPhoto==null)return;vm.Remember();vm.SelectedPhoto.Transform=new();RefreshUi();QueuePreview();}
    void CenterPhoto_Click(object sender,RoutedEventArgs e)
    {
        if(vm.SelectedPhoto==null||vm.SelectedSlot<0||vm.SelectedSlot>=vm.CurrentSlots.Count)return;
        var slot=vm.CurrentSlots[vm.SelectedSlot];
        if(Math.Abs(slot.X+slot.Width/2-.5)<.000001&&Math.Abs(slot.Y+slot.Height/2-.5)<.000001)
        {Status.Text="This photo frame is already centered in the collage.";return;}
        bool centeredOk=vm.Data.LayoutCollection=="RandomSpace"?FramePlacementEngine.TryCenterFree(vm.CurrentSlots,vm.SelectedSlot,.5,.5,out var centered,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin):FramePlacementEngine.TryCenterAt(vm.CurrentSlots,vm.SelectedSlot,.5,.5,out centered,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin);
        if(!centeredOk)
        {Status.Text="The center is occupied by another photo frame. Move that frame first.";return;}
        vm.Remember();vm.Data.CustomLayouts[vm.Layouts[vm.Data.LayoutIndex].Id]=centered;if(vm.Data.LayoutCollection=="RandomSpace")LayerStackEngine.BringToFront(vm.Data,centered.Count,new[]{vm.SelectedSlot});
        RefreshUi();QueuePreview();Status.Text="Selected photo frame centered in the collage. Undo is available.";
    }
    async void Replace_Click(object sender,RoutedEventArgs e){if(vm.SelectedPhoto==null)return;var path=PickJpg();if(path==null)return;try{Busy("Loading replacement…",true);var result=await Task.Run(()=>{var size=Rendering.ReadSize(path);var preview=Rendering.MakePreview(path);return(size,preview);});vm.Remember();var p=vm.SelectedPhoto;p.Path=path;p.Width=result.size.Width;p.Height=result.size.Height;p.Preview=result.preview;p.Transform=new();Busy("Photo replaced.",false);RefreshUi();QueuePreview();}catch(Exception ex){Busy("Ready",false);Error(ex);}}
    void Remove_Click(object sender,RoutedEventArgs e){if(vm.SelectedPhoto==null)return;vm.Remember();int index=vm.Data.Assignments[vm.SelectedSlot];vm.Data.Photos.RemoveAt(index);vm.Data.Assignments.RemoveAt(vm.SelectedSlot);for(int i=0;i<vm.Data.Assignments.Count;i++)if(vm.Data.Assignments[i]>index)vm.Data.Assignments[i]--;vm.Data.LayerOrder.Clear();vm.SelectedSlot=-1;vm.Data.LayoutIndex=0;RefreshUi();QueuePreview();}
    void RemoveAll_Click(object sender,RoutedEventArgs e){if(vm.Data.Photos.Count==0)return;vm.ClearPhotos();hoveredDivider=null;activeDivider=null;HitCanvas.Cursor=Cursors.Arrow;RefreshUi();QueuePreview();Status.Text="All photos removed from this collage. Source JPG files were not changed. Use Undo to restore.";}
    void MoveUsedPhotos_Changed(object sender,RoutedEventArgs e){if(updating||MoveUsedPhotosCheck==null)return;bool enabled=MoveUsedPhotosCheck.IsChecked==true;if(enabled==vm.Data.MoveUsedPhotosAfterExport)return;vm.Remember();vm.Data.MoveUsedPhotosAfterExport=enabled;Status.Text=enabled?"After a successful export, originals will move to a used photos folder beside each source.":"Automatic source-photo moving is disabled.";}
    void Gap_Changed(object sender,RoutedPropertyChangedEventArgs<double> e){if(updating||GapBox==null)return;int v=(int)e.NewValue;if(v==vm.Data.Gap)return;if(!Fits(v,vm.Data.Margin,vm.Data.LayoutIndex,vm.Data.PixelWidth,vm.Data.PixelHeight)){updating=true;GapSlider.Value=vm.Data.Gap;updating=false;Status.Text="Photo gap is too large for the current canvas and layout.";return;}vm.Remember();vm.Data.Gap=v;GapBox.Text=v.ToString();QueuePreview();}
    void Margin_Changed(object sender,RoutedPropertyChangedEventArgs<double> e){if(updating||MarginBox==null)return;int v=(int)e.NewValue;if(v==vm.Data.Margin)return;if(!Fits(vm.Data.Gap,v,vm.Data.LayoutIndex,vm.Data.PixelWidth,vm.Data.PixelHeight)){updating=true;MarginSlider.Value=vm.Data.Margin;updating=false;Status.Text="Outer margin is too large for the current canvas and layout.";return;}vm.Remember();vm.Data.Margin=v;MarginBox.Text=v.ToString();QueuePreview();}
    void GapBox_Changed(object sender,RoutedEventArgs e){if(int.TryParse(GapBox.Text,out int value)&&value is >=0 and <=500)GapSlider.Value=value;else GapBox.Text=vm.Data.Gap.ToString();}
    void MarginBox_Changed(object sender,RoutedEventArgs e){if(int.TryParse(MarginBox.Text,out int value)&&value is >=0 and <=1000)MarginSlider.Value=value;else MarginBox.Text=vm.Data.Margin.ToString();}
    void Quality_Changed(object sender,RoutedPropertyChangedEventArgs<double> e){if(updating||QualityValue==null)return;vm.Data.JpegQuality=(int)e.NewValue;QualityValue.Text=vm.Data.JpegQuality.ToString();}
    void Background_Changed(object sender,SelectionChangedEventArgs e){if(updating||BackgroundBox==null)return;string color=BackgroundBox.SelectedIndex switch{0=>"#FFFFFFFF",1=>"#FFE8E8E8",2=>"#FF000000",_=>vm.Data.Background};if(BackgroundBox.SelectedIndex==3){var input=new TextBox{Text=vm.Data.Background,Margin=new Thickness(12)};var dialog=new Window{Title="Custom background color",Owner=this,Width=340,Height=150,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new StackPanel{Children={new TextBlock{Text="Enter #RRGGBB or #AARRGGBB",Margin=new Thickness(12,12,12,0)},input}}};input.KeyDown+=(s,k)=>{if(k.Key==Key.Enter)dialog.DialogResult=true;};if(dialog.ShowDialog()==true){try{var parsed=(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(input.Text);color=$"#{parsed.A:X2}{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";}catch{Status.Text="Invalid color. Use #RRGGBB.";}}}if(color!=vm.Data.Background){vm.Remember();vm.Data.Background=color;QueuePreview();}}
    static int PresetIndex(ProjectData data)
    {
        if(data.Unit!="Inches")return 0;
        for(int i=0;i<CanvasPresets.Length;i++)
        {
            var (w,h)=CanvasPresets[i];
            if((Math.Abs(data.Width-w)<.000001&&Math.Abs(data.Height-h)<.000001)||(Math.Abs(data.Width-h)<.000001&&Math.Abs(data.Height-w)<.000001))return i+1;
        }
        return 0;
    }
    void CanvasPreset_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(updating||CanvasPresetBox==null||CanvasPresetBox.SelectedIndex<1)return;
        var (width,height)=CanvasPresets[CanvasPresetBox.SelectedIndex-1];int pw=(int)Math.Round(width*vm.Data.Dpi),ph=(int)Math.Round(height*vm.Data.Dpi);
        if(pw>30000||ph>30000||(long)pw*ph>220_000_000||!Fits(vm.Data.Gap,vm.Data.Margin,vm.Data.LayoutIndex,pw,ph))
        {Status.Text="This preset does not fit the current DPI, gap, and margin. Reduce them first.";updating=true;CanvasPresetBox.SelectedIndex=PresetIndex(vm.Data);updating=false;return;}
        if(vm.Data.Unit=="Inches"&&vm.Data.Width==width&&vm.Data.Height==height)return;
        vm.Remember();vm.Data.Unit="Inches";vm.Data.Width=width;vm.Data.Height=height;RefreshUi();QueuePreview();PersistCanvasSettings();
    }
    void SizeBox_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){Size_Changed(sender,e);e.Handled=true;}}
    void Size_Changed(object sender,RoutedEventArgs e){if(updating||WidthBox==null||HeightBox==null||DpiBox==null)return;var culture=System.Globalization.CultureInfo.InvariantCulture;if(!double.TryParse(WidthBox.Text,culture,out var w)||!double.TryParse(HeightBox.Text,culture,out var h)||!int.TryParse(DpiBox.Text,out var dpi)||w<=0||h<=0||dpi<1||dpi>1200){Status.Text="Enter positive width/height and DPI 1–1200.";return;}string unit=UnitBox.SelectedIndex==1?"Inches":"Pixels";double rawWidth=Math.Round(w*(unit=="Inches"?dpi:1)),rawHeight=Math.Round(h*(unit=="Inches"?dpi:1));if(rawWidth<1||rawHeight<1||rawWidth>30000||rawHeight>30000||rawWidth*rawHeight>220_000_000){Status.Text="Canvas must be at most 30,000 px per side and 220 megapixels.";return;}int pw=(int)rawWidth,ph=(int)rawHeight;if(!Fits(vm.Data.Gap,vm.Data.Margin,vm.Data.LayoutIndex,pw,ph)){Status.Text="Current gap and margin will not fit this canvas size. Reduce them first.";return;}if(w!=vm.Data.Width||h!=vm.Data.Height||dpi!=vm.Data.Dpi||unit!=vm.Data.Unit){vm.Remember();vm.Data.Width=w;vm.Data.Height=h;vm.Data.Dpi=dpi;vm.Data.Unit=unit;RefreshUi();FitPreview();QueuePreview();PersistCanvasSettings();Status.Text=$"Canvas size applied: {pw:N0} × {ph:N0} px.";}}
    void Orient(bool portrait){if(vm.Data.Width==vm.Data.Height)return;bool isPortrait=vm.Data.Height>vm.Data.Width;if(isPortrait==portrait)return;vm.Remember();(vm.Data.Width,vm.Data.Height)=(vm.Data.Height,vm.Data.Width);RefreshUi();QueuePreview();PersistCanvasSettings();}
    void Portrait_Click(object sender,RoutedEventArgs e)=>Orient(true);void Landscape_Click(object sender,RoutedEventArgs e)=>Orient(false);
    void Save_Click(object sender,RoutedEventArgs e){if(vm.ProjectPath==null){SaveAs_Click(sender,e);return;}try{vm.Save(vm.ProjectPath);Status.Text="Project saved.";}catch(Exception ex){Error(ex);}}
    void SaveAs_Click(object sender,RoutedEventArgs e){var d=new SaveFileDialog{Filter="Collage project|*.collageproject",DefaultExt=".collageproject"};if(d.ShowDialog()==true)try{vm.Save(d.FileName);Status.Text="Project saved.";}catch(Exception ex){Error(ex);}}
    async void Open_Click(object sender,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Collage project|*.collageproject"};if(d.ShowDialog()!=true)return;try{var data=EditorViewModel.Load(d.FileName);var missing=data.Photos.Where(p=>!File.Exists(p.Path)).Select(p=>p.Path).ToList();if(missing.Count>0)MessageBox.Show(this,$"{missing.Count} source photo(s) are missing. Their frames will remain blank until files are restored.\n\n{string.Join("\n",missing.Take(5))}","Missing source files");Busy("Loading project previews…",true);foreach(var p in data.Photos.Where(p=>File.Exists(p.Path))){try{var result=await Task.Run(()=>{var size=Rendering.ReadSize(p.Path);var preview=Rendering.MakePreview(p.Path);return(size,preview);});p.Width=result.size.Width;p.Height=result.size.Height;p.Preview=result.preview;}catch{}}vm.SetData(data);vm.ProjectPath=d.FileName;Busy("Project opened.",false);RefreshUi();QueuePreview();PersistCanvasSettings();}catch(Exception ex){Busy("Ready",false);Error(ex);}}
    void QuickSave_Changed(object sender,RoutedEventArgs e)
    {
        if(updating||QuickSaveCheck==null)return;
        PersistExportSettings();
        Status.Text=QuickSaveCheck.IsChecked==true?(lastExportPath==null?"Quick save is on. Choose a folder and name for the first export.":"Quick save is on. The next export will use the previous folder and a new numbered name."):"Quick save is off. The export dialog will open.";
    }
    string? ChooseExportPath(IEnumerable<string> sourcePaths)
    {
        string? previousFolder=null;
        try{if(lastExportPath!=null){var folder=Path.GetDirectoryName(Path.GetFullPath(lastExportPath));if(folder!=null&&Directory.Exists(folder))previousFolder=folder;}}catch{}
        bool hasPrevious=previousFolder!=null&&lastExportPath!=null&&new[]{".jpg",".jpeg",".png",".psd"}.Contains(Path.GetExtension(lastExportPath).ToLowerInvariant());
        if(QuickSaveCheck.IsChecked==true&&hasPrevious)return ExportNamePlanner.NextAvailable(lastExportPath!,sourcePaths);
        string suggested=hasPrevious?ExportNamePlanner.NextAvailable(lastExportPath!,sourcePaths):"collage.jpg";
        string extension=Path.GetExtension(suggested).ToLowerInvariant();
        var dialog=new SaveFileDialog{Filter="JPEG image|*.jpg|PNG image|*.png|Photoshop layered PSD|*.psd",FilterIndex=extension==".png"?2:extension==".psd"?3:1,DefaultExt=extension,FileName=Path.GetFileName(suggested),OverwritePrompt=false};
        if(previousFolder!=null)dialog.InitialDirectory=previousFolder;
        if(dialog.ShowDialog()!=true)return null;
        string expected=dialog.FilterIndex switch{2=>".png",3=>".psd",_=>".jpg"};
        string selected=Path.GetFullPath(dialog.FileName);
        if(!Path.GetExtension(selected).Equals(expected,StringComparison.OrdinalIgnoreCase)&&!(expected==".jpg"&&Path.GetExtension(selected).Equals(".jpeg",StringComparison.OrdinalIgnoreCase)))selected=Path.ChangeExtension(selected,expected);
        return selected;
    }
    async void Export_Click(object sender,RoutedEventArgs e)
    {
        if(exporting)return;
        if(vm.Data.Photos.Count==0){Status.Text="Import at least one photo before exporting.";return;}
        var missing=vm.Data.Photos.Where(p=>!File.Exists(p.Path)).Select(p=>p.Path).ToList();if(missing.Count>0){MessageBox.Show(this,$"Export stopped: {missing.Count} source photo(s) are missing.","Missing source files");return;}
        string? requestedPath;
        try{requestedPath=ChooseExportPath(vm.Data.Photos.Select(p=>p.Path));}catch(Exception ex){Error(ex);return;}
        if(requestedPath==null)return;
        exporting=true;IsEnabled=false;
        try
        {
            bool isPsd=Path.GetExtension(requestedPath).Equals(".psd",StringComparison.OrdinalIgnoreCase);
            Busy(isPsd?"Exporting separate Photoshop layers from original JPGs…":"Exporting from original JPGs…",true);
            var snapshot=System.Text.Json.JsonSerializer.Deserialize<ProjectData>(System.Text.Json.JsonSerializer.Serialize(vm.Data))!;
            bool moveAfterExport=snapshot.MoveUsedPhotosAfterExport;
            var sourceSnapshots=moveAfterExport?UsedPhotoMover.Capture(snapshot.Photos.Select(p=>p.Path)):[];
            var progress=new Progress<int>(v=>Progress.Value=v);
            string exportPath=await ExportFileWriter.ExportAsync(snapshot,LayoutEngine.ForCount(snapshot.Photos.Count,snapshot.LayoutCollection),requestedPath,progress);
            lastExportPath=exportPath;PersistExportSettings();
            if(!moveAfterExport){Busy($"Exported {snapshot.PixelWidth:N0} × {snapshot.PixelHeight:N0} to {exportPath}",false);return;}

            Busy("Export complete. Moving original photos to used photos folders…",true);
            var results=await Task.Run(()=>UsedPhotoMover.Move(sourceSnapshots));
            var moved=results.Where(r=>r.Succeeded).Select(r=>r.SourcePath).ToList();
            vm.RemoveMovedPhotos(moved);RefreshUi();QueuePreview();
            var failures=results.Where(r=>!r.Succeeded).ToList();
            Busy($"Exported to {exportPath}. Moved {moved.Count} source photo(s); {failures.Count} could not be moved.",false);
            if(failures.Count>0){IsEnabled=true;MessageBox.Show(this,$"The collage was exported. These source photos stayed in their original folders and remain in the app:\n\n{string.Join("\n",failures.Take(5).Select(r=>$"{Path.GetFileName(r.SourcePath)}: {r.Error}"))}","Some photos were not moved",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        catch(Exception ex){Busy("Export failed. Original photos were not moved.",false);IsEnabled=true;Error(ex);}
        finally{IsEnabled=true;exporting=false;}
    }
    void RestoreCaches(Dictionary<string,(int Width,int Height,byte[]? Preview)> cache){foreach(var photo in vm.Data.Photos){if(cache.TryGetValue(photo.Path,out var c)){photo.Width=c.Width;photo.Height=c.Height;photo.Preview=c.Preview;}}}
    Dictionary<string,(int Width,int Height,byte[]? Preview)> CacheMap()=>vm.Data.Photos.GroupBy(p=>p.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>(g.First().Width,g.First().Height,g.First().Preview),StringComparer.OrdinalIgnoreCase);
    async Task RefreshHistory(bool undo)
    {
        var cache=CacheMap();if(!(undo?vm.Undo():vm.Redo()))return;RestoreCaches(cache);
        foreach(var photo in vm.Data.Photos.Where(p=>p.Preview==null&&File.Exists(p.Path))){try{var result=await Task.Run(()=>{var size=Rendering.ReadSize(photo.Path);var preview=Rendering.MakePreview(photo.Path);return(size,preview);});photo.Width=result.size.Width;photo.Height=result.size.Height;photo.Preview=result.preview;}catch{}}
        RefreshUi();QueuePreview();
    }
    async void Undo_Click(object sender,RoutedEventArgs e)=>await RefreshHistory(true);async void Redo_Click(object sender,RoutedEventArgs e)=>await RefreshHistory(false);
    void NudgeSelected(Key direction)
    {
        if(dragging||resizing||vm.Layouts.Count==0)return;
        var slots=selectedSlots.Where(i=>i>=0&&i<vm.CurrentSlots.Count).Order().ToList();
        if(slots.Count==0&&vm.SelectedSlot>=0&&vm.SelectedSlot<vm.CurrentSlots.Count)slots.Add(vm.SelectedSlot);
        if(slots.Count==0)return;
        int x=direction==Key.Left?-2:direction==Key.Right?2:0;
        int y=direction==Key.Up?-2:direction==Key.Down?2:0;
        if(!FramePlacementEngine.TryNudge(vm.CurrentSlots,slots,x,y,vm.Data.LayoutCollection=="RandomSpace",out var moved,vm.Data.PixelWidth,vm.Data.PixelHeight,vm.Data.Margin))
        {Status.Text="Selected photo frame cannot move farther in that direction.";return;}
        vm.Remember();vm.Data.CustomLayouts[vm.Layouts[vm.Data.LayoutIndex].Id]=moved;
        DrawSelection();QueuePreview();UpdateEqualGapsButton();Status.Text=$"Moved {slots.Count} selected photo frame(s) by 2 px.";
    }
    void Window_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key is Key.LeftCtrl or Key.RightCtrl){if(!dragging)UpdateCanvasCursor(Mouse.GetPosition(HitCanvas));return;}
        if(Keyboard.FocusedElement is TextBox or ComboBox or Slider)return;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){if(e.Key==Key.A){PhotoList.SelectAll();Status.Text=$"Selected all {PhotoList.SelectedItems.Count} photos.";}else if(e.Key==Key.Down)ChangeLayout(1);else if(e.Key==Key.Up)ChangeLayout(-1);else if(e.Key==Key.O)Import_Click(sender,new());else if(e.Key==Key.S&&Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))SaveAs_Click(sender,new());else if(e.Key==Key.S)Save_Click(sender,new());else if(e.Key==Key.E)Export_Click(sender,new());else if(e.Key==Key.Z)Undo_Click(sender,new());else if(e.Key==Key.Y)Redo_Click(sender,new());else return;e.Handled=true;return;}
        if(Keyboard.Modifiers!=ModifierKeys.None)return;
        if(e.Key is Key.Left or Key.Right or Key.Up or Key.Down)NudgeSelected(e.Key);else if(e.Key==Key.Delete)Remove_Click(sender,new());else if(e.Key is Key.Add or Key.OemPlus)ZoomIn_Click(sender,new());else if(e.Key is Key.Subtract or Key.OemMinus)ZoomOut_Click(sender,new());else if(e.Key is Key.D0 or Key.NumPad0)Reset_Click(sender,new());else return;e.Handled=true;
    }
    void Window_KeyUp(object sender,KeyEventArgs e){if(e.Key is Key.LeftCtrl or Key.RightCtrl&&!dragging)UpdateCanvasCursor(Mouse.GetPosition(HitCanvas));}
    static Brush ColorBrush(string hex)=>new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
    void ApplyTheme(DependencyObject node)
    {
        Brush shell=ColorBrush(light?"#F3F5F7":"#171B20"),panel=ColorBrush(light?"#E5EAF0":"#212830"),bar=ColorBrush(light?"#DCE3EA":"#20262D"),field=ColorBrush(light?"#FFFFFF":"#292F37"),button=ColorBrush(light?"#CBD5DF":"#303943"),ink=ColorBrush(light?"#1B2630":"#ECEFF2");
        if(node is Window window){window.Background=shell;window.Foreground=ink;}
        if(node is Border border && border.Background is SolidColorBrush bb && bb.Color!=System.Windows.Media.Colors.Transparent)border.Background=panel;
        if(node is Grid grid && grid.Background is SolidColorBrush gb){string key=gb.Color.ToString();if(key is "#FF20262D" or "#FFDCE3EA")grid.Background=bar;else if(key is "#FF11151A" or "#FFF3F5F7")grid.Background=shell;}
        if(node is ScrollViewer scroll)scroll.Background=panel;
        if(node is TextBlock text)text.Foreground=ink;
        if(node is TextBox box){box.Background=field;box.Foreground=ink;}
        if(node is Button btn){if(btn.Background is SolidColorBrush cb && cb.Color.ToString()!="#FFAF754F"){btn.Background=button;btn.Foreground=ink;}}
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)ApplyTheme(VisualTreeHelper.GetChild(node,i));
    }
    void Theme_Click(object sender,RoutedEventArgs e){light=!light;ApplyTheme(this);Status.Text=light?"Light theme enabled.":"Dark theme enabled.";}
    void Credit_Navigate(object sender,System.Windows.Navigation.RequestNavigateEventArgs e){Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;}
}
