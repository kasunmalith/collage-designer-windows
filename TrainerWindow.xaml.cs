using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using SkiaSharp;

namespace CollageDesigner;
public partial class TrainerWindow : Window
{
    readonly TrainingCatalog catalog=new();readonly ManualResetEventSlim pauseGate=new(true);List<TrainingAlbumInfo> albums=[];CancellationTokenSource? analysisToken;string? root;bool paused;
    public TrainerWindow(){InitializeComponent();UpdateStats();}
    void UpdateStats(){var s=catalog.Dashboard();StatsText.Text=$"Albums Added: {s.Albums:N0}    Sheets Analyzed: {s.Sheets:N0}    Used Photos: {s.UsedPhotos:N0}    Matched Photos: {s.MatchedPhotos:N0}\nValidated Sheets: {s.ValidatedSheets:N0}    Needs Review: {s.NeedsReview:N0}    Ratio Families: {(s.RatioFamilies.Length==0?"None":s.RatioFamilies)}";}
    void Example_Click(object sender,RoutedEventArgs e)=>MessageBox.Show(this,"All Albums/\n  Couple Name 001/\n    Used Photos/  (original JPGs)\n    All Sheets/   (final flattened JPG spreads)\n  Couple Name 002/\n    Used Photos/\n    All Sheets/\n\nNames are matched without regard to letter case. Training IDs are assigned automatically.","Required folder structure");
    async void SelectFolder_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog{Title="Select All Albums folder"};if(dialog.ShowDialog()!=true)return;root=dialog.FolderName;
        try{StatusText.Text="Scanning album folders…";albums=await Task.Run(()=>TrainingFolderScanner.Scan(root));catalog.SaveScan(albums);RefreshAlbums();UpdateStats();StatusText.Text=$"Found {albums.Count} album folders; {albums.Count(a=>a.Status=="READY")} ready, {albums.Count(a=>a.Status!="READY")} need attention.";}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Scan failed",MessageBoxButton.OK,MessageBoxImage.Warning);StatusText.Text="Scan failed.";}
    }
    void RefreshAlbums()
    {
        var visible=albums.Select(a=>(Album:a,NeedsReview:catalog.Sheets(a.SourceFolder).Count(s=>s.Status=="NEEDS REVIEW"))).ToList();
        if(ProblemsOnly.IsChecked==true)visible=visible.Where(x=>x.Album.Status!="READY"||x.NeedsReview>0).ToList();
        AlbumList.Tag=visible.Select(x=>x.Album).ToList();
        AlbumList.ItemsSource=visible.Select(x=>$"{x.Album.TrainingId}   {x.Album.Label}   ·   {x.Album.Status}  {x.Album.Problem}  {(x.NeedsReview>0?$"{x.NeedsReview} sheets need review":"")}").ToList();
    }
    void Problems_Changed(object sender,RoutedEventArgs e){if(AlbumList!=null)RefreshAlbums();}
    void Album_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(AlbumList.Tag is not List<TrainingAlbumInfo> visible||AlbumList.SelectedIndex<0||AlbumList.SelectedIndex>=visible.Count)return;
        var a=visible[AlbumList.SelectedIndex];DetailsText.Text=$"{a.TrainingId}\n{a.Label}\n\nStatus: {a.Status}\nProblem: {a.Problem}\nUsed photos: {a.PhotoFiles.Count}\nSheets: {a.SheetFiles.Count}\nRatio family: {(a.RatioFamily?.ToString("0.0")??"Needs review")}:1\n\n{a.SourceFolder}";
        SheetList.ItemsSource=a.SheetFiles.Select(Path.GetFileName).ToList();
    }
    async void Analyze_Click(object sender,RoutedEventArgs e)
    {
        if(albums.Count==0){StatusText.Text="Select the All Albums folder first.";return;}
        AnalyzeButton.IsEnabled=false;analysisToken=new CancellationTokenSource();var token=analysisToken.Token;paused=false;pauseGate.Set();PauseButton.Content="Pause Analysis";
        try
        {
            int completed=0,changed=0;foreach(var a in albums)
            {
                pauseGate.Wait(token);token.ThrowIfCancellationRequested();
                if(a.Status=="READY"&&catalog.NeedsAnalysis(a))
                {
                    await Task.Run(()=>
                    {
                        var candidates=new List<(string Path,SKBitmap Preview)>();
                        try
                        {
                            foreach(string path in a.PhotoFiles){pauseGate.Wait(token);token.ThrowIfCancellationRequested();try{candidates.Add((path,Rendering.LoadUpright(path,260)));}catch{}}
                            var results=new List<SheetAnalysis>();
                            foreach(string path in a.SheetFiles){pauseGate.Wait(token);token.ThrowIfCancellationRequested();results.Add(TrainingSheetAnalyzer.Analyze(path,candidates,token));}
                            token.ThrowIfCancellationRequested();catalog.SaveSheetMetadata(a);catalog.SaveAnalysis(a,results);
                        }
                        finally{foreach(var item in candidates)item.Preview.Dispose();}
                    },token);changed++;
                }
                completed++;Progress.Value=100.0*completed/albums.Count;StatusText.Text=$"Indexing album {completed} / {albums.Count}…";
                await Task.Yield();
            }
            UpdateStats();RefreshAlbums();StatusText.Text=$"Analyzed {changed} new or changed albums. Check sheets needing review before using their layouts.";
        }
        catch(OperationCanceledException){StatusText.Text="Analysis canceled. Completed albums remain indexed.";}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Analysis failed",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{AnalyzeButton.IsEnabled=true;paused=false;pauseGate.Set();PauseButton.Content="Pause Analysis";}
    }
    void Cancel_Click(object sender,RoutedEventArgs e)=>analysisToken?.Cancel();
    void Pause_Click(object sender,RoutedEventArgs e)
    {
        paused=!paused;if(paused){pauseGate.Reset();PauseButton.Content="Resume Analysis";StatusText.Text="Analysis paused.";}
        else{pauseGate.Set();PauseButton.Content="Pause Analysis";StatusText.Text="Analysis resumed.";}
    }
    void Review_Click(object sender,RoutedEventArgs e)
    {
        if(AlbumList.Tag is not List<TrainingAlbumInfo> visible||AlbumList.SelectedIndex<0)return;
        var album=visible[AlbumList.SelectedIndex];if(catalog.Sheets(album.SourceFolder).Count==0){StatusText.Text="Analyze this album before opening sheet review.";return;}
        new TrainingReviewWindow(catalog,album){Owner=this}.ShowDialog();UpdateStats();RefreshAlbums();
    }
    void Train_Click(object sender,RoutedEventArgs e)
    {
        try{int count=catalog.TrainValidatedLayouts();StatusText.Text=count==0?"No validated sheets yet. Review and validate sheets first.":$"Style updated from {count} validated sheet layouts. New albums will use matching ratio and photo count.";}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Style training",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    void Credit_Navigate(object sender,RequestNavigateEventArgs e){Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;}
}
