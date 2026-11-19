using System.Globalization;

namespace LasReader.Core;

public sealed class LasCurve
{
    private readonly Dictionary<int, string>? exactValues;

    internal LasCurve(LasHeaderEntry header, List<double> values, Dictionary<int, string>? exactValues)
    {
        Header = header;
        Values = values.AsReadOnly();
        this.exactValues = exactValues;
    }

    public LasHeaderEntry Header { get; }
    public string Mnemonic => Header.Mnemonic;
    public string Unit => Header.Unit;
    public string Description => Header.Description;

    /// <summary>Values in file order. Missing samples are represented by NaN.</summary>
    public IReadOnlyList<double> Values { get; }

    /// <summary>Returns the numeric value as invariant text, retaining extra source precision.
    /// Returns null for missing measurements. Formatting such as trailing zeros can change.</summary>
    public string? GetValueText(int sampleIndex)
    {
        double value = Values[sampleIndex];
        if (double.IsNaN(value))
            return null;
        return exactValues is not null && exactValues.TryGetValue(sampleIndex, out string? original)
            ? original : value.ToString("R", CultureInfo.InvariantCulture);
    }
}
