using System.Windows;
using GameVault.Core;

namespace GameVault.App;

public partial class MainWindow : Window
{
    public MainWindow(ApplicationPaths paths)
    {
        InitializeComponent();
        DataContext = paths;
    }
}
