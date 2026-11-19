using System.ComponentModel;
using System.Windows.Media;
using LasReader.Core;

namespace LasViewer.ViewModels;

public sealed class CurveOption(LasCurve curve, bool isSelected, int colorIndex) : INotifyPropertyChanged
{
    private static readonly Color[] DefaultColors =
    [
        Color.FromRgb(0x00, 0x6D, 0x83), Color.FromRgb(0x15, 0x5B, 0xC4), Color.FromRgb(0x9A, 0x4D, 0x19),
        Color.FromRgb(0x73, 0x47, 0xA1), Color.FromRgb(0x33, 0x73, 0x42), Color.FromRgb(0xA7, 0x3A, 0x54)
    ];

    private bool _isSelected = isSelected;
    private bool _fitVisibleRange;
    private Color _color = DefaultColors[colorIndex % DefaultColors.Length];

    public event PropertyChangedEventHandler? PropertyChanged;

    public LasCurve Curve { get; } = curve;

    public string Label => string.IsNullOrWhiteSpace(Curve.Unit)
        ? Curve.Mnemonic
        : $"{Curve.Mnemonic} [{Curve.Unit}]";

    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value)
                return;

            _color = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public bool FitVisibleRange
    {
        get => _fitVisibleRange;
        set
        {
            if (_fitVisibleRange == value)
                return;

            _fitVisibleRange = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FitVisibleRange)));
        }
    }
}
