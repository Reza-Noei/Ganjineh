using System.Windows;
using PasswordManager.Desktop.ViewModels;

namespace PasswordManager.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
