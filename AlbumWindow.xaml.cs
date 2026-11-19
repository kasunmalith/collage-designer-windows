using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Point=System.Windows.Point;

namespace CollageDesigner;
public partial class AlbumWindow : Window
{
    AlbumProject? album;string? projectPath;readonly List<string> imported=[];
    readonly Stack<string> undo=new(),redo=new();CancellationTokenSource? renderToken;
    bool updating,panning,frameDragging,frameRemembered;int currentSheet,selectedSlot=-1;Point panStart,lastPan,photoDragStart;List<LayoutSlot>? frameOriginalSlots;
    readonly record struct PhotoDrag(int Sheet,int Slot);

    public AlbumWindow(){InitializeComponent();SizeBox.ItemsSource=AlbumEngine.Presets;SizeBox.SelectedIndex=6;UpdateSetup();}
    void Setup_Changed(object sender,RoutedEventArgs e){if(SizeBox!=null&&SetupSummary!=null)UpdateSetup();}
    void UpdateSetup()
    {
        var size=SizeBox.SelectedItem as AlbumSize;
        int.TryParse(SheetCountBox.Text,out int sheets);int.TryParse(DpiBox.Text,out int dpi);
        SetupSummary.Text=size==null?"Choose a size.":$"Album Size: {size.HeightInches} × {size.WidthInches} inches\nRatio: {(double)size.WidthInches/size.HeightInches:0.##}:1\nSheets: {sheets}\nSelected Photos: {imported.Count}\nAverage Density: {(sheets>0?(double)imported.Count/sheets:0):0.0} photos/sheet\nExport: {size.WidthInches*dpi:N0} × {size.HeightInches*dpi:N0} px";
    }
    void Import_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Multiselect=true,Filter="JPG photographs|*.jpg;*.jpeg",CheckFileExists=true};
        if(dialog.ShowDialog()!=true)return;
        imported.Clear();imported.AddRange(NaturalPhotoOrder.Sort(dialog.FileNames.Distinct(StringComparer.OrdinalIgnoreCase)));
        UpdateSetup();StatusText.Text=$"Imported {imported.Count} selected photos in natural filename order.";
    }
    void Generate_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(SizeBox.SelectedItem is not AlbumSize size||!int.TryParse(SheetCountBox.Text,out int sheets)||!int.TryParse(DpiBox.Text,out int dpi))throw new InvalidDataException("Choose an album size, sheet count, and DPI.");
            var catalog=new TrainingCatalog();album=AlbumEngine.Generate(size,sheets,imported,dpi,catalog.Recommend);projectPath=null;currentSheet=0;selectedSlot=-1;undo.Clear();redo.Clear();ShowEditor();
            StatusText.Text=$"Generated {sheets} sheets using all {album.Photos.Count} selected photos.";
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not generate album",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    void ShowEditor(){SetupPanel.Visibility=Visibility.Collapsed;EditorPanel.Visibility=Visibility.Visible;RefreshEditor();}
    void Remember(){if(album==null)return;undo.Push(JsonSerializer.Serialize(album));redo.Clear();}
    void Undo_Click(object sender,RoutedEventArgs e)
    {
        if(album==null||undo.Count==0)return;redo.Push(JsonSerializer.Serialize(album));Restore(undo.Pop());
    }
    void Redo_Click(object sender,RoutedEventArgs e)
    {
        if(album==null||redo.Count==0)return;undo.Push(JsonSerializer.Serialize(album));Restore(redo.Pop());
    }
    void Restore(string json)
    {
        var old=album;album=JsonSerializer.Deserialize<AlbumProject>(json)!;
        if(old!=null)for(int i=0;i<Math.Min(old.Photos.Count,album.Photos.Count);i++){album.Photos[i].Preview=old.Photos[i].Preview;album.Photos[i].Width=old.Photos[i].Width;album.Photos[i].Height=old.Photos[i].Height;}
        currentSheet=Math.Clamp(currentSheet,0,album.Sheets.Count-1);selectedSlot=-1;RefreshEditor();
    }
    void RefreshEditor()
    {
        if(album==null)return;updating=true;
        SheetList.ItemsSource=album.Sheets.Select((s,i)=>$"Sheet {i+1:D2}  ·  {s.PhotoIndices.Count} photos{(s.Approved?"  ✓":"")}").ToList();SheetList.SelectedIndex=currentSheet;
        var sheet=album.Sheets[currentSheet];PhotoList.ItemsSource=sheet.PhotoIndices.Select((p,i)=>$"{i+1:D2}  {Path.GetFileName(album.Photos[p].Path)}").ToList();
        if(selectedSlot>=sheet.PhotoIndices.Count)selectedSlot=-1;PhotoList.SelectedIndex=selectedSlot;
        UnassignedList.ItemsSource=album.Unassigned.Select(i=>Path.GetFileName(album.Photos[i].Path)).ToList();
        var usage=AlbumEngine.Usage(album);UsageText.Text=$"Photos Used: {usage.Used} / {album.Photos.Count}\nUnused: {usage.Unused}\nDuplicates: {usage.Duplicates}\nSheet {currentSheet+1} / {album.Sheets.Count}\nLayout {(sheet.LayoutVariant<0?"Learned style":$"{sheet.LayoutVariant+1} / 3")}";
        FoldCheck.IsChecked=album.ShowFoldGuide;FoldSafeBox.Text=album.FoldSafeZoneInches.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ZoomSlider.Value=SelectedPhoto?.Transform.Scale??1;updating=false;QueuePreview();
    }
    PhotoEntry? SelectedPhoto=>album!=null&&selectedSlot>=0&&selectedSlot<album.Sheets[currentSheet].PhotoIndices.Count?album.Photos[album.Sheets[currentSheet].PhotoIndices[selectedSlot]]:null;
    async Task EnsurePreviewPhotos(int sheetIndex,CancellationToken token)
    {
        if(album==null)return;
        foreach(int index in album.Sheets[sheetIndex].PhotoIndices)
        {
            token.ThrowIfCancellationRequested();var photo=album.Photos[index];
            if(photo.Preview!=null)continue;
            if(!File.Exists(photo.Path))continue;
            var loaded=await Task.Run(()=>{var size=Rendering.ReadSize(photo.Path);var preview=Rendering.MakePreview(photo.Path,900);return(size,preview);},token);
            photo.Width=loaded.size.Width;photo.Height=loaded.size.Height;photo.Preview=loaded.preview;
        }
    }
    void QueuePreview(){renderToken?.Cancel();var cts=new CancellationTokenSource();renderToken=cts;_ =RenderPreview(cts.Token);}
    async Task RenderPreview(CancellationToken token)
    {
        if(album==null)return;int sheetIndex=currentSheet;
        try
        {
            await EnsurePreviewPhotos(sheetIndex,token);
            token.ThrowIfCancellationRequested();var (data,layouts)=AlbumRenderService.Build(album,sheetIndex);
            byte[] bytes=await Task.Run(()=>Rendering.Render(data,layouts,true,1300),token);
            token.ThrowIfCancellationRequested();using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
            await Dispatcher.InvokeAsync(()=>{if(!token.IsCancellationRequested){SpreadImage.Source=bitmap;FitPreview();}});
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!token.IsCancellationRequested)await Dispatcher.InvokeAsync(()=>StatusText.Text=$"Preview: {ex.Message}");}
    }
    void FitPreview()
    {
        if(album==null)return;double availableWidth=Math.Max(1,PreviewHost.ActualWidth-28),availableHeight=Math.Max(1,PreviewHost.ActualHeight-28);
        double scale=Math.Min(availableWidth/album.PixelWidth,availableHeight/album.PixelHeight);
        SpreadImage.Width=HitCanvas.Width=album.PixelWidth*scale;SpreadImage.Height=HitCanvas.Height=album.PixelHeight*scale;
        FoldGuide.Height=FoldSafeGuide.Height=SpreadImage.Height;FoldGuide.Visibility=FoldSafeGuide.Visibility=album.ShowFoldGuide?Visibility.Visible:Visibility.Collapsed;
        FoldSafeGuide.Width=Math.Max(1,2*album.FoldSafeZoneInches*album.Dpi*scale);
    }
    void PreviewHost_SizeChanged(object sender,SizeChangedEventArgs e)=>FitPreview();
    int Hit(Point point)
    {
        if(album==null||HitCanvas.Width<=0)return -1;var sheet=album.Sheets[currentSheet];
        for(int i=sheet.Slots.Count-1;i>=0;i--)
        {
            var r=Rendering.SlotRect(sheet.Slots[i],album.PixelWidth,album.PixelHeight,album.Margin,album.Gap);
            double sx=HitCanvas.Width/album.PixelWidth,sy=HitCanvas.Height/album.PixelHeight;
            if(point.X>=r.Left*sx&&point.X<=r.Right*sx&&point.Y>=r.Top*sy&&point.Y<=r.Bottom*sy)return i;
        }
        return -1;
    }
    void Canvas_Down(object sender,MouseButtonEventArgs e)
    {
        int hit=Hit(e.GetPosition(HitCanvas));if(hit<0)return;selectedSlot=hit;PhotoList.SelectedIndex=hit;panStart=lastPan=e.GetPosition(HitCanvas);panning=true;
        frameDragging=Keyboard.Modifiers.HasFlag(ModifierKeys.Control);frameRemembered=false;frameOriginalSlots=frameDragging&&album!=null?LayoutResizeEngine.Copy(album.Sheets[currentSheet].Slots):null;
        HitCanvas.CaptureMouse();e.Handled=true;
    }
    void Canvas_Move(object sender,MouseEventArgs e)
    {
        if(!panning||album==null||SelectedPhoto is not { } photo)return;
        Point point=e.GetPosition(HitCanvas);if((point-panStart).Length<2)return;
        if(frameDragging&&frameOriginalSlots!=null)
        {
            double dx=(point.X-panStart.X)*album.PixelWidth/HitCanvas.Width/(album.PixelWidth-2.0*album.Margin);
            double dy=(point.Y-panStart.Y)*album.PixelHeight/HitCanvas.Height/(album.PixelHeight-2.0*album.Margin);
            if(FramePlacementEngine.PreviewTranslateMany(frameOriginalSlots,new[]{selectedSlot},dx,dy,out var moved,album.PixelWidth,album.PixelHeight,album.Margin))
            {
                if(!frameRemembered){Remember();frameRemembered=true;}
                album.Sheets[currentSheet].Slots=moved;album.Sheets[currentSheet].Approved=false;QueuePreview();
            }
            return;
        }
        if(photo.Width<=0||photo.Height<=0)return;
        var rect=Rendering.SlotRect(album.Sheets[currentSheet].Slots[selectedSlot],album.PixelWidth,album.PixelHeight,album.Margin,album.Gap);
        double rw=rect.Width*HitCanvas.Width/album.PixelWidth,rh=rect.Height*HitCanvas.Height/album.PixelHeight;
        double cover=Math.Max(rw/photo.Width,rh/photo.Height),overX=Math.Max(0,photo.Width*cover*photo.Transform.Scale-rw),overY=Math.Max(0,photo.Height*cover*photo.Transform.Scale-rh);
        if(lastPan==panStart)Remember();
        if(overX>0)photo.Transform.PanX=Math.Clamp(photo.Transform.PanX+2*(point.X-lastPan.X)/overX,-1,1);
        if(overY>0)photo.Transform.PanY=Math.Clamp(photo.Transform.PanY+2*(point.Y-lastPan.Y)/overY,-1,1);
        lastPan=point;QueuePreview();
    }
    void Canvas_Up(object sender,MouseButtonEventArgs e){panning=false;frameDragging=false;frameOriginalSlots=null;HitCanvas.ReleaseMouseCapture();}
    void Sheet_Changed(object sender,SelectionChangedEventArgs e){if(updating||album==null||SheetList.SelectedIndex<0)return;currentSheet=SheetList.SelectedIndex;selectedSlot=-1;RefreshEditor();}
    void Photo_Changed(object sender,SelectionChangedEventArgs e){if(updating||album==null)return;selectedSlot=PhotoList.SelectedIndex;updating=true;ZoomSlider.Value=SelectedPhoto?.Transform.Scale??1;updating=false;}
    void Zoom_Changed(object sender,RoutedPropertyChangedEventArgs<double> e){if(updating||album==null||SelectedPhoto is not { } photo)return;Remember();photo.Transform.Scale=e.NewValue;QueuePreview();}
    void ResetCrop_Click(object sender,RoutedEventArgs e){if(SelectedPhoto is not { } photo)return;Remember();photo.Transform=new();RefreshEditor();}
    void FrameSize_Click(object sender,RoutedEventArgs e)
    {
        if(album==null||selectedSlot<0||sender is not Button button||button.Tag is not string direction)return;
        bool horizontal=direction[0]=='W',increase=direction[1]=='+';
        double inner=horizontal?album.PixelWidth-2.0*album.Margin:album.PixelHeight-2.0*album.Margin;
        if(inner<=0)return;double delta=(increase?30:-30)/inner;
        var sheet=album.Sheets[currentSheet];
        if(!LayoutResizeEngine.TryResizeSelected(sheet.Slots,new[]{selectedSlot},horizontal,true,delta,false,album.PixelWidth,album.PixelHeight,album.Margin,album.Gap,out var changed))
        {StatusText.Text="Frame cannot resize farther in that direction.";return;}
        Remember();sheet.Slots=changed;sheet.Approved=false;QueuePreview();StatusText.Text=$"Frame {(horizontal?"width":"height")} changed by 30 px.";
    }
    void ChangeLayout(int delta){if(album==null)return;var sheet=album.Sheets[currentSheet];Remember();sheet.LayoutVariant=sheet.LayoutVariant<0?(delta>0?0:2):(sheet.LayoutVariant+delta+3)%3;AlbumEngine.Reflow(sheet);RefreshEditor();}
    void PreviousLayout_Click(object sender,RoutedEventArgs e)=>ChangeLayout(-1);
    void NextLayout_Click(object sender,RoutedEventArgs e)=>ChangeLayout(1);
    void Approve_Click(object sender,RoutedEventArgs e){if(album==null)return;Remember();album.Sheets[currentSheet].Approved=true;RefreshEditor();StatusText.Text=$"Sheet {currentSheet+1:D2} approved.";}
    void SaveStyle_Click(object sender,RoutedEventArgs e)
    {
        if(album==null)return;
        try{int count=new TrainingCatalog().AddApprovedAlbum(album);StatusText.Text=$"Saved {count} approved sheet layouts as style feedback. Open Tools → Album Style Trainer → Train / Update Style to apply them.";}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Album style feedback",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    void Fold_Changed(object sender,RoutedEventArgs e){if(updating||album==null)return;album.ShowFoldGuide=FoldCheck.IsChecked==true;FitPreview();}
    void FoldSafe_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){FoldSafe_Changed(sender,e);e.Handled=true;}}
    void FoldSafe_Changed(object sender,RoutedEventArgs e){if(album==null)return;if(double.TryParse(FoldSafeBox.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double value)&&value>=0&&value<=2){album.FoldSafeZoneInches=value;FitPreview();}else StatusText.Text="Fold safe zone must be 0–2 inches.";}
    void MoveSelected(int delta)
    {
        if(album==null||selectedSlot<0)return;int target=currentSheet+delta;if(target<0||target>=album.Sheets.Count)return;
        Remember();AlbumEngine.MovePhoto(album,currentSheet,selectedSlot,target,album.Sheets[target].PhotoIndices.Count);currentSheet=target;selectedSlot=album.Sheets[target].PhotoIndices.Count-1;RefreshEditor();
    }
    void MovePrevious_Click(object sender,RoutedEventArgs e)=>MoveSelected(-1);
    void MoveNext_Click(object sender,RoutedEventArgs e)=>MoveSelected(1);
    void Unassign_Click(object sender,RoutedEventArgs e)
    {
        if(album==null||selectedSlot<0)return;Remember();var sheet=album.Sheets[currentSheet];int index=sheet.PhotoIndices[selectedSlot];sheet.PhotoIndices.RemoveAt(selectedSlot);album.Unassigned.Add(index);AlbumEngine.Reflow(sheet);selectedSlot=-1;RefreshEditor();
    }
    void Assign_Click(object sender,RoutedEventArgs e)
    {
        if(album==null||UnassignedList.SelectedIndex<0)return;Remember();int index=album.Unassigned[UnassignedList.SelectedIndex];album.Unassigned.Remove(index);var sheet=album.Sheets[currentSheet];sheet.PhotoIndices.Add(index);AlbumEngine.Reflow(sheet);selectedSlot=sheet.PhotoIndices.Count-1;RefreshEditor();
    }
    void PhotoList_Drag(object sender,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed||album==null||PhotoList.SelectedIndex<0)return;
        Point point=e.GetPosition(PhotoList);if((point-photoDragStart).Length<4)return;
        DragDrop.DoDragDrop(PhotoList,new DataObject("AlbumPhotoDrag",new PhotoDrag(currentSheet,PhotoList.SelectedIndex)),DragDropEffects.Move);
    }
    void PhotoList_Down(object sender,MouseButtonEventArgs e)=>photoDragStart=e.GetPosition(PhotoList);
    void SheetList_DragOver(object sender,DragEventArgs e){e.Effects=e.Data.GetDataPresent("AlbumPhotoDrag")?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;}
    void SheetList_Drop(object sender,DragEventArgs e)
    {
        if(album==null||e.Data.GetData("AlbumPhotoDrag") is not PhotoDrag drag)return;
        var item=SheetList.ContainerFromElement((DependencyObject)e.OriginalSource) as ListBoxItem;
        if(item==null)return;int target=SheetList.ItemContainerGenerator.IndexFromContainer(item);
        if(target<0||target==drag.Sheet)return;Remember();AlbumEngine.MovePhoto(album,drag.Sheet,drag.Slot,target,album.Sheets[target].PhotoIndices.Count);
        currentSheet=target;selectedSlot=album.Sheets[target].PhotoIndices.Count-1;RefreshEditor();e.Handled=true;
    }
    void PhotoList_Drop(object sender,DragEventArgs e)
    {
        if(album==null||e.Data.GetData("AlbumPhotoDrag") is not PhotoDrag drag)return;
        var item=PhotoList.ContainerFromElement((DependencyObject)e.OriginalSource) as ListBoxItem;if(item==null)return;
        int target=PhotoList.ItemContainerGenerator.IndexFromContainer(item);if(target<0)return;
        Remember();if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))AlbumEngine.SwapPhotos(album,drag.Sheet,drag.Slot,currentSheet,target);else AlbumEngine.MovePhoto(album,drag.Sheet,drag.Slot,currentSheet,target);
        selectedSlot=Math.Clamp(target,0,album.Sheets[currentSheet].PhotoIndices.Count-1);RefreshEditor();e.Handled=true;
    }
    void Save_Click(object sender,RoutedEventArgs e)
    {
        if(album==null)return;
        if(projectPath==null){var dialog=new SaveFileDialog{Filter="Album project|*.albumproject",DefaultExt=".albumproject"};if(dialog.ShowDialog()!=true)return;projectPath=dialog.FileName;}
        try{AlbumEngine.Save(album,projectPath);StatusText.Text=$"Album saved: {projectPath}";}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Save album",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    void Open_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Filter="Album project|*.albumproject"};if(dialog.ShowDialog()!=true)return;
        try{album=AlbumEngine.Load(dialog.FileName);projectPath=dialog.FileName;currentSheet=0;selectedSlot=-1;undo.Clear();redo.Clear();ShowEditor();StatusText.Text=$"Opened album: {projectPath}";}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Open album",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    async void Export_Click(object sender,RoutedEventArgs e)
    {
        if(album==null)return;if(album.Unassigned.Count>0){StatusText.Text="Assign all photos before exporting.";return;}
        var dialog=new OpenFolderDialog{Title="Choose folder for album JPG spreads"};if(dialog.ShowDialog()!=true)return;
        try{EditorPanel.IsEnabled=false;StatusText.Text="Exporting original-resolution album spreads…";
            var progress=new Progress<int>(p=>StatusText.Text=$"Exporting album: {p}%");
            var outputs=await AlbumRenderService.ExportAll(album,dialog.FolderName,progress);
            StatusText.Text=$"Exported {outputs.Count} print-ready spreads to {Path.GetDirectoryName(outputs[0])}.";}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Album export",MessageBoxButton.OK,MessageBoxImage.Warning);StatusText.Text="Album export stopped.";}
        finally{EditorPanel.IsEnabled=true;}
    }
    void Window_KeyDown(object sender,KeyEventArgs e)
    {
        if(Keyboard.FocusedElement is TextBox)return;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&e.Key==Key.Z){Undo_Click(sender,e);e.Handled=true;return;}
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&e.Key==Key.Y){Redo_Click(sender,e);e.Handled=true;return;}
        if(Keyboard.Modifiers!=ModifierKeys.None)return;
        if(e.Key==Key.Down){ChangeLayout(1);e.Handled=true;}
        else if(e.Key==Key.Up){ChangeLayout(-1);e.Handled=true;}
    }
    void Credit_Navigate(object sender,RequestNavigateEventArgs e){Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;}
}
