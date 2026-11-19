using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows.Input;
using LasReader.Core;

namespace LasViewer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const int DefaultTrackCount = 3;
    private readonly OpenFileCommand _openFileCommand;
    private readonly MoveCurveCommand _moveUpCommand;
    private readonly MoveCurveCommand _moveDownCommand;
    private ObservableCollection<CurveOption> _curves = [];

    public MainViewModel(Func<string?> chooseFile)
    {
        _openFileCommand = new OpenFileCommand(this, chooseFile);
        _moveUpCommand = new MoveCurveCommand(this, -1);
        _moveDownCommand = new MoveCurveCommand(this, 1);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand OpenCommand => _openFileCommand;
    public ICommand MoveUpCommand => _moveUpCommand;
    public ICommand MoveDownCommand => _moveDownCommand;
    public string? FileName { get; private set; }
    public string? FilePath { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool HasError => ErrorMessage is not null;
    public string? FileWarning { get; private set; }
    public bool HasFileWarning => FileWarning is not null;
    public bool IsLoading { get; private set; }
    public LasCurve? IndexCurve { get; private set; }
    public IReadOnlyList<CurveOption> Curves => _curves;
    public IReadOnlyList<CurveOption> SelectedCurves { get; private set; } = [];
    public bool HasFile => FileName is not null;
    public bool HasPlot => IndexCurve?.Values.Count > 0 && SelectedCurves.Count > 0;

    public string FileSummary => $"{IndexCurve?.Values.Count ?? 0:N0} samples  ·  {Curves.Count:N0} curves";

    public string EmptyPlotMessage => IsLoading ? "Reading LAS file…"
        : !HasFile ? "Open a LAS file to view its curves."
        : IndexCurve?.Values.Count is null or 0 ? "This LAS file contains no samples."
        : Curves.Count == 0 ? "This LAS file contains only an index curve."
        : "Select curves to display them here.";

    public async Task OpenFileAsync(string path)
    {
        if (IsLoading)
            return;

        IsLoading = true;
        ErrorMessage = null;
        PublishLoadState();

        try
        {
            var fullPath = Path.GetFullPath(path);
            var document = await Task.Run(() =>
            {
                using var reader = new StreamReader(fullPath);
                return LasFileReader.Read(reader);
            });

            foreach (var option in Curves)
                option.PropertyChanged -= OnCurveOptionChanged;

            FilePath = fullPath;
            FileName = Path.GetFileName(fullPath);
            FileWarning = document.Warnings.Count > 0 ? string.Join(" ", document.Warnings) : null;
            IndexCurve = document.Curves.FirstOrDefault();
            _curves = new ObservableCollection<CurveOption>(document.Curves.Skip(1)
                .Select((curve, index) => new CurveOption(curve, index < DefaultTrackCount, index)));

            foreach (var option in Curves)
                option.PropertyChanged += OnCurveOptionChanged;

            UpdateSelection();
            Publish(nameof(FilePath), nameof(FileName), nameof(IndexCurve), nameof(Curves),
                nameof(HasFile), nameof(FileSummary), nameof(FileWarning), nameof(HasFileWarning));
            RefreshMoveCommands();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException or FormatException)
        {
            ErrorMessage = $"Could not open the LAS file. {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            PublishLoadState();
        }
    }

    private void OnCurveOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CurveOption.IsSelected) or nameof(CurveOption.FitVisibleRange))
            UpdateSelection();
    }

    private void UpdateSelection()
    {
        SelectedCurves = Curves.Where(option => option.IsSelected).ToArray();
        Publish(nameof(SelectedCurves), nameof(HasPlot), nameof(EmptyPlotMessage));
    }

    private void RefreshMoveCommands()
    {
        _moveUpCommand.Refresh();
        _moveDownCommand.Refresh();
    }

    private void PublishLoadState()
    {
        _openFileCommand.Refresh();
        Publish(nameof(IsLoading), nameof(HasError), nameof(ErrorMessage), nameof(EmptyPlotMessage));
    }

    private void Publish(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class MoveCurveCommand(MainViewModel viewModel, int direction) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => TryGetMove(parameter, out _, out _);

        public void Execute(object? parameter)
        {
            if (!TryGetMove(parameter, out var index, out var destination))
                return;

            viewModel._curves.Move(index, destination);
            viewModel.UpdateSelection();
            viewModel.RefreshMoveCommands();
        }

        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        private bool TryGetMove(object? parameter, out int index, out int destination)
        {
            index = parameter is CurveOption option ? viewModel._curves.IndexOf(option) : -1;
            destination = index + direction;
            return index >= 0 && destination >= 0 && destination < viewModel._curves.Count;
        }
    }
}
