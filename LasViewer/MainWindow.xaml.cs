using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LasViewer.ViewModels;
using DrawingColor = System.Drawing.Color;
using Forms = System.Windows.Forms;

namespace LasViewer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Plot.AttachDepthScrollBar(DepthScrollBar);
    }

    private void RemoveLastReader(object sender, RoutedEventArgs e) => Plot.UndoLastReader();

    private void ClearAllReaders(object sender, RoutedEventArgs e) => Plot.ClearReaders();

    private void ChangeCurveColor(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CurveOption curve })
            return;

        var current = curve.Color;
        using var dialog = new Forms.ColorDialog
        {
            Color = DrawingColor.FromArgb(current.R, current.G, current.B),
            FullOpen = true
        };

        var owner = new DialogOwner(new WindowInteropHelper(this).Handle);
        if (dialog.ShowDialog(owner) == Forms.DialogResult.OK)
        {
            var chosen = dialog.Color;
            curve.Color = Color.FromRgb(chosen.R, chosen.G, chosen.B);
        }
    }

    private sealed class DialogOwner(IntPtr handle) : Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
