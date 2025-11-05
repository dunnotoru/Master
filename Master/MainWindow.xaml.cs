using System.Windows;
using Microsoft.Win32;

namespace Master;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog fileDialog = new OpenFileDialog();
        bool res = fileDialog.ShowDialog() ?? false;
        if (!res)
        {
            return;
        }

        string file = fileDialog.FileName;
        _viewModel.FileToOpen = file;
    }
}