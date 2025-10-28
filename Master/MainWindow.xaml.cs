using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Master;

public partial class MainWindow : Window
{
    private MainViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        viewModel = new MainViewModel();
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        Task.Run(() => viewModel.SendJob());
    }
}