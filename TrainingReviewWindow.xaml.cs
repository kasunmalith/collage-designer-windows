using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Point=System.Windows.Point;
using Path=System.IO.Path;

namespace CollageDesigner;
public partial class TrainingReviewWindow : Window
{
    readonly TrainingCatalog catalog;readonly TrainingAlbumInfo album;List<TrainingSheetRecord> sheets=[];List<TrainingSlotRecord> regions=[];
    bool drawing;Point start;Rectangle? draft;
    sealed record PhotoChoice(string Name,string Path);
    public TrainingReviewWindow(TrainingCatalog catalog,TrainingAlbumInfo album)
    {
        this.catalog=catalog;this.album=album;InitializeComponent();
        SourcePhotoBox.ItemsSource=album.PhotoFiles.Select(p=>new PhotoChoice(Path.GetFileName(p),p)).ToList();
        RefreshSheets();
    }
    void RefreshSheets()
    {
        sheets=catalog.Sheets(album.SourceFolder);
        SheetList.ItemsSource=sheets.Select(s=>$"{s.Number:D3}.jpg   {s.Status}   ({s.PhotoCount})").ToList();
        if(sheets.Count>0)SheetList.SelectedIndex=0;
    }
    TrainingSheetRecord? CurrentSheet=>SheetList.SelectedIndex>=0&&SheetList.SelectedIndex<sheets.Count?sheets[SheetList.SelectedIndex]:null;
    void Sheet_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(CurrentSheet is not { } sheet)return;
        try
        {
            var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(sheet.SourcePath);image.DecodePixelWidth=1400;image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();
            SheetImage.Source=image;Fit();RefreshRegions();StatusText.Text=$"Review sheet {sheet.Number:D3}: {sheet.Status}";
        }
        catch(Exception ex){StatusText.Text=$"Could not open sheet: {ex.Message}";}
    }
    void Fit()
    {
        if(SheetImage.Source is not BitmapSource bitmap)return;
        double factor=Math.Min(Math.Max(1,PreviewHost.ActualWidth-24)/bitmap.PixelWidth,Math.Max(1,PreviewHost.ActualHeight-24)/bitmap.PixelHeight);
        SheetImage.Width=RegionCanvas.Width=bitmap.PixelWidth*factor;SheetImage.Height=RegionCanvas.Height=bitmap.PixelHeight*factor;DrawRegions();
    }
    void Preview_SizeChanged(object sender,SizeChangedEventArgs e)=>Fit();
    void RefreshRegions()
    {
        if(CurrentSheet is not { } sheet)return;
        regions=catalog.Slots(sheet.Id);RegionList.ItemsSource=regions.Select((r,i)=>$"Region {i+1}: {(r.PhotoPath==null?"Unmatched":Path.GetFileName(r.PhotoPath))}  {r.Confidence:P0}").ToList();DrawRegions();
    }
    void DrawRegions()
    {
        RegionCanvas.Children.Clear();if(RegionCanvas.Width<=0||RegionCanvas.Height<=0)return;
        for(int i=0;i<regions.Count;i++)
        {
            var s=regions[i].Slot;var rect=new Rectangle{Width=s.Width*RegionCanvas.Width,Height=s.Height*RegionCanvas.Height,Stroke=i==RegionList.SelectedIndex?Brushes.Yellow:regions[i].PhotoPath==null?Brushes.Orange:Brushes.LimeGreen,StrokeThickness=3,Fill=Brushes.Transparent,IsHitTestVisible=false};
            Canvas.SetLeft(rect,s.X*RegionCanvas.Width);Canvas.SetTop(rect,s.Y*RegionCanvas.Height);RegionCanvas.Children.Add(rect);
        }
        if(draft!=null)RegionCanvas.Children.Add(draft);
    }
    void Region_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(RegionList.SelectedIndex>=0&&RegionList.SelectedIndex<regions.Count)
        {var path=regions[RegionList.SelectedIndex].PhotoPath;SourcePhotoBox.SelectedItem=SourcePhotoBox.Items.OfType<PhotoChoice>().FirstOrDefault(p=>p.Path.Equals(path,StringComparison.OrdinalIgnoreCase));}
        DrawRegions();
    }
    void Canvas_Down(object sender,MouseButtonEventArgs e)
    {
        Point p=e.GetPosition(RegionCanvas);if(!drawing)
        {
            int hit=regions.FindLastIndex(r=>p.X>=r.Slot.X*RegionCanvas.Width&&p.X<=(r.Slot.X+r.Slot.Width)*RegionCanvas.Width&&p.Y>=r.Slot.Y*RegionCanvas.Height&&p.Y<=(r.Slot.Y+r.Slot.Height)*RegionCanvas.Height);
            RegionList.SelectedIndex=hit;return;
        }
        start=p;draft=new Rectangle{Stroke=Brushes.Yellow,StrokeThickness=2,Fill=Brushes.Transparent,IsHitTestVisible=false};RegionCanvas.CaptureMouse();DrawRegions();
    }
    void Canvas_Move(object sender,MouseEventArgs e)
    {
        if(!drawing||draft==null)return;Point p=e.GetPosition(RegionCanvas);
        double left=Math.Clamp(Math.Min(p.X,start.X),0,RegionCanvas.Width),top=Math.Clamp(Math.Min(p.Y,start.Y),0,RegionCanvas.Height);
        double right=Math.Clamp(Math.Max(p.X,start.X),0,RegionCanvas.Width),bottom=Math.Clamp(Math.Max(p.Y,start.Y),0,RegionCanvas.Height);
        draft.Width=right-left;draft.Height=bottom-top;Canvas.SetLeft(draft,left);Canvas.SetTop(draft,top);
    }
    void Canvas_Up(object sender,MouseButtonEventArgs e)
    {
        if(!drawing||draft==null||CurrentSheet is not { } sheet)return;
        Canvas_Move(sender,e);RegionCanvas.ReleaseMouseCapture();
        double width=draft.Width/RegionCanvas.Width,height=draft.Height/RegionCanvas.Height;
        if(width>.01&&height>.01)
        {
            var slot=new LayoutSlot{X=Canvas.GetLeft(draft)/RegionCanvas.Width,Y=Canvas.GetTop(draft)/RegionCanvas.Height,Width=width,Height=height};
            catalog.AddSlot(sheet.Id,slot,(SourcePhotoBox.SelectedItem as PhotoChoice)?.Path);StatusText.Text="Photo region added. Assign its source JPG, then validate the sheet.";
        }
        draft=null;drawing=false;RefreshRegions();
    }
    void Add_Click(object sender,RoutedEventArgs e){if(CurrentSheet==null)return;drawing=true;StatusText.Text="Draw a rectangle around a photo on the spread.";}
    void Remove_Click(object sender,RoutedEventArgs e)
    {
        if(CurrentSheet is not { } sheet||RegionList.SelectedIndex<0)return;catalog.DeleteSlot(regions[RegionList.SelectedIndex].Id,sheet.Id);RefreshRegions();StatusText.Text="Region removed. Draw a corrected one if needed.";
    }
    void Assign_Click(object sender,RoutedEventArgs e)
    {
        if(RegionList.SelectedIndex<0||SourcePhotoBox.SelectedItem is not PhotoChoice photo)return;
        catalog.SetSlotPhoto(regions[RegionList.SelectedIndex].Id,photo.Path);RefreshRegions();StatusText.Text=$"Matched region to {photo.Name}.";
    }
    void Validate_Click(object sender,RoutedEventArgs e)
    {
        if(CurrentSheet is not { } sheet)return;
        if(!catalog.ValidateSheet(sheet.Id)){StatusText.Text="Every region needs a different original JPG before validation.";return;}
        int selected=SheetList.SelectedIndex;RefreshSheets();SheetList.SelectedIndex=selected;StatusText.Text=$"Sheet {sheet.Number:D3} validated for style training.";
    }
    void Credit_Navigate(object sender,RequestNavigateEventArgs e){Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;}
}
