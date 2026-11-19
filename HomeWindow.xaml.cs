using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace CollageDesigner;
public partial class HomeWindow : Window
{
    public HomeWindow(){InitializeComponent();RefreshTrainerState();}
    void RefreshTrainerState(){try{int count=new TrainingCatalog().LearnedLayoutCount();TrainerState.Text=count==0?"Your Album Style has not been trained yet.":$"Your Album Style has {count} learned layout families.";}catch{TrainerState.Text="Album style training is available under Tools.";}}
    void Collage_Click(object sender,RoutedEventArgs e)=>new MainWindow{Owner=this}.ShowDialog();
    void Album_Click(object sender,RoutedEventArgs e)=>new AlbumWindow{Owner=this}.ShowDialog();
    void Trainer_Click(object sender,RoutedEventArgs e){new TrainerWindow{Owner=this}.ShowDialog();RefreshTrainerState();}
    void Credit_Navigate(object sender,RequestNavigateEventArgs e){Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;}
}
