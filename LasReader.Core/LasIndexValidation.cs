using System.Globalization;

namespace LasReader.Core;

internal static class LasIndexValidation
{
    internal static List<string> Check(List<LasHeaderEntry> wellInfo, LasCurve index, LasVersion version,
        string nullToken)
    {
        var warnings = new List<string>();
        if (index.Values.Count == 0)
            return warnings;

        foreach (var field in wellInfo)
        {
            string name = field.Mnemonic.ToUpperInvariant();
            if (name is not ("STRT" or "STOP" or "STEP"))
                continue;
            if (!double.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double declared)
                || !double.IsFinite(declared) || field.Value.Contains('\0'))
            {
                warnings.Add($"The {name} header is not a finite number.");
                continue;
            }
            if (name == "STEP")
            {
                if (declared != 0 && HasUnexpectedStep(index, declared))
                    warnings.Add("The index spacing differs from the STEP header.");
                continue;
            }
            // A live LAS 3.0 log can have an unknown stop index.
            if (version == LasVersion.V30 && name == "STOP" &&
                LasDataReader.AreEquivalentNumbers(field.Value, nullToken))
                continue;
            string? measured = index.GetValueText(name == "STRT" ? 0 : index.Values.Count - 1);
            if (measured is null || !LasDataReader.AreEquivalentNumbers(field.Value, measured))
                warnings.Add($"The {name} header differs from the measured index ({measured ?? "missing"}).");
        }
        return warnings;
    }

    private static bool HasUnexpectedStep(LasCurve index, double step)
    {
        for (int i = 1; i < index.Values.Count; i++)
        {
            double previous = index.Values[i - 1];
            double current = index.Values[i];
            // Allow for rounding when subtracting two binary64 index coordinates.
            double magnitude = Math.Max(Math.Abs(current), Math.Abs(previous));
            double tolerance = 2 * (magnitude - Math.BitDecrement(magnitude));
            if (!double.IsFinite(current) || !double.IsFinite(previous) ||
                Math.Abs((current - previous) - step) > tolerance)
                return true;
        }
        return false;
    }
}
