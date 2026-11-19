using System.Windows;
using LasViewer.ViewModels;
using Microsoft.Win32;

namespace LasViewer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var viewModel = new MainViewModel(ChooseLasFile);
        MainWindow = new MainWindow { DataContext = viewModel };
        MainWindow.Show();

        if (e.Args.Length > 0)
            await viewModel.OpenFileAsync(e.Args[0]);
    }

    private string? ChooseLasFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open LAS file",
            Filter = "LAS files (*.las)|*.las|All files (*.*)|*.*"
        };
        return dialog.ShowDialog(MainWindow) == true ? dialog.FileName : null;
    }
}
