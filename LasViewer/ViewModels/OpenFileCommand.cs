using System.Windows.Input;

namespace LasViewer.ViewModels;

internal sealed class OpenFileCommand(MainViewModel viewModel, Func<string?> chooseFile) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !viewModel.IsLoading;

    public async void Execute(object? parameter)
    {
        if (CanExecute(parameter) && chooseFile() is { } path)
            await viewModel.OpenFileAsync(path);
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
